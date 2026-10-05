using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// Movimiento del jugador con prediccion del cliente y autoridad del servidor.
///
/// Reglas del modelo:
///  - La FUENTE DE VERDAD de la simulacion es _pos / _lookDir, NO el transform.
///  - El servidor (y el host) escribe el transform directamente.
///  - El cliente duenio muestra su prediccion con AnticipatedNetworkTransform
///    (AnticipateMove / AnticipateRotate).
/// </summary>

/// <summary>
/// Movimiento con client anticipation 
///
/// Flujo:
///  - El dueño simula su input al instante y lo muestra con AnticipateMove/AnticipateRotate.
///  - Ese mismo input se envia al servidor por RPC y el servidor aplica la MISMA logica.
///  - El estado autoritativo vuelve solo, por el AnticipatedNetworkTransform (no hay RPC de estado).
///  - Cuando llega, el sistema llama a OnReanticipate: volvemos al estado del servidor,
///    re-aplicamos los inputs que el servidor todavia no vio y decidimos si ignorar,
///    suavizar o saltar a la correccion.
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    private const float STOPVALUE = 0.01f;
    private const int MAX_HISTORY = 256;

    [Header("Horizontal Movement")]
    [SerializeField, Min(0)] private float _acceleration = 3f;
    [SerializeField, Min(0)] private float _deceleration = 5f;
    [SerializeField, Min(0)] private float _maxSpeed = 5f;

    [Header("Vertical Movement")]
    [SerializeField, Min(0)] private float _jumpForce = 10f;
    [SerializeField, Min(0)] private float _gravityForce = 20f;
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private float _groundCheckDistance = 1.1f;

    [Header("Reconciliacion (cliente dueño)")]
    [Tooltip("Error menor a esto: se ignora la correccion y se conserva lo que el cliente ya mostraba.")]
    [SerializeField, Min(0)] private float _ignoreDistance = 0.25f;
    [Tooltip("Error entre _ignoreDistance y este valor: correccion suavizada. Mayor: salto directo.")]
    [SerializeField, Min(0)] private float _smoothDistance = 3f;
    [Tooltip("Duracion del suavizado en segundos. 0 = nunca suaviza (solo ignora o salta).")]
    [SerializeField, Min(0)] private float _smoothTime = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs;

    private AnticipatedNetworkTransform _ant;
    private LookToMause _lookToMause;
    private PlayerInput _playerInput;

    // Input local (solo el dueño)
    private Vector2 _currentMoveInput;
    private bool _jumpRequested;
    private int _inputCounter;

    // Estado de la simulacion (fuente de verdad: NO el transform)
    private Vector3 _pos;
    private Vector3 _lookDir = Vector3.forward;

    // Parte del estado que el servidor no nos manda: la guardamos nosotros por cada input.
    private Vector3 _actualMovement;
    private float _verticalVelocity;
    private float _actualSpeed;
        
    private struct SimState
    {
        public float VerticalVelocity;
        public float ActualSpeed;
        public Vector3 ActualMovement;
    }

    // Historial de inputs del cliente dueño, con timestamp de red.
    private struct HistoryImput
    {
        public double Time;
        public InputData Input;
        public SimState StateAfter;
    }
    private readonly List<HistoryImput> _history = new List<HistoryImput>();

    // ------------------------------------------------------------------
    // OnNetworkSpawn
    // Habilita input y mouse solo para el dueño, toma la posicion
    // de spawn como estado inicial y, en el
    // cliente dueño (no host), activa la reanticipacion constante.
    // ------------------------------------------------------------------
    public override void OnNetworkSpawn()
    {
        _playerInput = GetComponent<PlayerInput>();
        _lookToMause = GetComponent<LookToMause>();
        _ant = GetComponent<AnticipatedNetworkTransform>();

        _playerInput.enabled = IsOwner;
        _lookToMause.enabled = IsOwner;

        _pos = transform.position;
        _lookDir = transform.forward;

        if (_ant == null)
        {
            Debug.LogError($"[{name}] Falta el AnticipatedNetworkTransform en el prefab del player.");
            return;
        }

        if (IsOwner && !IsServer) _ant.StaleDataHandling = StaleDataHandling.Reanticipate;
    }

    // ------------------------------------------------------------------
    // FixedUpdate
    // Solo corre para el dueño (cliente o host). Los jugadores remotos los
    // mueve el servidor desde el RPC, no desde aca.
    //  1. Arma el InputData de este tick.
    //  2. Lo simula y lo muestra de inmediato (sin esperar al servidor).
    //  3. Si es el host, termina: ya es la autoridad.
    //  4. Si es cliente: lo guarda en el historial y lo manda al servidor.
    // ------------------------------------------------------------------
    private void FixedUpdate()
    {
        if (!IsOwner) return;

        InputData input = new InputData
        {
            Tick = ++_inputCounter,
            MoveInput = _currentMoveInput,
            LookDirection = _lookToMause.GetOrientation(),
            JumpPressed = _jumpRequested
        };
        _jumpRequested = false;

        SimulateStep(input);
        ApplyToAnticipatedTransform();

        if (IsServer) return;

        _history.Add(new HistoryImput
        {
            Time = NetworkManager.LocalTime.Time,
            Input = input,
            StateAfter = CaptureState()
        });
        while (_history.Count > MAX_HISTORY) _history.RemoveAt(0);

        SubmitInputServerRpc(input);
    }

    // ------------------------------------------------------------------
    // SubmitInputServerRpc
    // Se ejecuta en el servidor cuando llega un input de un cliente.
    // Aplica exactamente la misma simulacion que el cliente. Como se llama
    // con AnticipateMove en el servidor, el componente actualiza solo el
    // estado autoritativo y lo replica a todos.
    // ------------------------------------------------------------------
    [Rpc(SendTo.Server)]
    private void SubmitInputServerRpc(InputData input)
    {
        SimulateStep(input);
        ApplyToAnticipatedTransform();
    }

    // ------------------------------------------------------------------
    // OnReanticipate
    // Lo llama el AnticipatedNetworkTransform cuando llega un estado nuevo
    // del servidor (StaleDataHandling = Reanticipate). Antes de llamarlo, el
    // sistema ya revirtio el estado anticipado al autoritativo.
    //  1. Guarda lo que el cliente estaba mostrando (previousState).
    //  2. Calcula a que momento corresponde el estado recibido.
    //  3. Restaura las velocidades que teniamos en ese momento.
    //  4. Re-aplica los inputs posteriores desde la posicion autoritativa.
    //  5. Compara con lo que se mostraba: ignora, suaviza o salta.
    // ------------------------------------------------------------------
    public override void OnReanticipate(double lastRoundTripTime)
    {
        if (!IsOwner || IsServer || _ant == null) return;

        var previousState = _ant.PreviousAnticipatedState;
        double authorityTime = NetworkManager.LocalTime.Time - lastRoundTripTime;

        // Ultimo input que el servidor ya habria procesado (el historial esta en orden).
        int lastOld = -1;
        for (int i = 0; i < _history.Count; i++)
        {
            if (_history[i].Time <= authorityTime) lastOld = i;
            else break;
        }

        if (lastOld >= 0) RestoreState(_history[lastOld].StateAfter);

        // Arrancamos desde el estado autoritativo.
        _pos = _ant.AnticipatedState.Position;

        // Re-simulamos lo que el servidor todavia no vio.
        for (int i = lastOld + 1; i < _history.Count; i++)
        {
            SimulateStep(_history[i].Input);

            HistoryImput item = _history[i];
            item.StateAfter = CaptureState();
            _history[i] = item;
        }

        if (lastOld >= 0) _history.RemoveRange(0, lastOld + 1);

        ApplyToAnticipatedTransform(); // AnticipatedState = resultado corregido

        float sqDist = (previousState.Position - _ant.AnticipatedState.Position).sqrMagnitude;
        if (_debugLogs)
            Debug.Log($"[Reanticipate] error={Mathf.Sqrt(sqDist):F3} rtt={lastRoundTripTime:F3} pendientes={_history.Count}");

        if (_smoothTime <= 0f) return; // modo Snap: queda el estado corregido

        if (sqDist <= _ignoreDistance * _ignoreDistance)
        {
            // Diferencia minima: conservamos lo que el cliente ya mostraba.
            _ant.AnticipateState(previousState);
            _pos = previousState.Position;
        }
        else if (sqDist < _smoothDistance * _smoothDistance)
        {
            _ant.Smooth(previousState, _ant.AnticipatedState, _smoothTime);
        }
        // Si es mayor: salto directo al estado corregido (ya aplicado).
    }

    // ------------------------------------------------------------------
    // SimulateStep
    // Un paso de simulacion (un FixedUpdate). Identico en cliente y servidor.
    // Solo toca _pos, _lookDir y las velocidades; nunca el transform.
    //  - Rotacion: guarda la direccion del mouse.
    //  - Suelo: raycast hacia abajo desde _pos.
    //  - Salto y gravedad: modifican _verticalVelocity.
    //  - Horizontal: acelera o frena hacia la velocidad objetivo.
    //  - Desplazamiento: SUMA velocidad * dt a _pos.
    // ------------------------------------------------------------------
    private void SimulateStep(InputData input)
    {
        float dt = Time.fixedDeltaTime;

        if (input.LookDirection != Vector3.zero)
            _lookDir = input.LookDirection;

        bool isGrounded = Physics.Raycast(_pos, Vector3.down, _groundCheckDistance, _groundLayer);

        if (input.JumpPressed && isGrounded)
            _verticalVelocity = _jumpForce;

        if (isGrounded && _verticalVelocity <= 0f)
            _verticalVelocity = 0f;
        else
            _verticalVelocity -= _gravityForce * dt;

        Vector2 clampedInput = Vector2.ClampMagnitude(input.MoveInput, 1f);
        bool isMoving = clampedInput.sqrMagnitude > STOPVALUE;
        float changeFactor = isMoving ? _acceleration : _deceleration;
        float targetSpeed = isMoving ? _maxSpeed : 0f;

        _actualSpeed = Mathf.MoveTowards(_actualSpeed, targetSpeed, changeFactor * dt);

        if (isMoving)
            _actualMovement = new Vector3(clampedInput.x, 0f, clampedInput.y).normalized;

        Vector3 velocity = _actualMovement * _actualSpeed;
        velocity.y = _verticalVelocity;

        _pos += velocity * dt;
    }

    // ------------------------------------------------------------------
    // ApplyToAnticipatedTransform
    // Traduce el estado simulado a lo que se ve. Se usa en cliente dueño Y
    // en servidor: el componente detecta quien es la autoridad por si solo.
    // El transform nunca se escribe a mano.
    // ------------------------------------------------------------------
    private void ApplyToAnticipatedTransform()
    {
        if (_ant == null) return;

        _ant.AnticipateMove(_pos);
        if (_lookDir.sqrMagnitude > 0.0001f)
            _ant.AnticipateRotate(Quaternion.LookRotation(_lookDir, Vector3.up));
    }

    // ------------------------------------------------------------------
    // CaptureState / RestoreState
    // Guardan y restauran las velocidades. El servidor no las envia, asi que
    // el cliente las recuerda por cada input para poder re-simular bien.
    // ------------------------------------------------------------------
    private SimState CaptureState() => new SimState
    {
        VerticalVelocity = _verticalVelocity,
        ActualSpeed = _actualSpeed,
        ActualMovement = _actualMovement
    };

    private void RestoreState(SimState s)
    {
        _verticalVelocity = s.VerticalVelocity;
        _actualSpeed = s.ActualSpeed;
        _actualMovement = s.ActualMovement;
    }

    // ------------------------------------------------------------------
    // OnMove / OnJump
    // Callbacks del PlayerInput (Behavior: Send Messages). Solo guardan el
    // input; se consume en el proximo FixedUpdate.
    // ------------------------------------------------------------------
    public void OnMove(InputValue action)
    {
        if (!IsOwner) return;
        _currentMoveInput = action.Get<Vector2>();
    }

    public void OnJump()
    {
        if (!IsOwner) return;
        _jumpRequested = true;
    }
}

/* Reporte que envia el cliente al servidor:
 * Almacena los imputs del cliente con el tick exacto en el que ocurrieron.
 * El cliente ejecuta instantaneamente sus imputs de manera local para no sentir lag.
 * A la par, crea un reporte con los datos de la accion en ese momento
 */
public struct InputData : INetworkSerializable
{
    public int Tick;
    public Vector2 MoveInput;
    public Vector3 LookDirection;
    public bool JumpPressed;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref MoveInput);
        serializer.SerializeValue(ref LookDirection);
        serializer.SerializeValue(ref JumpPressed);
    }
}
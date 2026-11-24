using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Explicacion: 
///
/// Flujo:
/// El dueño simula su input al instante y se envia al servidor por RPC, 
/// el servidor recibe el imput, lo aplica u devuekve ek estado autoritativo (por el AnticipatedNetworkTransform, no hay RPC de estado)
/// 
/// Cuando llega al cliente, el sistema llama a OnReanticipate: volvemos al estado del servidor,
/// re-aplicamos los inputs que el servidor todavia no vio y decidimos si ignorar,
/// suavizar o saltar a la correccion.
///
/// Colisiones:
/// El Rigidbody es KINEMATIC: solo sirve para que los triggers detecten al jugador
/// Las paredes y el suelo se resuelven a mano en SimulateStep con CapsuleCast
///    (collide and slide). Como solo consulta la fisica estatica, el resultado es igual
///    en cliente y servidor, y se puede re-simular en OnReanticipate
///
/// La velocidad maxima sale de PlayerStats (StatType.MoveSpeed).
/// </summary>
[RequireComponent(typeof(Stats), typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMovement : NetworkBehaviour
{
    private const float STOPVALUE = 0.01f;
    private const int MAX_HISTORY = 256;
    private const int MAX_SLIDE_ITERATIONS = 3;

    [Header("Horizontal Movement")]
    [Tooltip("Segundos para llegar de 0 a la velocidad maxima")]
    [SerializeField, Min(0.01f)] private float _timeToMaxSpeed = 0.12f;
    [Tooltip("Segundos para frenar por completo desde la velocidad maxima")]
    [SerializeField, Min(0.01f)] private float _timeToStop = 0.08f;

    [Header("Vertical Movement")]
    [SerializeField, Min(0)] private float _jumpForce = 10f;
    [SerializeField, Min(0)] private float _gravityForce = 20f;

    [Header("Colisiones")]
    [Tooltip("Capas que bloquean el movimiento")]
    [SerializeField] private LayerMask _collisionMask;
    [Tooltip("Separacion minima que se deja con las superficies para no quedar pegado")]
    [SerializeField, Min(0.001f)] private float _skinWidth = 0.02f;
    [Tooltip("Pendiente maxima que cuenta como suelo caminable")]
    [SerializeField, Range(0f, 89f)] private float _maxSlopeAngle = 45f;
    [Tooltip("Distancia hacia abajo (desdel transform) para considerar que esta en el suelo")]
    [SerializeField, Min(0.01f)] private float _groundCheckDistance = 0.1f;

    [Header("Datos para la reconciliacion (cliente dueño)")]
    [Tooltip("Error menor a esto: se ignora la correccion y se conserva lo que el cliente ya mostraba")]
    [SerializeField, Min(0)] private float _ignoreDistance = 0.25f;

    [Tooltip("Error entre _ignoreDistance y este valor: correccion suavizada. Mayor: salto directo")]
    [SerializeField, Min(0)] private float _smoothDistance = 3f;

    [Tooltip("Duracion del suavizado en segundos. (0 = nunca suaviza)")]
    [SerializeField, Min(0)] private float _smoothTime = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs;

    private Rigidbody _rb;
    private CapsuleCollider _capsule;
    private AnticipatedNetworkTransform _ant;
    private LookToMause _lookToMause;
    private PlayerInput _playerInput;
    private Stats _stats;

    // Medidas de la capsula en espacio mundo, relativas a _pos (se calculan al spawnear)
    private float _capsuleRadius;
    private float _capsuleHalfSegment;  
    private float _capsuleCenterY;      
    private float _cosMaxSlope;

    // Input local (solo el dueño)
    private Vector2 _currentMoveInput;
    private bool _jumpRequested;
    private int _inputCounter;
    private int _simulationEpoch;
    private int _lastAckedTick;

    // Estado de la simulacion (fuente de verdad, un transform propio)
    private Vector3 _pos;
    private Vector3 _lookDir = Vector3.forward;

    // Parte del estado que el servidor no nos manda: la guardamos nosotros por cada input.
    private Vector3 _horizontalVelocity;
    private float _verticalVelocity;

    private struct HistoryImput
    {
        public InputData Input;
    }
    private readonly List<HistoryImput> _history = new List<HistoryImput>();

    private readonly Queue<InputData> _serverInputQueue = new Queue<InputData>();
    private int _lastReceivedTick;
    private int _lastProcessedTick;

    private struct MovementSnapshot : INetworkSerializable
    {
        public int Epoch;
        public int Tick;
        public Vector3 Position;
        public Vector3 LookDirection;
        public Vector3 HorizontalVelocity;
        public float VerticalVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Epoch);
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref LookDirection);
            serializer.SerializeValue(ref HorizontalVelocity);
            serializer.SerializeValue(ref VerticalVelocity);
        }
    }

    // ------------------------------------------------------------------
    // OnNetworkSpawn
    // Habilita input y mouse solo para el dueño, configura el Rigidbody kinematic,
    // mide la capsula, toma la posicion de spawn como estado inicial y, en el
    // cliente dueño (no host), activa la reanticipacion constante
    // ------------------------------------------------------------------
    public override void OnNetworkSpawn()
    {
        _playerInput = GetComponent<PlayerInput>();
        _lookToMause = GetComponent<LookToMause>();
        _ant = GetComponent<AnticipatedNetworkTransform>();
        _stats = GetComponent<Stats>();
        _rb = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.None;

        CacheCapsule();

        _playerInput.enabled = IsOwner;
        _lookToMause.enabled = IsOwner;

        _pos = transform.position;
        _lookDir = transform.forward;

        if (_ant == null)
        {
            Debug.LogError($"[{name}] Falta el AnticipatedNetworkTransform en el prefab del player.");
            return;
        }

        if (IsOwner) CameraManager.Instance.SetTarget(transform);

        if (IsOwner && !IsServer) _ant.StaleDataHandling = StaleDataHandling.Ignore;
    }
        
    private void CacheCapsule()
    {
        Vector3 scale = transform.lossyScale;
        _capsuleRadius = _capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(_capsule.height * Mathf.Abs(scale.y), _capsuleRadius * 2f);
        _capsuleHalfSegment = height * 0.5f - _capsuleRadius;
        _capsuleCenterY = _capsule.center.y * scale.y;
        _cosMaxSlope = Mathf.Cos(_maxSlopeAngle * Mathf.Deg2Rad);

        if (_collisionMask.value == 0)
            Debug.LogWarning($"[{name}] _collisionMask vacia: el jugador no va a colisionar con nada (ni con el suelo).", this);
    }

    // ------------------------------------------------------------------
    // FixedUpdate
    // Solo corre para el dueño (cliente o host). Los jugadores remotos los
    // mueve el servidor desde el RPC, no desde aca
    //  1. Arma el InputData de este tick
    //  2. Lo simula y lo muestra de inmediato (sin esperar al servidor)
    //  3. Si es el host, termina: ya es la autoridad
    //  4. Si es cliente: lo guarda en el historial y lo manda al servidor
    // -------------------------------------------------------------------
    private void FixedUpdate()
    {
        if (!CanMove)
        {
            _currentMoveInput = Vector2.zero;
            _jumpRequested = false;
            return;
        }
        if (IsServer && !IsOwner)
        {
            ProcessOneQueuedInput();
            return;
        }

        if (!IsOwner) return;
       
        if (!IsServer && _history.Count >= MAX_HISTORY) return;

        InputData input = new InputData
        {
            Tick = ++_inputCounter,
            Epoch = _simulationEpoch,
            MoveInput = _currentMoveInput,
            LookDirection = _lookToMause.GetOrientation(),
            JumpPressed = _jumpRequested
        };
        _jumpRequested = false;

        SimulateStep(input);
        ApplyToAnticipatedTransform();

        if (IsServer) return;

        _history.Add(new HistoryImput { Input = input });

        SubmitInputServerRpc(input);
    }

    // --------------------------------------------------------------------
    // SubmitInputServerRpc
    // Se ejecuta en el servidor cuando llega un input de un cliente
    // Valida que el emisor sea el dueño del cuerpo y aplica exactamente la
    // misma simulacion que el cliente. Como se llama con AnticipateMove en
    // el servidor, el componente actualiza solo el estado autoritativo y
    // lo replica a todos
    // ------------------------------------------------------------------
    [Rpc(SendTo.Server)]
    private void SubmitInputServerRpc(InputData input, RpcParams rpcParams = default)
    {
        if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId) return;
        PlayerSoul soul = PlayerSoul.GetFor(OwnerClientId);
        if (soul != null && !soul.IsAlive) return;
        if (input.Epoch != _simulationEpoch || input.Tick != _lastReceivedTick + 1) return;
        if (!IsFinite(input.MoveInput.x) || !IsFinite(input.MoveInput.y) ||
            !IsFinite(input.LookDirection.x) || !IsFinite(input.LookDirection.y) || !IsFinite(input.LookDirection.z)) return;

        if (_serverInputQueue.Count >= MAX_HISTORY)
        {
            _serverInputQueue.Clear();
            _lastReceivedTick = input.Tick;
            _lastProcessedTick = input.Tick;
            SendMovementSnapshot();
            return;
        }

        _lastReceivedTick = input.Tick;
        _serverInputQueue.Enqueue(input);
    }

    private void ProcessOneQueuedInput()
    {
        if (_serverInputQueue.Count == 0) return;

        InputData input = _serverInputQueue.Dequeue();
        if (input.Epoch != _simulationEpoch) return;

        SimulateStep(input);
        _lastProcessedTick = input.Tick;
        ApplyToAnticipatedTransform();
        SendMovementSnapshot();
    }

    [Rpc(SendTo.Owner)]
    private void MovementSnapshotRpc(MovementSnapshot snapshot)
    {
        if (IsServer || !IsOwner) return;
        Reconcile(snapshot);
    }

    private void SendMovementSnapshot()
    {
        if (!IsServer || IsOwner) return;

        MovementSnapshotRpc(new MovementSnapshot
        {
            Epoch = _simulationEpoch,
            Tick = _lastProcessedTick,
            Position = _pos,
            LookDirection = _lookDir,
            HorizontalVelocity = _horizontalVelocity,
            VerticalVelocity = _verticalVelocity
        });
    }

    private void Reconcile(MovementSnapshot snapshot)
    {
        if (snapshot.Epoch < _simulationEpoch) return;
        if (snapshot.Epoch > _simulationEpoch)
        {
            _simulationEpoch = snapshot.Epoch;
            _inputCounter = 0;
            _lastAckedTick = 0;
            _history.Clear();
        }
        if (snapshot.Tick < _lastAckedTick) return;

        var previousState = _ant.AnticipatedState;
        _lastAckedTick = snapshot.Tick;

        int confirmedCount = 0;
        while (confirmedCount < _history.Count && _history[confirmedCount].Input.Tick <= snapshot.Tick)
            confirmedCount++;
        if (confirmedCount > 0) _history.RemoveRange(0, confirmedCount);

        _pos = snapshot.Position;
        _lookDir = snapshot.LookDirection;
        _horizontalVelocity = snapshot.HorizontalVelocity;
        _verticalVelocity = snapshot.VerticalVelocity;

        for (int i = 0; i < _history.Count; i++)
            SimulateStep(_history[i].Input);

        ApplyToAnticipatedTransform();

        var correctedState = _ant.AnticipatedState;
        float sqDist = (previousState.Position - correctedState.Position).sqrMagnitude;
        if (_debugLogs)
            Debug.Log($"[Reconcile] tick={snapshot.Tick} error={Mathf.Sqrt(sqDist):F3} pendientes={_history.Count}");

        if (_smoothTime <= 0f) return;
        if (sqDist <= _ignoreDistance * _ignoreDistance)
        {
            _ant.AnticipateState(previousState);
            _pos = previousState.Position;
        }
        else if (sqDist < _smoothDistance * _smoothDistance)
        {
            _ant.Smooth(previousState, correctedState, _smoothTime);
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private bool CanMove
    {
        get
        {
            PlayerSoul soul = PlayerSoul.GetFor(OwnerClientId);
            return soul == null || soul.IsAlive;
        }
    }

    
    public override void OnReanticipate(double lastRoundTripTime)
    {
        if (!IsOwner || IsServer || _ant == null) return;

        var predictedState = _ant.PreviousAnticipatedState;
        _ant.AnticipateState(predictedState);
        _pos = predictedState.Position;
    }

    // ------------------------------------------------------------------
    // SimulateStep
    // Un paso de simulacion (un FixedUpdate). Identico en cliente y servidor
    // Solo toca _pos, _lookDir y las velocidades; nunca el transform
    //  - Rotacion: guarda la direccion del mouse
    //  - Suelo: CapsuleCast corto hacia abajo desde _pos
    //  - Salto y gravedad: modifican _verticalVelocity
    //  - Horizontal: la velocidad (un solo vector) se mueve hacia input * MoveSpeed
    //    Al invertir la direccion pasa por cero de forma natural
    //  - Desplazamiento: MoveAndCollide (frena y desliza contra paredes/suelo/techo)
    // ------------------------------------------------------------------
    private void SimulateStep(InputData input)
    {
        float dt = Time.fixedDeltaTime;

        if (input.LookDirection != Vector3.zero)
            _lookDir = input.LookDirection;

        bool isGrounded = CheckGrounded(_pos);

        if (input.JumpPressed && isGrounded)
            _verticalVelocity = _jumpForce;

        if (isGrounded && _verticalVelocity <= 0f)
            _verticalVelocity = 0f;
        else
            _verticalVelocity -= _gravityForce * dt;

        Vector2 clampedInput = Vector2.ClampMagnitude(input.MoveInput, 1f);
        bool isMoving = clampedInput.sqrMagnitude > STOPVALUE;

        float moveSpeed = _stats != null ? _stats.GetStat(StatType.MoveSpeed) : 0f;
        Vector3 targetVelocity = isMoving
            ? new Vector3(clampedInput.x, 0f, clampedInput.y) * moveSpeed
            : Vector3.zero;

        // seteando la acceleracion segun el tiempo
        float reference = Mathf.Max(moveSpeed, _horizontalVelocity.magnitude);
        float time = isMoving ? _timeToMaxSpeed : _timeToStop;
        float rate = reference / Mathf.Max(time, 0.0001f);

        _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, rate * dt);

        Vector3 velocity = _horizontalVelocity;
        velocity.y = _verticalVelocity;
                
        float yBefore = _pos.y;
        _pos = MoveAndCollide(_pos, velocity * dt);
               
        if (_verticalVelocity > 0f && (_pos.y - yBefore) < _verticalVelocity * dt * 0.5f) _verticalVelocity = 0f;
    }

   
    // Mueve la capsula 'delta' desde 'start'. Si choca, frena dejando _skinWidth de margen.
    // Desliza lo que sobra por la superficie (hasta MAX_SLIDE_ITERATIONS veces).
    private Vector3 MoveAndCollide(Vector3 start, Vector3 delta)
    {
        Vector3 pos = start;
        Vector3 remaining = delta;

        for (int i = 0; i < MAX_SLIDE_ITERATIONS; i++)
        {
            float distance = remaining.magnitude;
            if (distance < 0.0001f) break;

            Vector3 dir = remaining / distance;

            if (!CastCapsule(pos, dir, distance + _skinWidth, out RaycastHit hit))
            {
                pos += remaining;
                break;
            }
                       
            float moveDistance = Mathf.Max(0f, hit.distance - _skinWidth);
            pos += dir * moveDistance;

            Vector3 leftover = dir * (distance - moveDistance);
            remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
        }

        return pos;
    }
        
    private bool CheckGrounded(Vector3 position)
    {
        if (!CastCapsule(position, Vector3.down, _groundCheckDistance + _skinWidth, out RaycastHit hit))
            return false;

        return hit.normal.y >= _cosMaxSlope;
    }

    private bool CastCapsule(Vector3 position, Vector3 direction, float distance, out RaycastHit hit)
    {
        Vector3 center = position + Vector3.up * _capsuleCenterY;
        Vector3 top = center + Vector3.up * _capsuleHalfSegment;
        Vector3 bottom = center - Vector3.up * _capsuleHalfSegment;

        return Physics.CapsuleCast(top, bottom, _capsuleRadius, direction, out hit, distance,
                                   _collisionMask, QueryTriggerInteraction.Ignore);
    }

    
    private void ApplyToAnticipatedTransform()
    {
        if (_ant == null) return;

        _ant.AnticipateMove(_pos);
        if (_lookDir.sqrMagnitude > 0.0001f)
            _ant.AnticipateRotate(Quaternion.LookRotation(_lookDir, Vector3.up));
    }

    public void OnMove(InputValue action)
    {
        if (!IsOwner || !CanMove) return;
        _currentMoveInput = action.Get<Vector2>();
    }

    public void OnJump()
    {
        if (!IsOwner || !CanMove) return;
        _jumpRequested = true;
    }
    
    public void ServerTeleport(Vector3 position)
    {
        if (!IsServer) return;
        _simulationEpoch++;
        _lastReceivedTick = 0;
        _lastProcessedTick = 0;
        _serverInputQueue.Clear();
        ResetSimulation(position, _simulationEpoch);
        TeleportOwnerRpc(position, _simulationEpoch);
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 position, int epoch)
    {
        if (IsServer || !IsOwner || epoch < _simulationEpoch) return;
        _inputCounter = 0;
        _lastAckedTick = 0;
        ResetSimulation(position, epoch);
    }

    private void ResetSimulation(Vector3 position, int epoch)
    {
        _simulationEpoch = epoch;
        _pos = position;
        _lookDir = transform.forward;
        _horizontalVelocity = Vector3.zero;
        _verticalVelocity = 0f;
        _history.Clear();
        if (!IsServer) _lastAckedTick = 0;
        ApplyToAnticipatedTransform();
    }
}

/* Reporte que envia el cliente al servidor:
 * Almacena los imputs del cliente con el tick exacto en el que ocurrieron
 * El cliente ejecuta instantaneamente sus imputs de manera local para no sentir lag
 * A la par, crea un reporte con los datos de la accion en ese momento
 */
public struct InputData : INetworkSerializable
{
    public int Tick;
    public int Epoch;
    public Vector2 MoveInput;
    public Vector3 LookDirection;
    public bool JumpPressed;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Epoch);
        serializer.SerializeValue(ref MoveInput);
        serializer.SerializeValue(ref LookDirection);
        serializer.SerializeValue(ref JumpPressed);
    }
}

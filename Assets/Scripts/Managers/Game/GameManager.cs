using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static Unity.Cinemachine.CinemachineSplineRoll;

public enum GamePhase
{
    WaitingForBodies,
    Preparation,
    ChoosingDoor,
    CombatPlanning,
    CombatCountdown,
    Combat,
    Resolution,       // [TEMP] hoy solo muestra el resultado y vuelve al hub. TODO 2B: tesoros, niveles, d20
    SavingThrow,      // TODO 2B
    ChestDecision,    // TODO etapa 3
    Charity,          // TODO etapa 4
    RestRoom,         // TODO etapa 4 (hoy solo espera y pasa al piso siguiente)
    GameOver          // TODO victoria
}

/// <summary>
/// Autoridad del flujo de la partida. Objeto de la escena Game (con NetworkObject).
/// Solo el servidor escribe; todos leen fase, turno, piso, participantes y tiempo por variables de red.
/// Los pisos duran tantos turnos como jugadores conectados: el piso termina cuando todos los conectados jugaron.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("[TEMP: inspector] Ajustes (luego los define el host en el lobby)")]
    [SerializeField] private GameSettings _settings = new GameSettings();
    [SerializeField] private ItemCatalog _lootCatalog;

    [Header("Salas")]
    [SerializeField] private RoomCatalog _roomCatalog;
    [Tooltip("Punto de esta escena donde se instancian las salas (lejos del hub para que no se solapen)")]
    [SerializeField] private Transform _roomAnchor;

    [Header("[TEMP: inspector] Tiempos en segundos")]
    [Tooltip("0 = se saltea la preparacion grupal")]
    [SerializeField, Min(0)] private float _preparationSeconds = 20f;

    [Tooltip("0 = sin limite para patear la puerta")]
    [SerializeField, Min(0)] private float _doorSeconds = 0f;

    [Tooltip("0 = se saltea la sala de descanso")]
    [SerializeField, Min(0)] private float _restSeconds = 5f;

    [Tooltip("Tiempo base de planificacion (sabotaje / negociacion)")]
    [SerializeField, Min(1)] private float _planningSeconds = 15f;
    [Tooltip("Tiempo que suma cada modificacion del combate. Sin tope de extensiones")]
    [SerializeField, Min(0)] private float _planningExtensionSeconds = 5f;
    [SerializeField, Min(0)] private float _countdownSeconds = 3f;
    [SerializeField, Min(0.5f)] private float _resolutionSeconds = 3f;

    [Header("[TEMP: inspector]")]
    [Tooltip("Permite atacar en cualquier fase (para probar). Apagado: solo los participantes en la fase Combat")]
    [SerializeField] private bool _allowAttackOutsideCombat = false;

    private readonly NetworkVariable<GamePhase> _phase = new NetworkVariable<GamePhase>(GamePhase.WaitingForBodies);
    private readonly NetworkVariable<ulong> _currentTurnPlayer = new NetworkVariable<ulong>();
    private readonly NetworkVariable<int> _floor = new NetworkVariable<int>(1);
    private readonly NetworkVariable<int> _turnIndex = new NetworkVariable<int>(0);
    private readonly NetworkVariable<double> _phaseEndTime = new NetworkVariable<double>();   // tiempo de red; 0 = sin limite
    private readonly NetworkVariable<int> _currentCombatId = new NetworkVariable<int>(RoomCatalog.EmptyID);

    private NetworkList<ulong> _participants;
    private NetworkList<ulong> _combatReady;

    // Solo servidor
    private readonly HashSet<ulong> _playedThisFloor = new HashSet<ulong>();
    private CombatEncounter _encounter;

    public GamePhase Phase => _phase.Value;
    public ulong CurrentTurnPlayer => _currentTurnPlayer.Value;
    public int Floor => _floor.Value;
    public int TurnIndex => _turnIndex.Value;
    public GameSettings Settings => _settings;
    public bool IsMyTurn => IsSpawned && _currentTurnPlayer.Value == NetworkManager.LocalClientId;

    public float RemainingTime => _phaseEndTime.Value > 0
        ? Mathf.Max(0f, (float)(_phaseEndTime.Value - NetworkManager.ServerTime.Time))
        : 0f;

    public bool IsParticipant(ulong clientId) => _participants.Contains(clientId);
    public bool IsReadyForCombat(ulong clientId) => _combatReady.Contains(clientId);

    /// <summary>Datos del combate en curso (para la futura pantalla de presentacion del monstruo).</summary>
    public bool TryGetCurrentCombat(out BaseCombatData combat)
    {
        combat = null;
        return _roomCatalog != null && _currentCombatId.Value >= 0 && _roomCatalog.TryGetCombat(_currentCombatId.Value, out combat);
    }

    public event Action<GamePhase> OnPhaseChanged;
    public event Action<ulong> OnTurnPlayerChanged;

    // ---------------------------------------------------------------- Hooks (los consultan PlayerInventory y PlayerCombat)

    /// <summary>No se puede equipar desde la planificacion del combate hasta su resolucion.</summary>
    public bool CanModifyEquipment
    {
        get
        {
            switch (_phase.Value)
            {
                case GamePhase.CombatPlanning:
                case GamePhase.CombatCountdown:
                case GamePhase.Combat:
                case GamePhase.Resolution:
                case GamePhase.SavingThrow:
                    return false;
                default:
                    return true;
            }
        }
    }

    public bool CanAttack(ulong clientId) =>
        _allowAttackOutsideCombat || (_phase.Value == GamePhase.Combat && IsParticipant(clientId));

    // ---------------------------------------------------------------- Ciclo de vida

    private void Awake()
    {
        _participants = new NetworkList<ulong>(null, NetworkVariableReadPermission.Everyone);
        _combatReady = new NetworkList<ulong>(null, NetworkVariableReadPermission.Everyone);
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;

        _phase.OnValueChanged += HandlePhaseChanged;
        _currentTurnPlayer.OnValueChanged += HandleTurnPlayerChanged;

        if (!IsServer) return;

        NetworkManager.OnClientDisconnectCallback += ServerOnClientDisconnected;
        ServerSetPhase(GamePhase.WaitingForBodies);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;

        _phase.OnValueChanged -= HandlePhaseChanged;
        _currentTurnPlayer.OnValueChanged -= HandleTurnPlayerChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= ServerOnClientDisconnected;
    }

    private void HandlePhaseChanged(GamePhase previous, GamePhase current)
    {
        Debug.Log($"[Game] {(IsServer ? "SERVIDOR" : "CLIENTE")} fase {previous} -> {current} | turno={_currentTurnPlayer.Value} piso={_floor.Value} combate={_currentCombatId.Value}");
        OnPhaseChanged?.Invoke(current);
    }

    private void HandleTurnPlayerChanged(ulong previous, ulong current) => OnTurnPlayerChanged?.Invoke(current);

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        if (_phase.Value == GamePhase.WaitingForBodies && AllBodiesReady()) ServerStartGame();
        if (_phase.Value == GamePhase.Combat) ServerTickCombat();

        if (_phaseEndTime.Value > 0 && NetworkManager.ServerTime.Time >= _phaseEndTime.Value)
        {
            _phaseEndTime.Value = 0;
            ServerOnPhaseTimeout();
        }
    }

    // ---------------------------------------------------------------- Servidor: flujo general

    private void ServerSetPhase(GamePhase phase, float seconds = 0f)
    {
        _phaseEndTime.Value = seconds > 0f ? NetworkManager.ServerTime.Time + seconds : 0;
        _phase.Value = phase;
    }

    private bool AllBodiesReady()
    {
        int souls = PlayerSoul.All.Count;
        if (souls == 0 || souls != NetworkManager.ConnectedClientsIds.Count) return false;

        foreach (PlayerSoul soul in PlayerSoul.All)
            if (!soul.TryGetBodyComponent(out PlayerInventory inventory) || !inventory.IsSpawned) return false;

        return true;
    }

    private void ServerStartGame()
    {
        _floor.Value = 1;
        _playedThisFloor.Clear();

        LootService.DealToAll(CollectInventories(), _settings.initialLootCount, _lootCatalog);

        if (_preparationSeconds > 0f) ServerSetPhase(GamePhase.Preparation, _preparationSeconds);
        else ServerBeginNextTurn();
    }

    private void ServerOnPhaseTimeout()
    {
        switch (_phase.Value)
        {
            case GamePhase.Preparation: ServerBeginNextTurn(); break;
            case GamePhase.ChoosingDoor: ServerKickDoor(); break;   // [TEMP] luego: puerta al azar
            case GamePhase.CombatPlanning: ServerStartCountdown(); break;
            case GamePhase.CombatCountdown: ServerStartCombat(); break;
            case GamePhase.Resolution: ServerEndEncounter(); break;
            case GamePhase.RestRoom: ServerStartNextFloor(); break;
        }
    }

    private void ServerBeginNextTurn()
    {
        var candidates = new List<PlayerSoul>();
        foreach (PlayerSoul soul in PlayerSoul.All)
        {
            bool connected = NetworkManager.ConnectedClients.ContainsKey(soul.OwnerClientId);
            if (connected && !_playedThisFloor.Contains(soul.OwnerClientId)) candidates.Add(soul);
        }

        if (candidates.Count == 0)
        {
            ServerEndFloor();
            return;
        }

        PlayerSoul chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        _playedThisFloor.Add(chosen.OwnerClientId);
        _turnIndex.Value = _playedThisFloor.Count;
        _currentTurnPlayer.Value = chosen.OwnerClientId;

        // TODO 2B: si el jugador esta muerto, resucitar (ServerSetAlive, RestoreFullHealth)
        // y darle la misma cantidad de items que el loot inicial (LootService.GiveRandom).

        ServerSetPhase(GamePhase.ChoosingDoor, _doorSeconds);
    }

    private void ServerEndTurn()
    {
        // TODO etapa 4: pasar por la fase Charity (excedente de mochila al jugador de menor nivel).
        ServerBeginNextTurn();
    }

    private void ServerEndFloor()
    {
        // TODO: chequeo de victoria (nivel objetivo) y combate de desempate entre jugadores.
        // TODO etapa 4: sala de descanso real (curar vivos, habilitar trade).
        if (_restSeconds > 0f) ServerSetPhase(GamePhase.RestRoom, _restSeconds);
        else ServerStartNextFloor();
    }

    private void ServerStartNextFloor()
    {
        _floor.Value++;
        _turnIndex.Value = 0;
        _playedThisFloor.Clear();
        ServerBeginNextTurn();
    }

    private List<PlayerInventory> CollectInventories()
    {
        var list = new List<PlayerInventory>();
        foreach (PlayerSoul soul in PlayerSoul.All)
            if (soul.TryGetBodyComponent(out PlayerInventory inventory)) list.Add(inventory);
        return list;
    }

    // ---------------------------------------------------------------- Servidor: encuentro de combate

    private void ServerKickDoor()
    {
        Debug.Log($"[Game] El jugador {_currentTurnPlayer.Value} pateo la puerta.");

        // TODO etapa 3: sortear tambien salas de tesoro y maldicion
        if (!ServerTryStartCombatRoom()) ServerEndTurn();   // sin salas configuradas se salta el turno
    }

    private bool ServerTryStartCombatRoom()
    {
        if (_roomCatalog == null || _roomAnchor == null)
        {
            Debug.LogError("[Game] Falta asignar RoomCatalog o RoomAnchor en el GameManager.", this);
            return false;
        }

        RoomData room = _roomCatalog.GetRandomRoom(RoomType.Combat);
        BaseCombatData combat = room != null ? room.GetRandomCombat() : null;
        if (room == null || combat == null || room.roomPrefab == null)
        {
            Debug.LogError("[Game] No hay sala de combate valida (revisar RoomCatalog, prefab y combates posibles).", this);
            return false;
        }

        GameObject roomObject = Instantiate(room.roomPrefab, _roomAnchor.position, _roomAnchor.rotation);
        if (!roomObject.TryGetComponent(out NetworkObject roomNetworkObject) || !roomObject.TryGetComponent(out RoomLayout layout))
        {
            Debug.LogError($"[Game] El prefab de sala {room.roomPrefab.name} necesita NetworkObject y RoomLayout.", room.roomPrefab);
            Destroy(roomObject);
            return false;
        }
        roomNetworkObject.Spawn(true);

        _encounter = new CombatEncounter(combat, layout, roomNetworkObject);
        _encounter.Participants.Add(_currentTurnPlayer.Value);   // TODO: ayudantes

        _participants.Clear();
        _combatReady.Clear();
        foreach (ulong id in _encounter.Participants) _participants.Add(id);
        _currentCombatId.Value = combat.combatID;

        combat.ServerSpawn(_encounter);
        ServerTeleportToRoom();
        ServerSetPhase(GamePhase.CombatPlanning, _planningSeconds);
        return true;
    }

    /// <summary>Solo servidor. Lo llamaran sabotajes, consumibles y ayudas (sin tope de extensiones).</summary>
    public void ServerExtendPlanning(float seconds)
    {
        if (!IsServer || _phase.Value != GamePhase.CombatPlanning || _phaseEndTime.Value <= 0) return;
        _phaseEndTime.Value += seconds;
    }

    private void ServerCheckAllReady()
    {
        if (_phase.Value != GamePhase.CombatPlanning || _participants.Count == 0) return;

        for (int i = 0; i < _participants.Count; i++)
            if (!_combatReady.Contains(_participants[i])) return;

        ServerStartCountdown();
    }

    private void ServerStartCountdown()
    {
        if (_countdownSeconds > 0f) ServerSetPhase(GamePhase.CombatCountdown, _countdownSeconds);
        else ServerStartCombat();
    }

    private void ServerStartCombat() => ServerSetPhase(GamePhase.Combat);

    private void ServerTickCombat()
    {
        if (_encounter == null) return;

        if (_encounter.Data.IsVictory(_encounter)) ServerResolveCombat(true);
        else if (_encounter.Data.IsDefeat(_encounter)) ServerResolveCombat(false);
    }

    private void ServerResolveCombat(bool victory)
    {
        Debug.Log($"[Game] Combate {(victory ? "GANADO" : "PERDIDO")}.");

        // TODO 2B: victoria -> tesoros y niveles al dueño del turno.
        // TODO 2B: derrota -> d20 por participante, EscapeCondition y BadThing.
        // TODO 2B: curar a los participantes vivos hasta el minimo de vida configurable.
        ServerSetPhase(GamePhase.Resolution, _resolutionSeconds);
    }

    private void ServerEndEncounter()
    {
        if (_encounter != null)
        {
            ServerTeleportToHub();
            _encounter.Cleanup();
            _encounter = null;
        }

        _participants.Clear();
        _combatReady.Clear();
        _currentCombatId.Value = RoomCatalog.EmptyID;

        ServerEndTurn();
    }

    private void ServerTeleportToRoom()
    {
        int index = 0;
        foreach (PlayerSoul soul in PlayerSoul.All)
        {
            ulong id = soul.OwnerClientId;
            bool travels = soul.IsAlive || _encounter.Participants.Contains(id);   // los muertos se quedan en el hub
            if (!travels || !NetworkManager.ConnectedClients.ContainsKey(id)) continue;
            if (!soul.TryGetBodyComponent(out PlayerMovement movement)) continue;

            movement.ServerTeleport(_encounter.Layout.GetPlayerSpawn(index++));
            _encounter.Travelers.Add(id);
        }
    }

    private void ServerTeleportToHub()
    {
        int index = 0;
        foreach (ulong id in _encounter.Travelers)
        {
            PlayerSoul soul = PlayerSoul.GetFor(id);
            if (soul == null || !soul.TryGetBodyComponent(out PlayerMovement movement)) continue;

            movement.ServerTeleport(GetHubPosition(index++));
        }
    }

    private Vector3 GetHubPosition(int index)
    {
        SpawnPoint[] points = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.InstanceID);
        if (points.Length == 0)
        {
            Debug.LogWarning("[Game] No hay SpawnPoint en la escena Game: el hub vuelve a (0,0,0).");
            return Vector3.zero;
        }
        return points[index % points.Length].transform.position;
    }

    private void ServerOnClientDisconnected(ulong clientId)
    {
        switch (_phase.Value)
        {
            case GamePhase.ChoosingDoor:
                if (clientId == _currentTurnPlayer.Value) ServerBeginNextTurn();
                break;

            case GamePhase.CombatPlanning:
            case GamePhase.CombatCountdown:
            case GamePhase.Combat:
            case GamePhase.Resolution:
                if (_encounter == null) break;

                if (clientId == _currentTurnPlayer.Value)
                {
                    Debug.Log("[Game] Se desconecto el dueño del turno: se aborta el combate.");
                    ServerEndEncounter();
                }
                else
                {
                    _encounter.Participants.Remove(clientId);
                    _participants.Remove(clientId);
                    _combatReady.Remove(clientId);
                    ServerCheckAllReady();
                }
                break;
        }
        // TODO: contemplar el resto de las fases del turno cuando existan.
    }

    // ---------------------------------------------------------------- Pedidos del cliente

    /// <summary>Lo llama el jugador de turno. Se valida contra el jugador de turno, no contra OwnerClientId (el GameManager no tiene dueño).</summary>
    [Rpc(SendTo.Server)]
    public void KickDoorRpc(RpcParams rpcParams = default)
    {
        if (_phase.Value != GamePhase.ChoosingDoor) return;
        if (rpcParams.Receive.SenderClientId != _currentTurnPlayer.Value) return;

        ServerKickDoor();
    }

    /// <summary>Un participante avisa que termino de planificar. Cuando todos estan listos arranca la cuenta regresiva.</summary>
    [Rpc(SendTo.Server)]
    public void CombatReadyRpc(RpcParams rpcParams = default)
    {
        if (_phase.Value != GamePhase.CombatPlanning) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (!_participants.Contains(sender) || _combatReady.Contains(sender)) return;

        _combatReady.Add(sender);
        ServerCheckAllReady();
    }

    /// <summary>
    /// [TEMP] Simula una modificacion del combate (sabotaje, consumible, ayuda) para probar la extension del tiempo.
    /// TODO: lo reemplazan los sistemas reales, que llaman a ServerExtendPlanning.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void DebugModifyCombatRpc(RpcParams rpcParams = default)
    {
        if (_phase.Value != GamePhase.CombatPlanning) return;
        if (!NetworkManager.ConnectedClients.ContainsKey(rpcParams.Receive.SenderClientId)) return;

        ServerExtendPlanning(_planningExtensionSeconds);
    }
}
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum GamePhase
{
    WaitingForBodies,
    Preparation,
    ChoosingDoor,
    EnteringRoom,
    CombatPlanning,
    CombatCountdown,
    Combat,
    Resolution,       // [TEMP] hoy solo muestra el resultado. TODO 2B: tesoros, niveles, d20
    ChestDecision,
    RestRoom,         // [TEMP] espera en la sala actual. TODO etapa 4
    SavingThrow,      // TODO 2B (sin estado todavia)
    Charity,          // TODO etapa 4 (sin estado todavia)
    GameOver,         // Fin de la partida
    FinalDuel
}

/// <summary>
/// Autoridad del flujo de la partida. Objeto de la escena Game (con NetworkObject).
/// Guarda los datos replicados, los hooks y los RPC. La logica de cada fase vive en un estado (carpeta GameStates),
/// y la maquina de estados corre solo en el servidor. Los clientes leen la fase replicada.
/// Las salas persisten: el siguiente jugador patea la puerta desde la sala donde esta el grupo.
/// </summary>
public partial class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("[TEMP: inspector] Ajustes (luego los define el host en el lobby)")]
    [SerializeField] private GameSettings _settings = new GameSettings();
    [SerializeField] private ItemCatalog _lootCatalog;

    [Header("Salas")]
    [SerializeField] private RoomCatalog _roomCatalog;
    [Tooltip("Dos anclas lejos una de otra: las salas se alternan entre ellas para no superponerse")]
    [SerializeField] private Transform _roomAnchorA;
    [SerializeField] private Transform _roomAnchorB;
    [Tooltip("Prefab de sala con NetworkObject y RoomLayout para el desempate final. Si queda vacío se reutiliza la sala actual.")]
    [SerializeField] private GameObject _finalDuelRoomPrefab;
    [Tooltip("Ancla opcional para el desempate. Si queda vacía se usa el ancla libre de la rotación de salas.")]
    [SerializeField] private Transform _finalDuelRoomAnchor;
    [Tooltip("Transform del Hub usado como sala de descanso. Si queda vacío se busca un objeto llamado Hub.")]
    [SerializeField] private Transform _hubAnchor;
    [SerializeField, Min(0.5f)] private float _hubReturnRadius = 2f;

    [Header("[TEMP: inspector] Tiempos en segundos")]
    [Tooltip("0 = se saltea la preparacion grupal")]
    [SerializeField, Min(0)] private float _preparationSeconds = 20f;
    [Tooltip("0 = sin limite para patear la puerta")]
    [SerializeField, Min(0)] private float _doorSeconds = 0f;
    [Tooltip("Espera tras entrar a la sala, antes de destruir la anterior")]
    [SerializeField, Min(0.5f)] private float _enterRoomSeconds = 1f;
    [Tooltip("Tiempo base de planificacion (sabotaje / negociacion)")]
    [SerializeField, Min(1)] private float _planningSeconds = 15f;
    [Tooltip("Tiempo que suma cada modificacion del combate. Sin tope de extensiones")]
    [SerializeField, Min(0)] private float _planningExtensionSeconds = 5f;
    [SerializeField, Min(0)] private float _countdownSeconds = 3f;
    [SerializeField, Min(0.5f)] private float _resolutionSeconds = 3f;
    [Tooltip("Tiempo para decidir que hacer con el cofre. Si se acaba, se ignora y pasa el turno")]
    [SerializeField, Min(1)] private float _chestDecisionSeconds = 30f;
    [Tooltip("0 = se saltea la sala de descanso")]
    [SerializeField, Min(0)] private float _restSeconds = 5f;

    [Header("Recuperacion al terminar combate")]
    [Tooltip("Si la vida queda por debajo de este porcentaje de la vida maxima, se aplica la recuperacion configurada.")]
    [SerializeField, Range(0f, 1f)] private float _postCombatRecoveryThreshold = 0.15f;
    [Tooltip("Porcentaje de la vida maxima que se recupera al terminar el combate.")]
    [SerializeField, Range(0f, 1f)] private float _postCombatRecoveryPercent = 0.15f;

    [Header("[TEMP: inspector]")]
    [Tooltip("Permite atacar en cualquier fase (para probar). Apagado: solo los participantes en la fase Combat")]
    [SerializeField] private bool _allowAttackOutsideCombat = false;

    private readonly NetworkVariable<GamePhase> _phase = new NetworkVariable<GamePhase>(GamePhase.WaitingForBodies);
    private readonly NetworkVariable<ulong> _currentTurnPlayer = new NetworkVariable<ulong>();
    private readonly NetworkVariable<int> _floor = new NetworkVariable<int>(1);
    private readonly NetworkVariable<int> _turnIndex = new NetworkVariable<int>(0);
    private readonly NetworkVariable<double> _phaseEndTime = new NetworkVariable<double>();   // tiempo de red; 0 = sin limite
    private readonly NetworkVariable<int> _currentCombatId = new NetworkVariable<int>(RoomCatalog.EmptyID);
    private readonly NetworkVariable<int> _currentCombatTreasureAmount = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<Vector3> _combatCameraTargetPosition = new NetworkVariable<Vector3>();
    private readonly NetworkVariable<ulong> _winnerClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    private readonly NetworkVariable<ulong> _pendingHelpTarget = new NetworkVariable<ulong>(ulong.MaxValue);
    private readonly NetworkVariable<int> _pendingHelpReward = new NetworkVariable<int>();

    private NetworkList<ulong> _participants;
    private NetworkList<ulong> _combatReady;

    // Solo servidor
    private StateMachine _machine;
    private readonly HashSet<ulong> _playedThisFloor = new HashSet<ulong>();
    private ActiveRoom _currentRoom;
    private ActiveRoom _previousRoom;     // se destruye al terminar EnteringRoom
    private CombatEncounter _encounter;
    private NetworkObject _finalDuelRoomObject;
    private RoomLayout _finalDuelRoomLayout;
    private readonly List<ulong> _finalDuelists = new List<ulong>();
    private bool _combatVictory;
    private bool _combatResolutionApplied;
    private ulong _acceptedHelperClientId = ulong.MaxValue;
    private int _acceptedHelperReward;
    private bool _nextRoomUsesAnchorA = true;

    // Lectura (todos)
    public GamePhase Phase => _phase.Value;
    public ulong CurrentTurnPlayer => _currentTurnPlayer.Value;
    public int Floor => _floor.Value;
    public int TurnIndex => _turnIndex.Value;
    public ulong WinnerClientId => _winnerClientId.Value;
    public bool HasPendingHelpOffer => _pendingHelpTarget.Value != ulong.MaxValue;
    public ulong PendingHelpTargetClientId => _pendingHelpTarget.Value;
    public int PendingHelpReward => _pendingHelpReward.Value;
    public bool HasAcceptedCombatHelper => _participants.Count > 1;
    public bool IsFinalDuel => _phase.Value == GamePhase.FinalDuel;
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

    public bool HasCurrentCombat => _currentCombatId.Value != RoomCatalog.EmptyID && _currentCombatTreasureAmount.Value >= 0;
    public int CurrentCombatTreasureAmount => Mathf.Max(0, _currentCombatTreasureAmount.Value);

    // Lectura para los estados (solo servidor)
    public CombatEncounter Encounter => _encounter;
    public RoomType CurrentRoomType => _currentRoom != null ? _currentRoom.Data.type : RoomType.Combat;
    public float PreparationSeconds => _preparationSeconds;
    public float PlanningSeconds => _planningSeconds;
    public float ChestDecisionSeconds => _chestDecisionSeconds;
    public float RestSeconds => _restSeconds;
    public bool PhaseTimeUp => IsServer && _phaseEndTime.Value > 0 && NetworkManager.ServerTime.Time >= _phaseEndTime.Value;

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
                case GamePhase.FinalDuel:
                case GamePhase.SavingThrow:
                    return false;
                default:
                    return true;
            }
        }
    }

    public bool CanAttack(ulong clientId) =>
        _allowAttackOutsideCombat ||
        ((_phase.Value == GamePhase.Combat || _phase.Value == GamePhase.FinalDuel) && IsParticipant(clientId));

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
        _currentCombatId.OnValueChanged += HandleCombatIdChanged;
        _combatCameraTargetPosition.OnValueChanged += HandleCombatCameraTargetChanged;
        _participants.OnListChanged += HandleParticipantsChanged;
        UpdateLocalCamera();

        if (!IsServer) return;

        NetworkManager.OnClientDisconnectCallback += ServerOnClientDisconnected;

        _machine = new StateMachine();
        _machine.AddState(new WaitingForBodiesState(this), GamePhase.WaitingForBodies);
        _machine.AddState(new PreparationState(this), GamePhase.Preparation);
        _machine.AddState(new ChoosingDoorState(this), GamePhase.ChoosingDoor);
        _machine.AddState(new EnteringRoomState(this), GamePhase.EnteringRoom);
        _machine.AddState(new CombatPlanningState(this), GamePhase.CombatPlanning);
        _machine.AddState(new CombatCountdownState(this), GamePhase.CombatCountdown);
        _machine.AddState(new CombatState(this), GamePhase.Combat);
        _machine.AddState(new ResolutionState(this), GamePhase.Resolution);
        _machine.AddState(new ChestDecisionState(this), GamePhase.ChestDecision);
        _machine.AddState(new RestRoomState(this), GamePhase.RestRoom);
        _machine.AddState(new FinalDuelState(this), GamePhase.FinalDuel);
        _machine.AddState(new GameOverState(this), GamePhase.GameOver);

        ChangePhase(GamePhase.WaitingForBodies);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;

        _phase.OnValueChanged -= HandlePhaseChanged;
        _currentTurnPlayer.OnValueChanged -= HandleTurnPlayerChanged;
        _currentCombatId.OnValueChanged -= HandleCombatIdChanged;
        _combatCameraTargetPosition.OnValueChanged -= HandleCombatCameraTargetChanged;
        _participants.OnListChanged -= HandleParticipantsChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= ServerOnClientDisconnected;
    }

    private void HandlePhaseChanged(GamePhase previous, GamePhase current)
    {
        Debug.Log($"[Game] {(IsServer ? "SERVIDOR" : "CLIENTE")} fase {previous} -> {current} | turno={_currentTurnPlayer.Value} piso={_floor.Value} combate={_currentCombatId.Value}");
        UpdateLocalCamera();
        OnPhaseChanged?.Invoke(current);
    }

    private void UpdateLocalCamera()
    {
        CameraManager cameraManager = CameraManager.Instance;
        if (cameraManager == null || NetworkManager == null) return;

        bool isCombatRoom = _currentCombatId.Value != RoomCatalog.EmptyID;
        bool isSpectatorDuringCombat = Phase == GamePhase.Combat &&
            !_participants.Contains(NetworkManager.LocalClientId);
        if (isCombatRoom && isSpectatorDuringCombat)
        {
            PlayerSoul turnOwner = PlayerSoul.GetFor(_currentTurnPlayer.Value);
            if (turnOwner != null && turnOwner.TryGetBody(out NetworkObject body) && body != null)
                cameraManager.FocusBetween(_combatCameraTargetPosition.Value, body.transform);
            else
                cameraManager.FocusAt(_combatCameraTargetPosition.Value);
            return;
        }

        if (isCombatRoom &&
            (Phase == GamePhase.EnteringRoom || Phase == GamePhase.CombatPlanning || Phase == GamePhase.CombatCountdown))
        {
            cameraManager.FocusAt(_combatCameraTargetPosition.Value);
            return;
        }

        cameraManager.RestoreDefaultTarget();
    }

    private void HandleTurnPlayerChanged(ulong previous, ulong current)
    {
        UpdateLocalCamera();
        OnTurnPlayerChanged?.Invoke(current);
    }

    private void HandleCombatIdChanged(int previous, int current) => UpdateLocalCamera();
    private void HandleCombatCameraTargetChanged(Vector3 previous, Vector3 current) => UpdateLocalCamera();
    private void HandleParticipantsChanged(NetworkListEvent<ulong> changeEvent) => UpdateLocalCamera();

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        _machine?.MachineUpdate();
    }

    // ---------------------------------------------------------------- Servidor: fases

    /// <summary>Cambia la fase (replicada) y el estado de la maquina. Con seconds &gt; 0 la fase tiene tiempo limite.</summary>
    public bool ChangePhase(GamePhase phase, float seconds = 0f)
    {
        if (!IsServer) return false;
        if (_machine == null || !_machine.CanChangeState(phase))
        {
            Debug.LogWarning($"[Game] Transición de fase rechazada: {Phase} -> {phase}.", this);
            return false;
        }

        _phaseEndTime.Value = seconds > 0f ? NetworkManager.ServerTime.Time + seconds : 0;
        _phase.Value = phase;
        return _machine.ChangeState(phase);
    }

    /// <summary>Solo servidor. Lo llamaran sabotajes, consumibles y ayudas (sin tope de extensiones).</summary>
    public void ServerExtendPlanning(float seconds)
    {
        if (!IsServer || _phase.Value != GamePhase.CombatPlanning || _phaseEndTime.Value <= 0) return;
        _phaseEndTime.Value += seconds;
    }

    // ---------------------------------------------------------------- Servidor: inicio, turnos y pisos

    public bool ServerAllBodiesReady()
    {
        int souls = PlayerSoul.All.Count;
        if (souls == 0 || souls != NetworkManager.ConnectedClientsIds.Count) return false;

        foreach (PlayerSoul soul in PlayerSoul.All)
            if (!soul.TryGetBodyComponent(out PlayerInventory inventory) || !inventory.IsSpawned) return false;

        return true;
    }

    /// <summary>Reinicia el piso y reparte el loot inicial.</summary>
    public void ServerStartGame()
    {
        if (!IsServer) return;
        _floor.Value = 1;
        _winnerClientId.Value = ulong.MaxValue;
        _playedThisFloor.Clear();

        var inventories = new List<PlayerInventory>();
        foreach (PlayerSoul soul in PlayerSoul.All)
            if (soul.TryGetBodyComponent(out PlayerInventory inventory)) inventories.Add(inventory);

        LootService.DealToAll(inventories, _settings.initialLootCount, _lootCatalog);
    }

    /// <summary>Sortea al siguiente jugador del piso y pasa a ChoosingDoor. Si ya jugaron todos, termina el piso.</summary>
    public void ServerBeginNextTurn()
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

        if (!chosen.IsAlive && chosen.TryGetBodyComponent(out Health health) &&
            chosen.TryGetBodyComponent(out PlayerInventory inventory))
        {
            chosen.ServerSetAlive(true);
            health.RestoreFullHealth();
            // Mismo equipo garantizado y misma cantidad de loot aleatorio que al inicio de partida.
            inventory.ServerGrantStartingItems();
            LootService.GiveRandom(inventory, _settings.initialLootCount, _lootCatalog);
            Debug.Log($"[Game] El jugador {chosen.OwnerClientId} vuelve al juego al comenzar su turno.");
        }

        ChangePhase(GamePhase.ChoosingDoor, _doorSeconds);
    }

    public void ServerEndTurn()
    {
        // TODO etapa 4: pasar por la fase Charity (excedente de mochila al jugador de menor nivel).
        ServerBeginNextTurn();
    }

    private void ServerEndFloor()
    {
        if (!IsServer) return;

        var winners = new List<PlayerSoul>();
        foreach (PlayerSoul soul in PlayerSoul.All)
        {
            if (NetworkManager.ConnectedClients.ContainsKey(soul.OwnerClientId) && soul.IsAlive &&
                soul.Level >= _settings.targetLevel)
                winners.Add(soul);
        }

        if (winners.Count == 1)
        {
            ServerFinishGame(winners[0].OwnerClientId);
            return;
        }

        if (winners.Count > 1 && ServerStartFinalDuel(winners)) return;

        ChangePhase(GamePhase.RestRoom, _restSeconds);
    }

    /// <summary>Vuelve al Hub y cura a todo el grupo al cierre de una ronda.</summary>
    public void ServerEnterRestRoom()
    {
        if (!IsServer) return;

        if (!ServerTeleportEveryoneToHub()) return;
        foreach (PlayerSoul soul in PlayerSoul.All)
            if (soul.IsAlive && NetworkManager.ConnectedClients.ContainsKey(soul.OwnerClientId) &&
                soul.TryGetBodyComponent(out Health health))
                health.RestoreFullHealth();

        ServerDespawnPreviousRoom();
        if (_currentRoom != null)
        {
            _currentRoom.Despawn();
            _currentRoom = null;
        }
        _encounter = null;
        _participants.Clear();
        _combatReady.Clear();
        _currentCombatId.Value = RoomCatalog.EmptyID;
        _currentCombatTreasureAmount.Value = -1;
    }

    private bool ServerTeleportEveryoneToHub()
    {
        Transform hub = _hubAnchor != null ? _hubAnchor : GameObject.Find("Hub")?.transform;
        if (hub == null)
        {
            Debug.LogError("[Game] No se encontró el Hub. Asigna _hubAnchor en el GameManager.", this);
            return false;
        }

        SpawnPoint[] points = hub.GetComponentsInChildren<SpawnPoint>(true);
        int connectedIndex = 0;
        foreach (PlayerSoul soul in PlayerSoul.All)
        {
            if (!NetworkManager.ConnectedClients.ContainsKey(soul.OwnerClientId) ||
                !soul.TryGetBodyComponent(out PlayerMovement movement)) continue;

            Vector3 position;
            if (points.Length > 0)
            {
                position = points[connectedIndex % points.Length].transform.position;
            }
            else
            {
                float angle = connectedIndex * (Mathf.PI * 2f / Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count));
                position = hub.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _hubReturnRadius;
            }

            movement.ServerTeleport(position);
            connectedIndex++;
        }
        return true;
    }

    private bool ServerStartFinalDuel(List<PlayerSoul> finalists)
    {
        if (!IsServer || finalists == null || finalists.Count < 2) return false;

        RoomLayout duelLayout = null;
        NetworkObject duelObject = null;
        bool spawnedDedicatedRoom = false;

        if (_finalDuelRoomPrefab != null)
        {
            Transform anchor = _finalDuelRoomAnchor != null
                ? _finalDuelRoomAnchor
                : (_nextRoomUsesAnchorA ? _roomAnchorA : _roomAnchorB);
            if (anchor != null)
            {
                GameObject roomInstance = Instantiate(_finalDuelRoomPrefab, anchor.position, anchor.rotation);
                if (roomInstance.TryGetComponent(out duelObject) && roomInstance.TryGetComponent(out duelLayout))
                {
                    duelObject.Spawn(true);
                    spawnedDedicatedRoom = true;
                }
                else
                {
                    Debug.LogError("[Game] La sala de desempate necesita NetworkObject y RoomLayout.", _finalDuelRoomPrefab);
                    Destroy(roomInstance);
                    duelObject = null;
                    duelLayout = null;
                }
            }
            else
            {
                Debug.LogError("[Game] Asigna _finalDuelRoomAnchor o al menos un ancla de sala.", this);
            }
        }

        // Fallback para que una referencia de Inspector olvidada no deje el juego bloqueado.
        if (duelLayout == null && _currentRoom != null)
        {
            duelObject = _currentRoom.NetworkObject;
            duelLayout = _currentRoom.Layout;
            Debug.LogWarning("[Game] Se reutiliza la sala actual para el desempate; asigna _finalDuelRoomPrefab para usar una sala dedicada.", this);
        }

        if (duelLayout == null)
        {
            Debug.LogError("[Game] No hay una sala válida para el desempate final.", this);
            return false;
        }

        _finalDuelists.Clear();
        _participants.Clear();
        _combatReady.Clear();
        foreach (PlayerSoul finalist in finalists)
        {
            _finalDuelists.Add(finalist.OwnerClientId);
            _participants.Add(finalist.OwnerClientId);
            if (finalist.TryGetBodyComponent(out Health health))
                health.RestoreFullHealth();
        }

        _finalDuelRoomObject = duelObject;
        _finalDuelRoomLayout = duelLayout;
        if (_finalDuelRoomLayout.ArenaSpawnCount < finalists.Count)
            Debug.LogWarning($"[Game] La sala de desempate tiene {_finalDuelRoomLayout.ArenaSpawnCount} puntos de arena para {finalists.Count} duelistas; se distribuirán alrededor del centro.", this);
        bool returnedToHub = ServerTeleportEveryoneToHub();

        if (spawnedDedicatedRoom && returnedToHub)
        {
            ServerDespawnPreviousRoom();
            if (_currentRoom != null)
            {
                _currentRoom.Despawn();
                _currentRoom = null;
            }
        }
        else if (returnedToHub || !spawnedDedicatedRoom)
        {
            ServerDespawnPreviousRoom();
        }

        _currentCombatId.Value = RoomCatalog.EmptyID;
        _currentCombatTreasureAmount.Value = -1;
        ChangePhase(GamePhase.FinalDuel);
        return true;
    }

    public void ServerMoveFinalDuelistsToArena()
    {
        if (!IsServer || _finalDuelRoomLayout == null) return;

        for (int i = 0; i < _finalDuelists.Count; i++)
        {
            PlayerSoul soul = PlayerSoul.GetFor(_finalDuelists[i]);
            if (soul != null && soul.TryGetBodyComponent(out PlayerMovement movement))
            {
                Vector3 position = i < _finalDuelRoomLayout.ArenaSpawnCount
                    ? _finalDuelRoomLayout.GetArenaSpawn(i)
                    : GetSpreadSpawn(_finalDuelRoomLayout.transform.position, i, _finalDuelists.Count, _hubReturnRadius);
                movement.ServerTeleport(position);
            }
        }
    }

    private static Vector3 GetSpreadSpawn(Vector3 center, int index, int count, float radius)
    {
        float angle = index * (Mathf.PI * 2f / Mathf.Max(1, count));
        float spread = Mathf.Max(1.5f, radius);
        return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spread;
    }

    public void ServerCheckFinalDuel()
    {
        if (!IsServer || _phase.Value != GamePhase.FinalDuel) return;

        ulong survivor = ulong.MaxValue;
        int livingCount = 0;
        foreach (ulong clientId in _finalDuelists)
        {
            PlayerSoul soul = PlayerSoul.GetFor(clientId);
            if (soul == null || !NetworkManager.ConnectedClients.ContainsKey(clientId) ||
                !soul.TryGetBodyComponent(out Health health) || health.IsDead) continue;

            survivor = clientId;
            livingCount++;
        }

        if (livingCount == 1)
        {
            ServerFinishGame(survivor);
        }
        else if (livingCount == 0)
        {
            // Si todos caen en el mismo paso, reinicia el duelo para resolver el empate.
            foreach (ulong clientId in _finalDuelists)
            {
                PlayerSoul soul = PlayerSoul.GetFor(clientId);
                if (soul != null && soul.TryGetBodyComponent(out Health health)) health.RestoreFullHealth();
            }
            ServerMoveFinalDuelistsToArena();
        }
    }

    private void ServerFinishGame(ulong winnerClientId)
    {
        _winnerClientId.Value = winnerClientId;
        Debug.Log($"[Game] Gana el jugador {winnerClientId}.");
        ChangePhase(GamePhase.GameOver);
    }

    public void ServerStartNextFloor()
    {
        _floor.Value++;
        _turnIndex.Value = 0;
        _playedThisFloor.Clear();
        ServerBeginNextTurn();
    }

    // ---------------------------------------------------------------- Servidor: salas

    /// <summary>Abre la sala siguiente (nueva sala, grupo teletransportado) y pasa a EnteringRoom.</summary>
    public void ServerKickDoor()
    {
        Debug.Log($"[Game] El jugador {_currentTurnPlayer.Value} pateo la puerta.");

        if (!ServerTryOpenRoom())
        {
            ServerEndTurn();   // sin salas configuradas se salta el turno
            return;
        }

        ChangePhase(GamePhase.EnteringRoom, _enterRoomSeconds);
    }

    private bool ServerTryOpenRoom()
    {
        Transform anchor = _nextRoomUsesAnchorA ? _roomAnchorA : _roomAnchorB;
        if (_roomCatalog == null || anchor == null)
        {
            Debug.LogError("[Game] Falta asignar RoomCatalog o las anclas de sala en el GameManager.", this);
            return false;
        }

        RoomData roomData = _roomCatalog.GetRandomRoom();
        if (roomData == null)
        {
            Debug.LogError("[Game] No hay salas validas en el RoomCatalog (revisar que tengan prefab).", this);
            return false;
        }

        GameObject roomObject = Instantiate(roomData.roomPrefab, anchor.position, anchor.rotation);
        if (!roomObject.TryGetComponent(out NetworkObject roomNetworkObject) || !roomObject.TryGetComponent(out RoomLayout layout))
        {
            Debug.LogError($"[Game] El prefab de sala {roomData.roomPrefab.name} necesita NetworkObject y RoomLayout.", roomData.roomPrefab);
            Destroy(roomObject);
            return false;
        }
        roomNetworkObject.Spawn(true);

        var newRoom = new ActiveRoom(roomData, layout, roomNetworkObject);
        if (!ServerPopulateRoom(newRoom))
        {
            newRoom.Despawn();
            return false;
        }

        _nextRoomUsesAnchorA = !_nextRoomUsesAnchorA;
        _previousRoom = _currentRoom;
        _currentRoom = newRoom;

        ServerTeleportEveryoneToArrival();
        return true;
    }

    private bool ServerPopulateRoom(ActiveRoom room)
    {
        switch (room.Data)
        {
            case CombatRoomData combatRoom: return ServerSetupCombat(combatRoom, room);
            case TreasureRoomData treasureRoom: return ServerSetupTreasure(treasureRoom, room);
            default:
                Debug.LogError($"[Game] Tipo de sala sin soporte: {room.Data.name}", room.Data);
                return false;
        }
    }

    private bool ServerSetupCombat(CombatRoomData combatRoom, ActiveRoom room)
    {
        BaseCombatData combat = combatRoom.GetRandomCombat();
        if (combat == null)
        {
            Debug.LogError("[Game] La sala de combate no tiene combates posibles.", combatRoom);
            return false;
        }

        // El monstruo aparece ya en la arena, para poder mostrarlo durante la planificacion
        _encounter = new CombatEncounter(combat, room.Layout);
        _encounter.Participants.Add(_currentTurnPlayer.Value);   // TODO: ayudantes
        ServerClearHelpOffer();
        _acceptedHelperClientId = ulong.MaxValue;
        _acceptedHelperReward = 0;

        _participants.Clear();
        _combatReady.Clear();
        foreach (ulong id in _encounter.Participants) _participants.Add(id);
        _currentCombatId.Value = combat.combatID;
        _currentCombatTreasureAmount.Value = combat.treasureAmount;

        combat.ServerSpawn(_encounter);
        _combatCameraTargetPosition.Value = GetCombatCameraTarget(room.Layout);
        return true;
    }

    private Vector3 GetCombatCameraTarget(RoomLayout layout)
    {
        if (_encounter == null || _encounter.Monsters.Count == 0) return layout.transform.position;

        Vector3 center = Vector3.zero;
        int count = 0;
        foreach (NetworkObject monster in _encounter.Monsters)
        {
            if (monster == null) continue;
            center += monster.transform.position;
            count++;
        }
        return count > 0 ? center / count : layout.transform.position;
    }

    private bool ServerSetupTreasure(TreasureRoomData treasureRoom, ActiveRoom room)
    {
        // TODO: varios cofres por sala (decision uno por uno). Hoy solo el primer punto de cofre.
        NetworkObject chestObject = room.Layout.Spawn(treasureRoom.chestPrefab, 0);
        if (chestObject == null) return false;

        if (!chestObject.TryGetComponent(out Chest chest))
        {
            Debug.LogError($"[Game] El prefab de cofre {treasureRoom.chestPrefab.name} necesita el script Chest.", treasureRoom.chestPrefab);
            chestObject.Despawn(true);
            return false;
        }
        room.Chests.Add(chestObject);

        bool isCurse = treasureRoom.RollCurse();
        chest.ServerSetup(isCurse ? ChestType.Curse : ChestType.Treasure, treasureRoom.treasureItemCount, isCurse ? treasureRoom.GetRandomCurse() : null);

        _encounter = null;
        _participants.Clear();
        _combatReady.Clear();
        _currentCombatId.Value = RoomCatalog.EmptyID;
        _currentCombatTreasureAmount.Value = -1;
        return true;
    }

    public void ServerDespawnPreviousRoom()
    {
        if (_previousRoom == null) return;
        _previousRoom.Despawn();
        _previousRoom = null;
    }

    private void ServerTeleportEveryoneToArrival()
    {
        int index = 0;
        foreach (PlayerSoul soul in PlayerSoul.All)
        {
            if (!NetworkManager.ConnectedClients.ContainsKey(soul.OwnerClientId)) continue;
            if (!soul.TryGetBodyComponent(out PlayerMovement movement)) continue;

            movement.ServerTeleport(_currentRoom.Layout.GetArrivalSpawn(index++));   // incluye a los muertos (espectadores)
        }
    }

    /// <summary>Lo llama el cofre (o el estado, si se acaba el tiempo) cuando el cofre ya fue resuelto.</summary>
    public void ServerOnChestResolved()
    {
        if (!IsServer || _phase.Value != GamePhase.ChestDecision) return;
        ServerEndTurn();
    }

    // ---------------------------------------------------------------- Servidor: combate

    private void ServerCheckAllReady()
    {
        if (_phase.Value != GamePhase.CombatPlanning || _participants.Count == 0 || HasPendingHelpOffer) return;

        for (int i = 0; i < _participants.Count; i++)
            if (!_combatReady.Contains(_participants[i])) return;

        ServerStartCountdown();
    }

    public void ServerStartCountdown()
    {
        if (!IsServer || _phase.Value != GamePhase.CombatPlanning) return;
        ServerClearHelpOffer();
        if (_countdownSeconds > 0f) ChangePhase(GamePhase.CombatCountdown, _countdownSeconds);
        else ChangePhase(GamePhase.Combat);
    }

    private void ServerClearHelpOffer()
    {
        if (!IsServer) return;
        _pendingHelpTarget.Value = ulong.MaxValue;
        _pendingHelpReward.Value = 0;
    }

    public void ServerMoveParticipantsToArena()
    {
        if (_encounter == null || _currentRoom == null) return;

        int index = 0;
        foreach (ulong id in _encounter.Participants)
        {
            PlayerSoul soul = PlayerSoul.GetFor(id);
            if (soul == null || !soul.TryGetBodyComponent(out PlayerMovement movement)) continue;

            movement.ServerTeleport(_currentRoom.Layout.GetArenaSpawn(index++));
        }
    }

    private void ServerReturnParticipantsToArrival()
    {
        if (_encounter == null || _currentRoom == null) return;

        int index = 0;
        foreach (ulong id in _encounter.Participants)
        {
            PlayerSoul soul = PlayerSoul.GetFor(id);
            if (soul == null || !soul.TryGetBodyComponent(out PlayerMovement movement)) continue;

            movement.ServerTeleport(_currentRoom.Layout.GetArrivalSpawn(index++));
        }
    }

    public void ServerResolveCombat(bool victory)
    {
        if (!IsServer || _phase.Value != GamePhase.Combat || _encounter == null) return;
        // La salvación de derrota solo se habilita cuando no queda ningún participante en pie.
        if (!victory && !_encounter.AllParticipantsDown()) return;
        _combatVictory = victory;
        _combatResolutionApplied = false;
        ServerClearEncounterCombatModifiers();
        Debug.Log($"[Game] Combate {(victory ? "GANADO" : "PERDIDO")}.");
        ChangePhase(GamePhase.Resolution, _resolutionSeconds);
    }

    private void ServerClearEncounterCombatModifiers()
    {
        if (!IsServer || _encounter == null) return;
        foreach (Stats affectedStats in _encounter.AffectedStats)
            if (affectedStats != null && affectedStats.IsSpawned)
                affectedStats.ServerClearCombatModifiers();
        _encounter.AffectedStats.Clear();
    }

    /// <summary>Entrega una sola vez las recompensas del encuentro ganado.</summary>
    public void ServerApplyCombatResolution()
    {
        if (!IsServer || _phase.Value != GamePhase.Resolution || _combatResolutionApplied) return;
        _combatResolutionApplied = true;

        if (_encounter == null) return;
        if (!_combatVictory)
        {
            ServerResolveCombatDefeat();
            return;
        }

        if (_lootCatalog == null)
            Debug.LogWarning("[Game] No se pueden entregar recompensas: falta _lootCatalog.", this);
        else
        {
            int totalReward = _encounter.Data.treasureAmount;
            PlayerSoul helper = _acceptedHelperClientId != ulong.MaxValue
                ? PlayerSoul.GetFor(_acceptedHelperClientId)
                : null;
            PlayerInventory helperInventory = null;
            bool helperGetsReward = helper != null &&
                NetworkManager.ConnectedClients.ContainsKey(_acceptedHelperClientId) &&
                _encounter.Participants.Contains(_acceptedHelperClientId) &&
                helper.TryGetBodyComponent(out helperInventory);
            int helperReward = helperGetsReward ? Mathf.Clamp(_acceptedHelperReward, 0, totalReward) : 0;

            PlayerSoul rewardOwner = PlayerSoul.GetFor(_currentTurnPlayer.Value);
            if (rewardOwner != null && rewardOwner.TryGetBodyComponent(out PlayerInventory ownerInventory))
                LootService.GiveRandom(ownerInventory, totalReward - helperReward, _lootCatalog);

            if (helperGetsReward)
                LootService.GiveRandom(helperInventory, helperReward, _lootCatalog);
        }

        PlayerSoul turnOwner = PlayerSoul.GetFor(_currentTurnPlayer.Value);
        if (turnOwner == null) return;

        // La regla de la partida es que ganar un combate siempre sube al menos un nivel.
        // El dato puede configurar una recompensa mayor, pero no anular la progresion.
        int levels = Mathf.Max(1, _encounter.Data.levelsReward);
        int newLevel = Mathf.Min(_settings.targetLevel, turnOwner.Level + levels);
        turnOwner.ServerSetLevel(newLevel);
        ServerApplyPostCombatRecovery();
        Debug.Log($"[Game] El dueño del turno {turnOwner.OwnerClientId} sube a nivel {newLevel}.");
    }

    /// <summary>Resuelve una salvacion independiente para cada participante caido.</summary>
    private void ServerResolveCombatDefeat()
    {
        if (!IsServer || _encounter == null) return;

        BadThing badThing = _encounter.Data.badThing;
        EscapeCondition escape = _encounter.Data.escapeCondition;
        foreach (ulong clientId in _encounter.Participants)
        {
            if (!NetworkManager.ConnectedClients.ContainsKey(clientId)) continue;
            PlayerSoul soul = PlayerSoul.GetFor(clientId);
            if (soul == null || !soul.TryGetBodyComponent(out Health health)) continue;

            int roll = UnityEngine.Random.Range(1, 21);
            bool escaped = escape == null || escape.IsEscaped(roll);
            if (escaped)
            {
                soul.ServerSetAlive(true);
                ServerApplyPostCombatRecovery(health);
                Debug.Log($"[Game] {clientId} escapa de la consecuencia ({roll}/20).");
                continue;
            }

            ApplyBadThing(soul, health, badThing);
            Debug.Log($"[Game] {clientId} falla la salvacion ({roll}/20) y recibe {(badThing != null ? badThing.type.ToString() : BadThingType.None.ToString())}.");
        }
    }

    private void ApplyBadThing(PlayerSoul soul, Health health, BadThing badThing)
    {
        if (badThing == null || badThing.type == BadThingType.None)
        {
            soul.ServerSetAlive(true);
            ServerApplyPostCombatRecovery(health);
            return;
        }

        switch (badThing.type)
        {
            case BadThingType.Death:
                ServerKillPlayer(soul);
                return;

            case BadThingType.LoseItems:
                if (soul.TryGetBodyComponent(out PlayerInventory inventory))
                {
                    List<ItemDataBase> backpack = inventory.GetBackpack();
                    int itemsToLose = Mathf.Min(Mathf.Max(0, badThing.amount), backpack.Count);
                    for (int i = 0; i < itemsToLose; i++)
                    {
                        int index = UnityEngine.Random.Range(0, backpack.Count);
                        inventory.RemoveItem(backpack[index].itemID);
                        backpack.RemoveAt(index);
                    }
                }
                break;

            case BadThingType.LoseLevels:
                soul.ServerSetLevel(Mathf.Max(1, soul.Level - Mathf.Max(0, badThing.amount)));
                break;
        }

        // Solo Death deja al personaje muerto. Los demas castigos se aplican y el jugador
        // vuelve con la vida restaurada para poder continuar el turno.
        soul.ServerSetAlive(true);
        ServerApplyPostCombatRecovery(health);
    }

    private void ServerKillPlayer(PlayerSoul soul)
    {
        if (!IsServer || soul == null || !soul.IsAlive) return;

        if (soul.TryGetBodyComponent(out PlayerInventory inventory)) inventory.ServerClearAllItems();
        if (soul.TryGetBodyComponent(out PlayerCurses curses)) curses.ServerClearAll();
        soul.ServerSetAlive(false);
        Debug.Log($"[Game] El jugador {soul.OwnerClientId} murió; perdió sus objetos y maldiciones. Conserva nivel.");
    }

    private void ServerApplyPostCombatRecovery()
    {
        if (!IsServer || _encounter == null) return;

        foreach (ulong clientId in _encounter.Participants)
        {
            PlayerSoul soul = PlayerSoul.GetFor(clientId);
            if (soul == null || !soul.TryGetBodyComponent(out Health health)) continue;
            soul.ServerSetAlive(true);
            ServerApplyPostCombatRecovery(health);
        }
    }

    private void ServerApplyPostCombatRecovery(Health health)
    {
        if (!IsServer || health == null) return;

        float maxHealth = health.MaxHealth;
        if (maxHealth <= 0f) return;
        float recoveryPercent = health.IsDead ? Mathf.Max(_postCombatRecoveryPercent, 0.01f) : _postCombatRecoveryPercent;
        if (recoveryPercent <= 0f || (!health.IsDead && health.CurrentHealth / maxHealth >= _postCombatRecoveryThreshold)) return;

        // Una caída en combate no puede dejar al jugador con 0 HP tras una victoria/rescate.
        health.RestoreHealthPercent(recoveryPercent);
    }

    /// <summary>Cierra el combate (monstruos y participantes) y pasa el turno. La sala se queda.</summary>
    public void ServerEndCombat()
    {
        if (!IsServer) return;
        if (_encounter != null)
        {
            ServerReturnParticipantsToArrival();
            ServerClearEncounterCombatModifiers();
            _encounter.Cleanup();
            _encounter = null;
        }

        ServerDespawnPreviousRoom();   // por si se aborto durante EnteringRoom

        _participants.Clear();
        _combatReady.Clear();
        ServerClearHelpOffer();
        _acceptedHelperClientId = ulong.MaxValue;
        _acceptedHelperReward = 0;
        _currentCombatId.Value = RoomCatalog.EmptyID;
        _currentCombatTreasureAmount.Value = -1;
        _combatVictory = false;
        _combatResolutionApplied = false;

        ServerEndTurn();
    }

    private void ServerOnClientDisconnected(ulong clientId)
    {
        if (_phase.Value == GamePhase.FinalDuel)
        {
            _finalDuelists.Remove(clientId);
            _participants.Remove(clientId);
            ServerCheckFinalDuel();
            return;
        }

        switch (_phase.Value)
        {
            case GamePhase.ChoosingDoor:
            case GamePhase.ChestDecision:
                if (clientId == _currentTurnPlayer.Value) ServerBeginNextTurn();
                break;

            case GamePhase.EnteringRoom:
            case GamePhase.CombatPlanning:
            case GamePhase.CombatCountdown:
            case GamePhase.Combat:
            case GamePhase.Resolution:
                if (_encounter == null)
                {
                    // sala de tesoro: no hay combate que abortar
                    if (clientId == _currentTurnPlayer.Value)
                    {
                        ServerDespawnPreviousRoom();
                        ServerBeginNextTurn();
                    }
                    break;
                }

                if (clientId == _currentTurnPlayer.Value)
                {
                    Debug.Log("[Game] Se desconecto el dueño del turno: se aborta el combate.");
                    ServerEndCombat();
                }
                else
                {
                    if (clientId == _pendingHelpTarget.Value) ServerClearHelpOffer();
                    if (clientId == _acceptedHelperClientId)
                    {
                        _acceptedHelperClientId = ulong.MaxValue;
                        _acceptedHelperReward = 0;
                    }
                    _encounter.Participants.Remove(clientId);
                    _participants.Remove(clientId);
                    _combatReady.Remove(clientId);
                    ServerCheckAllReady();
                }
                break;
        }
        // TODO: contemplar el resto de las fases cuando existan.
    }

}

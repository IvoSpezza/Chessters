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
    GameOver          // TODO victoria (sin estado todavia)
}

/// <summary>
/// Autoridad del flujo de la partida. Objeto de la escena Game (con NetworkObject).
/// Guarda los datos replicados, los hooks y los RPC. La logica de cada fase vive en un estado (carpeta GameStates),
/// y la maquina de estados corre solo en el servidor. Los clientes leen la fase replicada.
/// Las salas persisten: el siguiente jugador patea la puerta desde la sala donde esta el grupo.
/// </summary>
public class GameManager : NetworkBehaviour
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
    private StateMachine _machine;
    private readonly HashSet<ulong> _playedThisFloor = new HashSet<ulong>();
    private ActiveRoom _currentRoom;
    private ActiveRoom _previousRoom;     // se destruye al terminar EnteringRoom
    private CombatEncounter _encounter;
    private bool _nextRoomUsesAnchorA = true;

    // Lectura (todos)
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

    // Lectura para los estados (solo servidor)
    public CombatEncounter Encounter => _encounter;
    public RoomType CurrentRoomType => _currentRoom != null ? _currentRoom.Data.type : RoomType.Combat;
    public float PreparationSeconds => _preparationSeconds;
    public float PlanningSeconds => _planningSeconds;
    public float ChestDecisionSeconds => _chestDecisionSeconds;
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

        ChangePhase(GamePhase.WaitingForBodies);
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
        _machine?.MachineUpdate();
    }

    // ---------------------------------------------------------------- Servidor: fases

    /// <summary>Cambia la fase (replicada) y el estado de la maquina. Con seconds &gt; 0 la fase tiene tiempo limite.</summary>
    public void ChangePhase(GamePhase phase, float seconds = 0f)
    {
        if (!IsServer) return;

        _phaseEndTime.Value = seconds > 0f ? NetworkManager.ServerTime.Time + seconds : 0;
        _phase.Value = phase;
        _machine.ChangeState(phase);
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
        _floor.Value = 1;
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

        // TODO 2B: si el jugador esta muerto, resucitar (ServerSetAlive, RestoreFullHealth, PlayerCurses.ServerClearAll)
        // y darle la misma cantidad de items que el loot inicial (LootService.GiveRandom).

        ChangePhase(GamePhase.ChoosingDoor, _doorSeconds);
    }

    public void ServerEndTurn()
    {
        // TODO etapa 4: pasar por la fase Charity (excedente de mochila al jugador de menor nivel).
        ServerBeginNextTurn();
    }

    private void ServerEndFloor()
    {
        // TODO: chequeo de victoria (nivel objetivo) y combate de desempate entre jugadores.
        // TODO etapa 4: sala de descanso real (curar vivos, habilitar trade).
        if (_restSeconds > 0f) ChangePhase(GamePhase.RestRoom, _restSeconds);
        else ServerStartNextFloor();
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

        _participants.Clear();
        _combatReady.Clear();
        foreach (ulong id in _encounter.Participants) _participants.Add(id);
        _currentCombatId.Value = combat.combatID;

        combat.ServerSpawn(_encounter);
        return true;
    }

    private bool ServerSetupTreasure(TreasureRoomData treasureRoom, ActiveRoom room)
    {
        // TODO: varios cofres por sala (decision uno por uno). Hoy solo el primer punto de cofre.
        NetworkObject chestObject = room.Layout.Spawn(treasureRoom.chestPrefab, SpawnKind.Chest, 0);
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
        if (_phase.Value != GamePhase.CombatPlanning || _participants.Count == 0) return;

        for (int i = 0; i < _participants.Count; i++)
            if (!_combatReady.Contains(_participants[i])) return;

        ServerStartCountdown();
    }

    public void ServerStartCountdown()
    {
        if (_countdownSeconds > 0f) ChangePhase(GamePhase.CombatCountdown, _countdownSeconds);
        else ChangePhase(GamePhase.Combat);
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
        Debug.Log($"[Game] Combate {(victory ? "GANADO" : "PERDIDO")}.");
        ChangePhase(GamePhase.Resolution, _resolutionSeconds);
    }

    /// <summary>Cierra el combate (monstruos y participantes) y pasa el turno. La sala se queda.</summary>
    public void ServerEndCombat()
    {
        if (_encounter != null)
        {
            ServerReturnParticipantsToArrival();
            _encounter.Cleanup();
            _encounter = null;
        }

        ServerDespawnPreviousRoom();   // por si se aborto durante EnteringRoom

        _participants.Clear();
        _combatReady.Clear();
        _currentCombatId.Value = RoomCatalog.EmptyID;

        ServerEndTurn();
    }

    private void ServerOnClientDisconnected(ulong clientId)
    {
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
                    _encounter.Participants.Remove(clientId);
                    _participants.Remove(clientId);
                    _combatReady.Remove(clientId);
                    ServerCheckAllReady();
                }
                break;
        }
        // TODO: contemplar el resto de las fases cuando existan.
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
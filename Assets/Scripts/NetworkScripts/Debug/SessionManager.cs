using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionManager : MonoBehaviour
{
    private enum Cifration
    {
        dtls,
        udp
    }
    public static SessionManager instance { get; private set; }

    [SerializeField] private int maxPlayers = 6;
    [SerializeField, Min(2)] private int minPlayers = 2;
        
    [Header("Red")]
    [Tooltip("Activado: Relay. Desactivado: modo de prueba local; host y clientes usan localhost en esta máquina.")]
    [SerializeField] private bool useRelay = true;
    private const ushort DirectTestPort = 7777;

    [Header("Relay")]
    [Tooltip("dtls = cifrado (recomendado). udp = sin cifrar.")]
    [SerializeField] private Cifration _connectionTypeEnum = Cifration.dtls;
    [Tooltip("Opcional: perfil de autenticación. Útil si dos instancias locales comparten el mismo Player ID.")]
    [SerializeField] private string authProfile = "";

    [Header("Escenas")]
    [SerializeField] private string menuScene = "MainMenu";
    [SerializeField] private string lobbyScene = "Lobby";
    [SerializeField] private string gameScene = "Game";

    [Header("Cuerpo")]
    [Tooltip("Prefab del CUERPO del jugador. El alma es el Default Player Prefab del NetworkManager.")]
    [SerializeField] private NetworkObject bodyPrefab;

    private string _connectionType;
    private enum SessionState { Menu, Lobby, InGame }
    private SessionState _state = SessionState.Menu;
    private bool _sceneReady;   
    private bool _connecting;   
    private string _lobbyId = "";
    private string _relayJoinCode = "";
    private bool _isLobbyHost;
    private Coroutine _heartbeatCoroutine;
    private bool _cleaningLobby;
    private const string RelayCodeKey = "relayJoinCode";
    private const string LobbyStateKey = "gameState";

    /// <summary>Código de Unity Lobby para compartir con los demás jugadores.</summary>
    public string JoinCode { get; private set; } = "";
    public string LocalPlayerName { get; private set; } = "";
    public int MinPlayers => minPlayers;
    public int MaxPlayers => maxPlayers;
    public bool UsesRelay => useRelay;

    /// <summary>Se dispara cuando falla el inicio de host o cliente. La UI muestra el mensaje.</summary>
    public event Action<string> OnConnectionError;

    private NetworkManager Net => NetworkManager.Singleton;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        _connectionType = _connectionTypeEnum.Equals(Cifration.dtls) ? "dtls" : "udp";
        LocalPlayerName = PlayerPrefs.GetString("Chessters.PlayerName", "");
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        Net.OnServerStarted += OnServerStarted;
        Net.OnServerStopped += OnServerStopped;
        Net.OnClientDisconnectCallback += OnClientDisconnected;
        Net.ConnectionApprovalCallback += OnConnectionApproval;
    }

    private void OnDestroy()
    {
        if (Net == null) return;
        Net.OnServerStarted -= OnServerStarted;
        Net.OnServerStopped -= OnServerStopped;
        Net.OnClientDisconnectCallback -= OnClientDisconnected;
        Net.ConnectionApprovalCallback -= OnConnectionApproval;
        if (_heartbeatCoroutine != null) StopCoroutine(_heartbeatCoroutine);
    }

    // ---------- API para la UI ----------
    public async void StartHost()
    {
        if (_connecting || Net.IsListening) return;
        _connecting = true;

        try
        {
            if (useRelay)
            {
                await InitServicesAsync();

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
                _relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                Net.GetComponent<UnityTransport>().SetRelayServerData(
                    AllocationUtils.ToRelayServerData(allocation, _connectionType));

                var hostPlayer = new Player(
                    id: AuthenticationService.Instance.PlayerId,
                    allocationId: allocation.AllocationId.ToString(),
                    profile: new PlayerProfile(LocalPlayerName));
                var lobbyOptions = new CreateLobbyOptions
                {
                    IsPrivate = true,
                    IsLocked = false,
                    Player = hostPlayer,
                    Data = new Dictionary<string, DataObject>
                    {
                        { RelayCodeKey, new DataObject(DataObject.VisibilityOptions.Member, _relayJoinCode) },
                        { LobbyStateKey, new DataObject(DataObject.VisibilityOptions.Public, "Waiting") }
                    }
                };
                Lobby lobby = await LobbyService.Instance.CreateLobbyAsync("Chesters", maxPlayers, lobbyOptions);
                SetLobby(lobby, isHost: true);
            }
            else
            {
                SetConnectionData();
            }

            if (!Net.StartHost())
            {
                Fail("No se pudo iniciar el host.");
                _ = LeaveLobbyAsync(deleteLobby: true);
            }
        }
        catch (Exception e)
        {
            Fail(useRelay
                ? "No se pudo crear la sala. Revisá la configuración de Unity Services y Relay."
                : "No se pudo iniciar el host con conexión directa. Revisá la configuración de red.", e);
            _ = LeaveLobbyAsync(deleteLobby: true);
        }
        finally
        {
            _connecting = false;
        }
    }

    /// <summary>Con Relay recibe un código de sala; sin Relay se conecta a localhost para pruebas.</summary>
    public async void StartClient(string address = null)
    {
        if (_connecting || Net.IsListening) return;
        _connecting = true;

        try
        {
            string target = (address ?? "").Trim();
            if (useRelay && string.IsNullOrEmpty(target))
            {
                Fail("Ingresá el código de la sala.");
                return;
            }

            if (useRelay)
            {
                string code = target.ToUpperInvariant();
                await InitServicesAsync();
                var joiningPlayer = new Player(
                    id: AuthenticationService.Instance.PlayerId,
                    profile: new PlayerProfile(LocalPlayerName));
                Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code,
                    new JoinLobbyByCodeOptions { Player = joiningPlayer });
                SetLobby(lobby, isHost: false);

                if (lobby.Data == null || !lobby.Data.TryGetValue(RelayCodeKey, out DataObject relayData) ||
                    string.IsNullOrWhiteSpace(relayData.Value))
                    throw new InvalidOperationException("La sala no tiene datos de conexión de Relay.");

                _relayJoinCode = relayData.Value;
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(_relayJoinCode);
                Net.GetComponent<UnityTransport>().SetRelayServerData(
                    AllocationUtils.ToRelayServerData(joinAllocation, _connectionType));
                await LobbyService.Instance.UpdatePlayerAsync(_lobbyId, AuthenticationService.Instance.PlayerId,
                    new UpdatePlayerOptions { AllocationId = joinAllocation.AllocationId.ToString() });
            }
            else
            {
                SetConnectionData();
            }

            if (!Net.StartClient())
            {
                Fail("No se pudo iniciar el cliente.");
                _ = LeaveLobbyAsync(deleteLobby: false);
            }
        }
        catch (LobbyServiceException e)
        {
            Fail("Código de Lobby inválido, sala llena o sala no disponible.", e);
            _ = LeaveLobbyAsync(deleteLobby: false);
        }
        catch (RelayServiceException e)
        {
            Fail("No se pudo conectar a Relay desde esta sala.", e);
            _ = LeaveLobbyAsync(deleteLobby: false);
        }
        catch (Exception e)
        {
            Fail(useRelay
                ? "No se pudo conectar a la sala. Revisá tu conexión a internet."
                : "No se pudo conectar al host local. Confirmá que haya una instancia host activa.", e);
            _ = LeaveLobbyAsync(deleteLobby: false);
        }
        finally
        {
            _connecting = false;
        }
    }

    public void StartGame()
    {
        if (!Net.IsServer || _state != SessionState.Lobby) return;
        _state = SessionState.InGame;
        _sceneReady = false;
        _ = UpdateLobbyGameStateAsync("InGame", isLocked: true);
        Net.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);
    }

    /// <summary>Devuelve a todos los clientes conectados al lobby al terminar la partida.</summary>
    public void ServerReturnToLobby()
    {
        if (Net == null || !Net.IsServer || _state != SessionState.InGame) return;

        _state = SessionState.Lobby;
        _sceneReady = false;
        _ = UpdateLobbyGameStateAsync("Waiting", isLocked: false);
        Net.SceneManager.LoadScene(lobbyScene, LoadSceneMode.Single);
    }

    public void SetLocalPlayerName(string playerName)
    {
        LocalPlayerName = (playerName ?? string.Empty).Trim();
        if (LocalPlayerName.Length > 24) LocalPlayerName = LocalPlayerName.Substring(0, 24);
        PlayerPrefs.SetString("Chessters.PlayerName", LocalPlayerName);
        PlayerPrefs.Save();
    }
    // ---------- Servicios de Unity (Relay) ----------
    // Se inicializa una sola vez y se reutiliza (la sesion anonima queda abierta).
    private async Task InitServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            if (!string.IsNullOrEmpty(authProfile)) options.SetProfile(authProfile);
            await UnityServices.InitializeAsync(options);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void Fail(string message, Exception e = null)
    {
        JoinCode = "";
        if (e != null) Debug.LogException(e);
        Debug.LogWarning($"[SessionManager] {message}");
        OnConnectionError?.Invoke(message);
    }

    // ---------- Server ----------
    private void OnServerStarted()
    {
        _state = SessionState.Lobby;
        _sceneReady = false;

        Net.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;       // host / cambios de escena
        Net.SceneManager.OnSynchronizeComplete += OnClientSynchronized;      // clientes que entran tarde
        PlayerSoul.OnServerSoulSpawned += OnSoulSpawned;                     // alma del host (puede llegar tarde)

        Net.SceneManager.LoadScene(lobbyScene, LoadSceneMode.Single);
    }

    private void OnServerStopped(bool shutdown)
    {
        _state = SessionState.Menu;
        _sceneReady = false;
        JoinCode = "";
        if (_heartbeatCoroutine != null) StopCoroutine(_heartbeatCoroutine);
        PlayerSoul.OnServerSoulSpawned -= OnSoulSpawned;
        if (Net.SceneManager != null)
        {
            Net.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
            Net.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
        }
        if (_isLobbyHost) _ = LeaveLobbyAsync(deleteLobby: true);
        SceneManager.LoadScene(menuScene);
    }

    private void OnConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
                                      NetworkManager.ConnectionApprovalResponse response)
    {
        bool hasRoom = Net.ConnectedClientsIds.Count < maxPlayers;
        bool open = _state == SessionState.Menu || _state == SessionState.Lobby; // Menu = el propio host

        response.Approved = open && hasRoom;
        response.CreatePlayerObject = true;             // NGO crea el ALMA (Default Player Prefab)
        Debug.Log($"[Approval] cliente={request.ClientNetworkId} estado={_state} prefabDelAlma={(Net.NetworkConfig.PlayerPrefab != null)}");
        if (!response.Approved)
            response.Reason = !open ? "La partida ya empezó" : "Sala llena";
    }

    private void OnSceneLoadCompleted(string scene, LoadSceneMode mode,
                                      List<ulong> completed, List<ulong> timedOut)
    {
        _sceneReady = true;
        foreach (ulong id in completed) SpawnBody(id);
    }

    // El alma del host puede spawnear DESPUES de que la escena cargo. En ese caso el cuerpo
    // se crea ahora. Los clientes que entran tarde pasan por OnClientSynchronized.
    private void OnSoulSpawned(PlayerSoul soul)
    {
        if (!_sceneReady) return;                                    // lo hara OnSceneLoadCompleted
        if (soul.OwnerClientId != NetworkManager.ServerClientId) return;

        StartCoroutine(SpawnBodyNextFrame(soul.OwnerClientId));
    }

    // Un frame de espera: no se spawnea un objeto adentro del OnNetworkSpawn de otro.
    private System.Collections.IEnumerator SpawnBodyNextFrame(ulong clientId)
    {
        yield return null;
        if (Net != null && Net.IsServer) SpawnBody(clientId);
    }

    private void OnClientSynchronized(ulong clientId) => SpawnBody(clientId);

    // El alma ya existe (la crea NGO al conectar). Aca solo se crea el cuerpo y se vincula.
    private void SpawnBody(ulong clientId)
    {
        PlayerSoul soul = PlayerSoul.GetFor(clientId);
        if (soul == null)
        {
            Debug.Log($"[SessionManager] El cliente {clientId} todavia no tiene alma; el cuerpo se creara cuando exista.");
            return;
        }

        // Evita duplicados si ambos eventos disparan para el mismo cliente
        if (soul.HasBody) return;

        Vector3 pos = GetSpawnPosition(clientId);
        NetworkObject body = Instantiate(bodyPrefab, pos, Quaternion.identity);
        body.SpawnWithOwnership(clientId, true);   // true = se destruye con la escena
        soul.ServerSetBody(body);
    }

    private Vector3 GetSpawnPosition(ulong clientId)
    {
        if (SceneManager.GetActiveScene().name == lobbyScene)
        {
            LobbyDoor door = FindFirstObjectByType<LobbyDoor>();
            if (door != null && door.TryGetSpawnTransform(clientId, out Transform lobbySpawn))
                return lobbySpawn.position;
        }

        var points = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.InstanceID);
        if (points.Length == 0) return Vector3.zero;
        return points[(int)(clientId % (ulong)points.Length)].transform.position;
    }

    // ---------- Cliente ----------
    private void OnClientDisconnected(ulong clientId)
    {
        // Solo me interesa cuando soy yo el que se desconectó (cliente, no host)
        if (Net.IsServer) return;
        Debug.Log($"Desconectado: {Net.DisconnectReason}");
        JoinCode = "";
        _ = LeaveLobbyAsync(deleteLobby: false);
        SceneManager.LoadScene(menuScene);
    }

    private void SetLobby(Lobby lobby, bool isHost)
    {
        _lobbyId = lobby.Id;
        JoinCode = lobby.LobbyCode;
        _isLobbyHost = isHost;
        if (isHost) _heartbeatCoroutine = StartCoroutine(HeartbeatLobby());
    }

    private void SetConnectionData()
    {
        // Modo de prueba local: host y clientes corren en esta misma máquina.
        Net.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", DirectTestPort);
    }

    private System.Collections.IEnumerator HeartbeatLobby()
    {
        var wait = new WaitForSecondsRealtime(15f);
        while (!string.IsNullOrEmpty(_lobbyId) && _isLobbyHost)
        {
            yield return wait;
            if (!string.IsNullOrEmpty(_lobbyId)) _ = SendLobbyHeartbeatAsync(_lobbyId);
        }
    }

    private async Task SendLobbyHeartbeatAsync(string lobbyId)
    {
        try { await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId); }
        catch (Exception e) { Debug.LogWarning($"[Lobby] No se pudo renovar la sala: {e.Message}"); }
    }

    private async Task UpdateLobbyGameStateAsync(string gameState, bool isLocked)
    {
        if (!_isLobbyHost || string.IsNullOrEmpty(_lobbyId)) return;
        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(_lobbyId, new UpdateLobbyOptions
            {
                IsLocked = isLocked,
                Data = new Dictionary<string, DataObject>
                {
                    { LobbyStateKey, new DataObject(DataObject.VisibilityOptions.Public, gameState) }
                }
            });
        }
        catch (Exception e) { Debug.LogWarning($"[Lobby] No se pudo actualizar el estado de la sala: {e.Message}"); }
    }

    private async Task LeaveLobbyAsync(bool deleteLobby)
    {
        if (_cleaningLobby || string.IsNullOrEmpty(_lobbyId)) return;
        _cleaningLobby = true;
        string lobbyId = _lobbyId;
        string playerId = AuthenticationService.Instance != null ? AuthenticationService.Instance.PlayerId : "";
        bool wasHost = _isLobbyHost;

        _lobbyId = "";
        _relayJoinCode = "";
        _isLobbyHost = false;
        JoinCode = "";
        if (_heartbeatCoroutine != null)
        {
            StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }

        try
        {
            if (deleteLobby && wasHost) await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            else if (!string.IsNullOrEmpty(playerId)) await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
        }
        catch (Exception e) { Debug.LogWarning($"[Lobby] No se pudo limpiar la sala: {e.Message}"); }
        finally { _cleaningLobby = false; }
    }
}

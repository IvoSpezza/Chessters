
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using RaybelCreation.Netcode.Transports.Steam;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionManager : MonoBehaviour
{
    public enum NetworkMode
    {
        Local,
        Steam
    }

    private enum SessionState
    {
        Menu,
        Lobby,
        InGame
    }

    public static SessionManager instance { get; private set; }

    [Header("Jugadores")]
    [SerializeField] private int maxPlayers = 6;
    [SerializeField, Min(2)] private int minPlayers = 2;

    [Header("Configuración de red")]
    [SerializeField] private NetworkMode networkMode = NetworkMode.Local;

    private const ushort DirectTestPort = 7777;

    private UnityTransport _unityTransport;
    private SteamNetworkTransport _steamTransport;

    [Header("Escenas")]
    [SerializeField] private string menuScene = "MainMenu";
    [SerializeField] private string lobbyScene = "Lobby";
    [SerializeField] private string gameScene = "Game";

    [Header("Cuerpo")]
    [Tooltip("Prefab del CUERPO del jugador. El alma es el Default Player Prefab del NetworkManager.")]
    [SerializeField] private NetworkObject bodyPrefab;

    private SessionState _state = SessionState.Menu;

    private bool _sceneReady;
    private bool _connecting;

    /// <summary>
    /// En modo Steam contiene el SteamID del host.
    /// En modo Local queda vacío.
    /// </summary>
    public string JoinCode { get; private set; } = "";

    public string LocalPlayerName { get; private set; } = "";

    public int MinPlayers => minPlayers;
    public int MaxPlayers => maxPlayers;

    public NetworkMode CurrentNetworkMode => networkMode;

    public event Action<string> OnConnectionError;

    private NetworkManager Net => NetworkManager.Singleton;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        LocalPlayerName = PlayerPrefs.GetString(
            "Chessters.PlayerName",
            ""
        );

        DontDestroyOnLoad(gameObject);

        _unityTransport = GetComponent<UnityTransport>();
        _steamTransport = GetComponent<SteamNetworkTransport>();

        if (_unityTransport == null)
        {
            Debug.LogError(
                "[SessionManager] No se encontró UnityTransport."
            );
        }

        if (_steamTransport == null)
        {
            Debug.LogError(
                "[SessionManager] No se encontró SteamNetworkTransport."
            );
        }
    }

    private void Start()
    {
        if (Net == null)
        {
            Debug.LogError(
                "[SessionManager] No se encontró NetworkManager.Singleton."
            );
            return;
        }

        Net.OnServerStarted += OnServerStarted;
        Net.OnServerStopped += OnServerStopped;
        Net.OnClientDisconnectCallback += OnClientDisconnected;
        Net.ConnectionApprovalCallback += OnConnectionApproval;
    }

    private void OnDestroy()
    {
        if (Net != null)
        {
            Net.OnServerStarted -= OnServerStarted;
            Net.OnServerStopped -= OnServerStopped;
            Net.OnClientDisconnectCallback -= OnClientDisconnected;
            Net.ConnectionApprovalCallback -= OnConnectionApproval;

            if (Net.SceneManager != null)
            {
                Net.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
                Net.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
            }
        }

        PlayerSoul.OnServerSoulSpawned -= OnSoulSpawned;

        if (instance == this)
            instance = null;
    }

    // =========================================================
    // CONFIGURACIÓN DEL TRANSPORTE
    // =========================================================

    private bool ConfigureTransport()
    {
        if (Net == null)
        {
            Fail("No se encontró NetworkManager.");
            return false;
        }

        switch (networkMode)
        {
            case NetworkMode.Local:

                if (_unityTransport == null)
                {
                    Fail("No se encontró Unity Transport.");
                    return false;
                }

                Net.NetworkConfig.NetworkTransport = _unityTransport;

                _unityTransport.SetConnectionData(
                    "127.0.0.1",
                    DirectTestPort
                );

                Debug.Log(
                    "[SessionManager] Usando Unity Transport local."
                );

                return true;

            case NetworkMode.Steam:

                if (_steamTransport == null)
                {
                    Fail("No se encontró Steam Network Transport.");
                    return false;
                }

                if (SteamBootstrap.Instance == null ||
                    !SteamBootstrap.Instance.IsInitialized)
                {
                    Fail("Steam no está inicializado.");
                    return false;
                }

                // Seis jugadores en total: el host admite cinco conexiones.
                _steamTransport.MaxRemoteConnections = maxPlayers - 1;

                Net.NetworkConfig.NetworkTransport = _steamTransport;

                Debug.Log(
                    "[SessionManager] Usando Steam Network Transport."
                );

                return true;

            default:
                Fail("El modo de red seleccionado no es válido.");
                return false;
        }
    }

    // =========================================================
    // API PARA LA UI
    // =========================================================

    public void StartHost()
    {
        if (_connecting || Net == null || Net.IsListening)
            return;

        _connecting = true;

        try
        {
            if (!ConfigureTransport())
                return;

            if (networkMode == NetworkMode.Steam)
            {
                JoinCode = SteamUser.GetSteamID().m_SteamID.ToString();

                Debug.Log(
                    $"[SessionManager] SteamID del Host: {JoinCode}"
                );
            }
            else
            {
                JoinCode = "";
            }

            if (!Net.StartHost())
            {
                Fail("No se pudo iniciar el Host.");
                return;
            }

            Debug.Log("[SessionManager] Host iniciado correctamente.");
        }
        catch (Exception e)
        {
            Fail("Error al iniciar el Host.", e);
        }
        finally
        {
            _connecting = false;
        }
    }

    /// <summary>
    /// En modo Local no necesita dirección.
    /// En modo Steam recibe el SteamID del host.
    /// </summary>
    public void StartClient(string address = null)
    {
        if (_connecting || Net == null || Net.IsListening)
            return;

        _connecting = true;

        try
        {
            if (!ConfigureTransport())
                return;

            if (networkMode == NetworkMode.Steam)
            {
                string target = (address ?? "").Trim();

                if (!ulong.TryParse(target, out ulong hostSteamId))
                {
                    Fail("Ingresá un SteamID válido.");
                    return;
                }

                // Debe configurarse antes de StartClient().
                _steamTransport.SetRemoteSteamId(hostSteamId);

                Debug.Log(
                    $"[SessionManager] Conectando al SteamID: {hostSteamId}"
                );
            }

            if (!Net.StartClient())
            {
                Fail("No se pudo iniciar el Client.");
                return;
            }

            Debug.Log("[SessionManager] Client iniciado correctamente.");
        }
        catch (Exception e)
        {
            Fail("Error al iniciar el Client.", e);
        }
        finally
        {
            _connecting = false;
        }
    }

    public void StartGame()
    {
        if (Net == null || !Net.IsServer || _state != SessionState.Lobby)
            return;

        _state = SessionState.InGame;
        _sceneReady = false;

        Net.SceneManager.LoadScene(
            gameScene,
            LoadSceneMode.Single
        );
    }

    /// <summary>
    /// Devuelve a todos los clientes conectados al lobby.
    /// </summary>
    public void ServerReturnToLobby()
    {
        if (Net == null ||
            !Net.IsServer ||
            _state != SessionState.InGame)
            return;

        _state = SessionState.Lobby;
        _sceneReady = false;

        Net.SceneManager.LoadScene(
            lobbyScene,
            LoadSceneMode.Single
        );
    }

    public void SetLocalPlayerName(string playerName)
    {
        LocalPlayerName = (playerName ?? string.Empty).Trim();

        if (LocalPlayerName.Length > 24)
            LocalPlayerName = LocalPlayerName.Substring(0, 24);

        PlayerPrefs.SetString(
            "Chessters.PlayerName",
            LocalPlayerName
        );

        PlayerPrefs.Save();
    }

    private void Fail(string message, Exception e = null)
    {
        JoinCode = "";

        if (e != null)
            Debug.LogException(e);

        Debug.LogWarning($"[SessionManager] {message}");

        OnConnectionError?.Invoke(message);
    }

    // =========================================================
    // SERVER
    // =========================================================

    private void OnServerStarted()
    {
        _state = SessionState.Lobby;
        _sceneReady = false;

        Net.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
        Net.SceneManager.OnSynchronizeComplete += OnClientSynchronized;

        PlayerSoul.OnServerSoulSpawned += OnSoulSpawned;

        Net.SceneManager.LoadScene(
            lobbyScene,
            LoadSceneMode.Single
        );
    }

    private void OnServerStopped(bool shutdown)
    {
        _state = SessionState.Menu;
        _sceneReady = false;
        JoinCode = "";

        PlayerSoul.OnServerSoulSpawned -= OnSoulSpawned;

        if (Net != null && Net.SceneManager != null)
        {
            Net.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
            Net.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
        }

        SceneManager.LoadScene(menuScene);
    }

    private void OnConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        bool hasRoom = Net.ConnectedClientsIds.Count < maxPlayers;

        bool open =
            _state == SessionState.Menu ||
            _state == SessionState.Lobby;

        response.Approved = open && hasRoom;

        // NGO crea el ALMA mediante el Default Player Prefab.
        response.CreatePlayerObject = true;

        Debug.Log(
            $"[Approval] cliente={request.ClientNetworkId} " +
            $"estado={_state} " +
            $"prefabDelAlma={(Net.NetworkConfig.PlayerPrefab != null)}"
        );

        if (!response.Approved)
        {
            response.Reason = !open
                ? "La partida ya empezó"
                : "Sala llena";
        }
    }

    // =========================================================
    // CARGA DE ESCENAS Y SPAWN
    // =========================================================

    private void OnSceneLoadCompleted(
        string scene,
        LoadSceneMode mode,
        List<ulong> completed,
        List<ulong> timedOut)
    {
        _sceneReady = true;

        foreach (ulong id in completed)
            SpawnBody(id);
    }

    private void OnSoulSpawned(PlayerSoul soul)
    {
        if (!_sceneReady)
            return;

        if (soul.OwnerClientId != NetworkManager.ServerClientId)
            return;

        StartCoroutine(
            SpawnBodyNextFrame(soul.OwnerClientId)
        );
    }

    private IEnumerator SpawnBodyNextFrame(ulong clientId)
    {
        yield return null;

        if (Net != null && Net.IsServer)
            SpawnBody(clientId);
    }

    private void OnClientSynchronized(ulong clientId)
    {
        SpawnBody(clientId);
    }

    private void SpawnBody(ulong clientId)
    {
        if (bodyPrefab == null)
        {
            Debug.LogError(
                "[SessionManager] El Body Prefab no está asignado."
            );
            return;
        }

        PlayerSoul soul = PlayerSoul.GetFor(clientId);

        if (soul == null)
        {
            Debug.Log(
                $"[SessionManager] El cliente {clientId} todavía no tiene alma."
            );
            return;
        }

        // Evita crear cuerpos duplicados.
        if (soul.HasBody)
            return;

        Vector3 pos = GetSpawnPosition(clientId);

        NetworkObject body = Instantiate(
            bodyPrefab,
            pos,
            Quaternion.identity
        );

        body.SpawnWithOwnership(
            clientId,
            true
        );

        soul.ServerSetBody(body);
    }

    private Vector3 GetSpawnPosition(ulong clientId)
    {
        if (SceneManager.GetActiveScene().name == lobbyScene)
        {
            LobbyDoor door = FindFirstObjectByType<LobbyDoor>();

            if (door != null &&
                door.TryGetSpawnTransform(clientId, out Transform lobbySpawn))
            {
                return lobbySpawn.position;
            }
        }

        SpawnPoint[] points = FindObjectsByType<SpawnPoint>(
            FindObjectsSortMode.InstanceID
        );

        if (points.Length == 0)
            return Vector3.zero;

        int index = (int)(clientId % (ulong)points.Length);

        return points[index].transform.position;
    }

    // =========================================================
    // CLIENT
    // =========================================================

    private void OnClientDisconnected(ulong clientId)
    {
        // Si soy servidor, no necesito tratarlo como una desconexión propia.
        if (Net == null || Net.IsServer)
            return;

        Debug.Log(
            $"[SessionManager] Desconectado: {Net.DisconnectReason}"
        );

        JoinCode = "";

        SceneManager.LoadScene(menuScene);
    }
}

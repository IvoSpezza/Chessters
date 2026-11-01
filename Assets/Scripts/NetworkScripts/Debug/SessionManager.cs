using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
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

    [Header("Red")]
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private int maxPlayers = 6;

    [Header("Relay")]
    [Tooltip("Si está tildado se usa Unity Relay con join code. Si no, conexión directa por IP.")]
    [SerializeField] private bool useRelay = true;
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

    /// <summary>Join code de la sesion actual (vacio si se usa IP directa o no hay sesion).</summary>
    public string JoinCode { get; private set; } = "";

    /// <summary>Se dispara cuando falla el inicio de host o cliente. La UI muestra el mensaje.</summary>
    public event Action<string> OnConnectionError;

    private NetworkManager Net => NetworkManager.Singleton;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        _connectionType = _connectionTypeEnum.Equals(Cifration.dtls) ? "dtls" : "udp";
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
    }

    // ---------- API para la UI ----------
    public async void StartHost(string address = null)
    {
        if (_connecting || Net.IsListening) return;
        _connecting = true;

        try
        {
            if (useRelay)
            {
                await InitServicesAsync();

                // El host no cuenta como conexion de Relay: maxPlayers - 1
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var utp = Net.GetComponent<UnityTransport>();
                utp.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, _connectionType));
                Debug.Log($"[Relay] Host listo. Join code: {JoinCode}");
            }
            else
            {
                SetConnectionData(address);
            }

            if (!Net.StartHost()) Fail("No se pudo iniciar el host.");
        }
        catch (Exception e)
        {
            Fail("No se pudo crear la sala. Revisá tu conexión a internet.", e);
        }
        finally
        {
            _connecting = false;
        }
    }

    /// <summary>Con Relay, "address" es el join code. Sin Relay, es la IP.</summary>
    public async void StartClient(string address)
    {
        if (_connecting || Net.IsListening) return;
        _connecting = true;

        try
        {
            if (useRelay)
            {
                string code = (address ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(code))
                {
                    Fail("Ingresá el código de la sala.");
                    return;
                }

                await InitServicesAsync();

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);
                var utp = Net.GetComponent<UnityTransport>();
                utp.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, _connectionType));
                JoinCode = code;
                Debug.Log($"[Relay] Unido con el código {code}");
            }
            else
            {
                SetConnectionData(address);
            }

            if (!Net.StartClient()) Fail("No se pudo iniciar el cliente.");
        }
        catch (RelayServiceException e)
        {
            Fail("Código inválido o sala no disponible.", e);
        }
        catch (Exception e)
        {
            Fail("No se pudo conectar. Revisá tu conexión a internet.", e);
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
        Net.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);
    }

    private void SetConnectionData(string address)
    {
        var utp = Net.GetComponent<UnityTransport>();
        utp.SetConnectionData(string.IsNullOrWhiteSpace(address) ? ipAddress : address, port);
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

    private void OnServerStopped(bool _)
    {
        _state = SessionState.Menu;
        _sceneReady = false;
        JoinCode = "";
        PlayerSoul.OnServerSoulSpawned -= OnSoulSpawned;
        if (Net.SceneManager != null)
        {
            Net.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
            Net.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
        }
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
        SceneManager.LoadScene(menuScene);
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

public class SessionManager : MonoBehaviour
{
    public static SessionManager instance { get; private set; }

    [Header("Red")]
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private int maxPlayers = 6;

    [Header("Escenas")]
    [SerializeField] private string menuScene = "MainMenu";
    [SerializeField] private string lobbyScene = "Lobby";
    [SerializeField] private string gameScene = "Game";

    [Header("Cuerpo")]
    [Tooltip("Prefab del CUERPO del jugador. El alma es el Default Player Prefab del NetworkManager.")]    
    [SerializeField] private NetworkObject bodyPrefab;

    private enum SessionState { Menu, Lobby, InGame }
    private SessionState _state = SessionState.Menu;
    private bool _sceneReady;   // true cuando la escena actual ya termino de cargar en el servidor

    private NetworkManager Net => NetworkManager.Singleton;

    private void Awake()
    {
       
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
   

    private void Start()
    {
        var cfg = Net.NetworkConfig;
        
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
    public void StartHost(string address = null)
    {
        var cfg = Net.NetworkConfig;        
        SetConnectionData(address);
        Net.StartHost();
    }

    public void StartClient(string address)
    {
        SetConnectionData(address);
        Net.StartClient();
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
        SceneManager.LoadScene(menuScene);
    }
}
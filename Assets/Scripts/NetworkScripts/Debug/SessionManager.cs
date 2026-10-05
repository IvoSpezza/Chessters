using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
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

    [Header("Player")]
    [SerializeField] private NetworkObject playerPrefab;

    private enum SessionState { Menu, Lobby, InGame }
    private SessionState _state = SessionState.Menu;

    private NetworkManager Net => NetworkManager.Singleton;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
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
    public void StartHost(string address = null)
    {
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

        Net.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;       // host / cambios de escena
        Net.SceneManager.OnSynchronizeComplete += OnClientSynchronized;      // clientes que entran tarde

        Net.SceneManager.LoadScene(lobbyScene, LoadSceneMode.Single);
    }

    private void OnServerStopped(bool _)
    {
        _state = SessionState.Menu;
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
        response.CreatePlayerObject = false;            // el spawn es manual
        if (!response.Approved)
            response.Reason = !open ? "La partida ya empezó" : "Sala llena";
    }

    private void OnSceneLoadCompleted(string scene, LoadSceneMode mode,
                                      List<ulong> completed, List<ulong> timedOut)
    {
        foreach (ulong id in completed) SpawnPlayer(id);
    }

    private void OnClientSynchronized(ulong clientId) => SpawnPlayer(clientId);

    private void SpawnPlayer(ulong clientId)
    {
        // Evita duplicados si ambos eventos disparan para el mismo cliente
        if (Net.SpawnManager.GetPlayerNetworkObject(clientId) != null) return;

        Vector3 pos = GetSpawnPosition(clientId);
        NetworkObject player = Instantiate(playerPrefab, pos, Quaternion.identity);
        player.SpawnAsPlayerObject(clientId, true); // true = se destruye con la escena
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

// Componente trivial: ponelo en objetos vacíos de cada escena (Lobby y Game)
public class SpawnPoint : MonoBehaviour { }
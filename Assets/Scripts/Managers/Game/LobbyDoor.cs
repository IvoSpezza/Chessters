using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puerta grande del lobby. Objeto de la escena Lobby con NetworkObject y un Collider en modo trigger
/// (en el MISMO GameObject que este script).
/// Un jugador dentro de la zona ve el boton Ready. Si sale de la zona, su Ready se cancela.
/// Cuando todos los conectados estan listos, el servidor llama a SessionManager.StartGame().
/// </summary>
[RequireComponent(typeof(Collider))]
public class LobbyDoor : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private Button _readyButton;
    [SerializeField] private TMP_Text _readyCountText;

    [SerializeField] private TMP_Text _codeText;
    [SerializeField] private Button _startGameButton;


    [Tooltip("Opcional: botón que copia el código al portapapeles.")]
    [SerializeField] private Button _copyButton;   

    [Header("Aparición de jugadores")]
    [Tooltip("Asigná aquí los puntos de aparición de la escena Lobby.")]
    [SerializeField] private Transform[] _spawnPoints;
    

    private NetworkList<ulong> _ready;

    // Solo servidor: quienes estan dentro de la zona segun los triggers
    private readonly HashSet<ulong> _inside = new HashSet<ulong>();
    private readonly List<ulong> _toRemove = new List<ulong>();

    // Solo UI local
    private bool _localInside;
    private bool _started;

    private void Awake()
    {
        _ready = new NetworkList<ulong>(null, NetworkVariableReadPermission.Everyone);
        
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogWarning("[LobbyDoor] El collider de la puerta debe estar en modo Is Trigger.", this);
    }

    private void Start()
    {
        if (_spawnPoints == null || _spawnPoints.Length == 0)
            Debug.LogWarning("[LobbyDoor] Asigná al menos un punto de aparición para los jugadores del Lobby.", this);
        else if (SessionManager.instance != null && _spawnPoints.Length < SessionManager.instance.MaxPlayers)
            Debug.LogWarning("[LobbyDoor] Hay menos puntos de aparición que plazas; algunos jugadores compartirán posición.", this);

        string code = SessionManager.instance != null ? SessionManager.instance.JoinCode : "";
        if (_codeText != null) _codeText.text = string.IsNullOrEmpty(code) ? "" : $"Código: {code}";

        if (_copyButton != null)
        {
            _copyButton.gameObject.SetActive(!string.IsNullOrEmpty(code));
            _copyButton.onClick.AddListener(CopyCode);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (_startGameButton != null) _startGameButton.gameObject.SetActive(false);
        _ready.OnListChanged += HandleReadyChanged;
        if (_readyButton != null) _readyButton.onClick.AddListener(OnReadyClicked);

        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += HandleClientListChanged;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        _started = false;
        _localInside = false;
        RefreshButton();
        RefreshText();
    }

    public override void OnNetworkDespawn()
    {
        _ready.OnListChanged -= HandleReadyChanged;
        if (_readyButton != null) _readyButton.onClick.RemoveListener(OnReadyClicked);
        if (NetworkManager != null && IsServer)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientListChanged;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleReadyChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshButton();
        RefreshText();
        RefreshStartButton();
    }

    private void HandleClientListChanged(ulong _) => RefreshStartButton();

    private void HandleClientDisconnected(ulong clientId)
    {
        _inside.Remove(clientId);
        _ready.Remove(clientId);
        RefreshText();
        RefreshStartButton();
    }

    // ---------------------------------------------------------------- Triggers

    private void OnTriggerEnter(Collider other)
    {
        if (!IsSpawned || !TryGetPlayerBody(other, out NetworkObject body)) return;

        if (IsServer)
        {
            _inside.Add(body.OwnerClientId);            
        }

        if (body.IsOwner)
        {
            _localInside = true;
            RefreshButton();            
        }
        RefreshText();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsSpawned || !TryGetPlayerBody(other, out NetworkObject body)) return;

        if (IsServer)
        {
            _inside.Remove(body.OwnerClientId);
            _ready.Remove(body.OwnerClientId);            
            RefreshStartButton();
        }

        if (body.IsOwner)
        {
            _localInside = false;
            RefreshButton();            
        }
        RefreshText();
    }

    private static bool TryGetPlayerBody(Collider other, out NetworkObject body)
    {
        body = other.GetComponentInParent<NetworkObject>();
        return body != null && body.TryGetComponent(out PlayerMovement _);
    }

    // ---------------------------------------------------------------- Cliente

    private void OnReadyClicked()
    {
        RefreshText();
        SetReadyRpc();
    }

    private void RefreshButton()
    {
        if (_readyButton == null) return;

        bool show = _localInside && !_ready.Contains(NetworkManager.LocalClientId);
        if (_readyButton.gameObject.activeSelf != show) _readyButton.gameObject.SetActive(show);
    }

    private void RefreshText()
    {
        if (_readyCountText != null)
        {
            int connectedPlayers = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            _readyCountText.text = $"Listos {_ready.Count}/{connectedPlayers}";
        }
    }

    // ---------------------------------------------------------------- Servidor

    [Rpc(SendTo.Server)]
    private void SetReadyRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // Solo vale si el servidor ya vio a ese jugador dentro de la zona
        if (!_inside.Contains(sender)) return;
        if (!_ready.Contains(sender)) _ready.Add(sender);
        RefreshStartButton();
    }      

    public void StartGame()
    {
        if (!IsServer || _started) return;
        CleanUp();
        if (!CanStartGame()) return;
        _started = true;
        SessionManager.instance.StartGame();
    }

    private bool CanStartGame()
    {
        int playerCount = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
        int minPlayers = SessionManager.instance != null ? SessionManager.instance.MinPlayers : 2;
        if (playerCount < minPlayers || _ready.Count != playerCount) return false;

        foreach (ulong readyPlayer in _ready)
            if (!_inside.Contains(readyPlayer)) return false;
        return true;
    }

    private void RefreshStartButton()
    {
        if (_startGameButton == null) return;
        bool canStart = IsServer && !_started && CanStartGame();
        if (_startGameButton.gameObject.activeSelf != canStart)
            _startGameButton.gameObject.SetActive(canStart);
    }

    public bool TryGetSpawnTransform(ulong clientId, out Transform spawnPoint)
    {
        spawnPoint = null;
        if (_spawnPoints == null || _spawnPoints.Length == 0) return false;
        int index = (int)(clientId % (ulong)_spawnPoints.Length);
        spawnPoint = _spawnPoints[index];
        return spawnPoint != null;
    }

    /// <summary>Unity no garantiza OnTriggerExit si el cuerpo se destruye o el cliente se desconecta dentro de la zona.</summary>
    private void CleanUp()
    {
        _toRemove.Clear();
        foreach (ulong id in _inside)
        {
            PlayerSoul soul = PlayerSoul.GetFor(id);
            if (soul == null || !soul.HasBody) _toRemove.Add(id);
        }
        foreach (ulong id in _toRemove) _inside.Remove(id);

        for (int i = _ready.Count - 1; i >= 0; i--)
            if (!_inside.Contains(_ready[i])) _ready.RemoveAt(i);
    }


    private void OnDestroy()
    {
        if (_copyButton != null) _copyButton.onClick.RemoveListener(CopyCode);
    }

    private void CopyCode()
    {
        GUIUtility.systemCopyBuffer = SessionManager.instance.JoinCode;
    }
}

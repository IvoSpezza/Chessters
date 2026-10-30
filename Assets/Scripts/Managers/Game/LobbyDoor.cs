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

    [Header("[TEMP: inspector]")]
    [Tooltip("Minimo de jugadores para iniciar. Pasara a un SO de config editable antes de hostear")]
    [SerializeField, Min(1)] private int _minPlayers = 1;
    [SerializeField] private bool _debugLogs;

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

    public override void OnNetworkSpawn()
    {
        _ready.OnListChanged += HandleReadyChanged;
        if (_readyButton != null) _readyButton.onClick.AddListener(OnReadyClicked);

        _started = false;
        _localInside = false;
        RefreshButton();
        RefreshText();
    }

    public override void OnNetworkDespawn()
    {
        _ready.OnListChanged -= HandleReadyChanged;
        if (_readyButton != null) _readyButton.onClick.RemoveListener(OnReadyClicked);
    }

    private void HandleReadyChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshButton();
        RefreshText();
    }

    // ---------------------------------------------------------------- Triggers

    private void OnTriggerEnter(Collider other)
    {
        if (!IsSpawned || !TryGetPlayerBody(other, out NetworkObject body)) return;

        if (IsServer)
        {
            _inside.Add(body.OwnerClientId);
            if (_debugLogs) Debug.Log($"[LobbyDoor] SERVIDOR: entro el cliente {body.OwnerClientId}");
        }

        if (body.IsOwner)
        {
            _localInside = true;
            RefreshButton();
            if (_debugLogs) Debug.Log("[LobbyDoor] LOCAL: entre a la zona");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsSpawned || !TryGetPlayerBody(other, out NetworkObject body)) return;

        if (IsServer)
        {
            _inside.Remove(body.OwnerClientId);
            _ready.Remove(body.OwnerClientId);
            if (_debugLogs) Debug.Log($"[LobbyDoor] SERVIDOR: salio el cliente {body.OwnerClientId}");
        }

        if (body.IsOwner)
        {
            _localInside = false;
            RefreshButton();
            if (_debugLogs) Debug.Log("[LobbyDoor] LOCAL: sali de la zona");
        }
    }

    private static bool TryGetPlayerBody(Collider other, out NetworkObject body)
    {
        body = other.GetComponentInParent<NetworkObject>();
        return body != null && body.TryGetComponent(out PlayerMovement _);
    }

    // ---------------------------------------------------------------- Cliente

    private void OnReadyClicked() => SetReadyRpc();

    private void RefreshButton()
    {
        if (_readyButton == null) return;

        bool show = _localInside && !_ready.Contains(NetworkManager.LocalClientId);
        if (_readyButton.gameObject.activeSelf != show) _readyButton.gameObject.SetActive(show);
    }

    private void RefreshText()
    {
        if (_readyCountText != null)
            _readyCountText.text = $"Listos {_ready.Count}/{PlayerSoul.All.Count}";
    }

    // ---------------------------------------------------------------- Servidor

    [Rpc(SendTo.Server)]
    private void SetReadyRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // Solo vale si el servidor ya vio a ese jugador dentro de la zona
        if (!_inside.Contains(sender)) return;
        if (!_ready.Contains(sender)) _ready.Add(sender);
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        CleanUp();

        if (_started) return;

        int total = PlayerSoul.All.Count;
        if (total < _minPlayers || _ready.Count != total) return;

        _started = true;
        // TODO: animacion de apertura de la puerta antes de cargar la escena
        SessionManager.instance.StartGame();
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
}
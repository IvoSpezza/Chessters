using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// El Alma representa la conexion del jugador. Es su PlayerObject: se crea al conectarse
/// y persiste entre escenas. Guarda el progreso (nivel, vivo/muerto, nombre) y la referencia
/// al cuerpo, que es otro NetworkObject con el mismo dueño (movimiento, stats, vida, inventario).
///
/// Solo el servidor escribe. Los datos se replican a todos con NetworkVariables.
/// </summary>
public class PlayerSoul : NetworkBehaviour
{
    /// <summary>Todas las almas spawneadas (vivas o muertas). Util para turnos, niveles y chat.</summary>
    public static readonly List<PlayerSoul> All = new List<PlayerSoul>();

    /// <summary>El alma del jugador local (null hasta que spawnea).</summary>
    public static PlayerSoul Local { get; private set; }

    /// <summary>Solo servidor: se dispara cuando un alma termina de spawnear.</summary>
    public static event Action<PlayerSoul> OnServerSoulSpawned;
    public static event Action<string> OnChatMessageReceived;

    private readonly NetworkVariable<NetworkObjectReference> _body = new NetworkVariable<NetworkObjectReference>();
    private readonly NetworkVariable<bool> _isAlive = new NetworkVariable<bool>(true);
    private readonly NetworkVariable<int> _level = new NetworkVariable<int>(1);
    private readonly NetworkVariable<FixedString32Bytes> _playerName = new NetworkVariable<FixedString32Bytes>();

    public bool IsAlive => _isAlive.Value;
    public int Level => _level.Value;
    public string PlayerName => _playerName.Value.ToString();

    public event Action<NetworkObject> OnBodyChanged;   // null si el cuerpo ya no existe
    public event Action<bool> OnAliveChanged;
    public event Action<int> OnLevelChanged;
    public event Action<string> OnNameChanged;

    // ---------------------------------------------------------------- Ciclo de vida

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (IsOwner) Local = this;

        _body.OnValueChanged += HandleBodyChanged;
        _isAlive.OnValueChanged += HandleAliveChanged;
        _level.OnValueChanged += HandleLevelChanged;
        _playerName.OnValueChanged += HandleNameChanged;

        if (IsServer)
        {
            ServerSetName(null);
            _isAlive.Value = true;
            _level.Value = 1;

            OnServerSoulSpawned?.Invoke(this);
        }

        if (IsOwner)
        {
            string requestedName = SessionManager.instance != null ? SessionManager.instance.LocalPlayerName : null;
            if (IsServer) ServerSetName(requestedName);
            else SetPlayerNameRpc(requestedName);
        }

        SyncBodyStatsLevel();
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (Local == this) Local = null;

        _body.OnValueChanged -= HandleBodyChanged;
        _isAlive.OnValueChanged -= HandleAliveChanged;
        _level.OnValueChanged -= HandleLevelChanged;
        _playerName.OnValueChanged -= HandleNameChanged;
    }

    private void HandleBodyChanged(NetworkObjectReference previous, NetworkObjectReference current)
    {
        current.TryGet(out NetworkObject body);
        SyncBodyStatsLevel(body);
        OnBodyChanged?.Invoke(body);
    }

    private void HandleAliveChanged(bool previous, bool current) => OnAliveChanged?.Invoke(current);
    private void HandleLevelChanged(int previous, int current)
    {
        SyncBodyStatsLevel();
        OnLevelChanged?.Invoke(current);
    }
    private void HandleNameChanged(FixedString32Bytes previous, FixedString32Bytes current) => OnNameChanged?.Invoke(current.ToString());

    // ---------------------------------------------------------------- Cuerpo

    /// <summary>True si el cuerpo existe. Si se destruyo (cambio de escena) devuelve false.</summary>
    public bool HasBody => _body.Value.TryGet(out NetworkObject _);

    public bool TryGetBody(out NetworkObject body) => _body.Value.TryGet(out body);

    public bool TryGetBodyComponent<T>(out T component) where T : Component
    {
        component = null;
        return _body.Value.TryGet(out NetworkObject body) && body.TryGetComponent(out component);
    }

    /// <summary>Busca el alma de un cliente (null si no existe).</summary>
    public static PlayerSoul GetFor(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null) return null;

        // GetPlayerNetworkObject is server-only for remote clients. Clients already
        // have their locally spawned PlayerSoul instances in All, so resolve there.
        if (!manager.IsServer)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].OwnerClientId == clientId)
                    return All[i];
            return null;
        }

        NetworkObject obj = manager.SpawnManager.GetPlayerNetworkObject(clientId);
        return obj != null ? obj.GetComponent<PlayerSoul>() : null;
    }

    // ---------------------------------------------------------------- Solo servidor

    public void ServerSetBody(NetworkObject body)
    {
        if (!IsServer) return;
        _body.Value = new NetworkObjectReference(body);
        SyncBodyStatsLevel(body);
    }

    public void ServerClearBody()
    {
        if (!IsServer) return;
        _body.Value = default;
    }

    public void ServerSetAlive(bool alive)
    {
        if (!IsServer) return;
        _isAlive.Value = alive;
    }

    public void ServerSetLevel(int level)
    {
        if (!IsServer) return;
        _level.Value = Mathf.Max(1, level);
        SyncBodyStatsLevel();
    }

    private void SyncBodyStatsLevel(NetworkObject body = null)
    {
        if (body == null && !_body.Value.TryGet(out body)) return;
        if (body != null && body.TryGetComponent(out Stats stats)) stats.SetLevel(_level.Value);
    }

    public void ServerSetName(string playerName)
    {
        if (!IsServer) return;

        string normalizedName = (playerName ?? string.Empty).Trim();
        normalizedName = normalizedName.Replace("\n", string.Empty).Replace("\r", string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedName)) normalizedName = $"Jugador {OwnerClientId + 1}";

        // FixedString32Bytes reserva 29 bytes UTF-8 para contenido.
        while (normalizedName.Length > 0 && Encoding.UTF8.GetByteCount(normalizedName) > 29)
        {
            int trimIndex = normalizedName.Length - 1;
            if (char.IsLowSurrogate(normalizedName[trimIndex]) && trimIndex > 0 && char.IsHighSurrogate(normalizedName[trimIndex - 1]))
                trimIndex--;
            normalizedName = normalizedName.Substring(0, trimIndex);
        }

        _playerName.Value = normalizedName;
    }

    [Rpc(SendTo.Server)]
    private void SetPlayerNameRpc(string playerName, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        ServerSetName(playerName);
    }

    /// <summary>Envía un mensaje desde el jugador propietario al servidor.</summary>
    public void SendChatMessage(string message)
    {
        if (!IsSpawned || string.IsNullOrWhiteSpace(message)) return;
        string cleanMessage = message.Trim();
        if (cleanMessage.Length > 200) cleanMessage = cleanMessage.Substring(0, 200);

        if (IsServer) ServerBroadcastChatMessage(cleanMessage);
        else SendChatMessageRpc(cleanMessage);
    }

    [Rpc(SendTo.Server)]
    private void SendChatMessageRpc(string message, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsServer) return;
        if (string.IsNullOrWhiteSpace(message)) return;

        string cleanMessage = message.Trim();
        if (cleanMessage.Length > 200) cleanMessage = cleanMessage.Substring(0, 200);
        ServerBroadcastChatMessage(cleanMessage);
    }

    private void ServerBroadcastChatMessage(string message)
    {
        if (!IsServer) return;
        string safeName = PlayerName;
        if (string.IsNullOrWhiteSpace(safeName)) safeName = $"Jugador {OwnerClientId + 1}";
        string formattedMessage = $"{safeName}: {message}";

        // Emitir desde el PlayerSoul del host asegura que el servidor sea el emisor
        // autorizado del RPC de difusión, aunque el mensaje haya llegado desde un cliente.
        PlayerSoul hostSoul = GetFor(NetworkManager.ServerClientId);
        if (hostSoul != null) hostSoul.BroadcastChatMessageRpc(formattedMessage);
        else BroadcastChatMessageRpc(formattedMessage);
    }

    [Rpc(SendTo.Everyone)]
    private void BroadcastChatMessageRpc(string formattedMessage)
    {
        OnChatMessageReceived?.Invoke(formattedMessage);
    }
}

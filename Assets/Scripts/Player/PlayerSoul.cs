using System;
using System.Collections.Generic;
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
            _playerName.Value = $"Jugador {OwnerClientId + 1}";
            _isAlive.Value = true;
            _level.Value = 1;

            OnServerSoulSpawned?.Invoke(this);
        }
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
        OnBodyChanged?.Invoke(body);
    }

    private void HandleAliveChanged(bool previous, bool current) => OnAliveChanged?.Invoke(current);
    private void HandleLevelChanged(int previous, int current) => OnLevelChanged?.Invoke(current);
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
        NetworkObject obj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        return obj != null ? obj.GetComponent<PlayerSoul>() : null;
    }

    // ---------------------------------------------------------------- Solo servidor

    public void ServerSetBody(NetworkObject body)
    {
        if (!IsServer) return;
        _body.Value = new NetworkObjectReference(body);
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
    }

    public void ServerSetName(string playerName)
    {
        if (!IsServer) return;
        _playerName.Value = playerName;   // se recorta a 32 bytes
    }
}
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Maldiciones activas del jugador. Va en el CUERPO, junto a PlayerInventory (usa su ItemCatalog).
/// Guarda IDs de CurseData (visibles para todos). Todos los peers recalculan los modifiers de PlayerStats
/// con una sola fuente (StatSource.Curse), igual que el equipo.
/// Son permanentes: solo las quita un efecto limpiador o morir. Solo el servidor escribe.
/// </summary>
[RequireComponent(typeof(Stats), typeof(PlayerInventory))]
public class PlayerCurses : NetworkBehaviour
{
    private NetworkList<int> _curses;

    private PlayerInventory _inventory;
    private Stats _stats;
    private bool _dirty;
    private readonly List<StatModifier> _modifierBuffer = new List<StatModifier>();

    /// <summary>Se dispara (una vez por frame como maximo) cuando cambian las maldiciones.</summary>
    public event Action OnCursesChanged;

    private void Awake()
    {
        _curses = new NetworkList<int>(null, NetworkVariableReadPermission.Everyone);
    }

    public override void OnNetworkSpawn()
    {
        _inventory = GetComponent<PlayerInventory>();
        _stats = GetComponent<Stats>();

        _curses.OnListChanged += HandleCursesChanged;

        // OnListChanged no se dispara con el estado inicial (clientes que entran tarde)
        _dirty = true;
    }

    public override void OnNetworkDespawn()
    {
        _curses.OnListChanged -= HandleCursesChanged;
    }

    private void HandleCursesChanged(NetworkListEvent<int> changeEvent) => _dirty = true;

    private void LateUpdate()
    {
        if (!_dirty || !IsSpawned) return;
        _dirty = false;

        RefreshModifiers();
        OnCursesChanged?.Invoke();
    }

    private void RefreshModifiers()
    {
        _modifierBuffer.Clear();

        for (int i = 0; i < _curses.Count; i++)
        {
            if (!_inventory.Catalog.TryGetItem(_curses[i], out CurseData curse)) continue;
            foreach (StatModifier modifier in curse.modifiers) _modifierBuffer.Add(modifier);
        }

        _stats.SetModifiersFromSource(StatSource.Curse, _modifierBuffer);
    }

    // ---------------------------------------------------------------- Lectura (cualquier peer)

    public bool HasCurse(int curseId) => _curses.Contains(curseId);

    public List<CurseData> GetCurses()
    {
        var result = new List<CurseData>(_curses.Count);
        for (int i = 0; i < _curses.Count; i++)
            if (_inventory.Catalog.TryGetItem(_curses[i], out CurseData curse)) result.Add(curse);
        return result;
    }
     

    // ---------------------------------------------------------------- Solo servidor

    /// <summary>Devuelve false si ya la tenia (no se acumulan repetidas).</summary>
    public bool ServerAddCurse(CurseData curse)
    {
        if (!IsServer || curse == null || _curses.Contains(curse.itemID)) return false;

        _curses.Add(curse.itemID);
        return true;
    }

    public bool ServerRemoveCurse(int curseId) => IsServer && _curses.Remove(curseId);

    /// <summary>Para efectos limpiadores y para la muerte.</summary>
    public void ServerClearAll()
    {
        if (!IsServer) return;
        _curses.Clear();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catalogo de salas y combates. Resuelve ID -> SO (lo unico que viaja por red).
/// Arrastrar aca todas las salas y todos los combates del juego.
/// </summary>
[CreateAssetMenu(fileName = "RoomCatalog", menuName = "New Rooms/Room Catalog")]
public class RoomCatalog : ScriptableObject
{
    public const int EmptyID = -1;

    [SerializeField] private List<RoomData> _rooms = new List<RoomData>();
    [SerializeField] private List<BaseCombatData> _combats = new List<BaseCombatData>();

    private Dictionary<int, RoomData> _roomLookup;
    private Dictionary<int, BaseCombatData> _combatLookup;

    private void OnEnable()
    {
        _roomLookup = null;
        _combatLookup = null;
    }

    public bool TryGetRoom(int id, out RoomData room)
    {
        if (_roomLookup == null) _roomLookup = BuildLookup(_rooms, r => r.roomID);
        return _roomLookup.TryGetValue(id, out room);
    }

    public bool TryGetCombat(int id, out BaseCombatData combat)
    {
        if (_combatLookup == null) _combatLookup = BuildLookup(_combats, c => c.combatID);
        return _combatLookup.TryGetValue(id, out combat);
    }

    public RoomData GetRandomRoom(RoomType type)
    {
        var matching = new List<RoomData>();
        foreach (RoomData room in _rooms)
            if (room != null && room.type == type) matching.Add(room);

        return matching.Count > 0 ? matching[UnityEngine.Random.Range(0, matching.Count)] : null;
    }

    private Dictionary<int, T> BuildLookup<T>(List<T> source, Func<T, int> getId) where T : ScriptableObject
    {
        var lookup = new Dictionary<int, T>();
        foreach (T entry in source)
        {
            if (entry == null) continue;
            int id = getId(entry);
            if (id < 0) Debug.LogWarning($"[RoomCatalog] {entry.name} tiene ID negativo (-1 esta reservado)", this);
            else if (!lookup.TryAdd(id, entry)) Debug.LogError($"[RoomCatalog] ID duplicado {id}: {entry.name} y {lookup[id].name}", this);
        }
        return lookup;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _roomLookup = BuildLookup(_rooms, r => r.roomID);
        _combatLookup = BuildLookup(_combats, c => c.combatID);
    }
#endif
}

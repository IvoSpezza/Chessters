using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catalogo de salas y combates. Resuelve ID -> SO (lo unico que viaja por red).
/// Los combates se descubren desde las salas de combate para evitar registrarlos dos veces.
/// </summary>
[CreateAssetMenu(fileName = "RoomCatalog", menuName = "New Rooms/Room Catalog")]
public class RoomCatalog : ScriptableObject
{
    public const int EmptyID = -1;

    [SerializeField] private List<RoomData> _rooms = new List<RoomData>();

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
        if (_combatLookup == null) _combatLookup = BuildCombatLookup();
        return _combatLookup.TryGetValue(id, out combat);
    }

    /// <summary>Sala al azar entre todas (100% aleatorio, sin pesos).</summary>
    public RoomData GetRandomRoom()
    {
        var valid = new List<RoomData>();
        foreach (RoomData room in _rooms)
            if (room != null && room.roomPrefab != null) valid.Add(room);

        return valid.Count > 0 ? valid[UnityEngine.Random.Range(0, valid.Count)] : null;
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

    private Dictionary<int, BaseCombatData> BuildCombatLookup()
    {
        var lookup = new Dictionary<int, BaseCombatData>();
        foreach (RoomData room in _rooms)
        {
            if (!(room is CombatRoomData combatRoom)) continue;

            foreach (BaseCombatData combat in combatRoom.PossibleCombats)
            {
                if (combat == null) continue;
                int id = combat.combatID;
                if (id < 0)
                    Debug.LogWarning($"[RoomCatalog] {combat.name} tiene ID negativo (-1 esta reservado)", this);
                else if (lookup.TryGetValue(id, out BaseCombatData existing))
                {
                    // El mismo combate puede aparecer en varias salas; eso no es una colision de ID.
                    if (existing != combat)
                        Debug.LogError($"[RoomCatalog] ID de combate duplicado {id}: {combat.name} y {existing.name}", this);
                }
                else lookup.Add(id, combat);
            }
        }
        return lookup;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _roomLookup = BuildLookup(_rooms, r => r.roomID);
        _combatLookup = BuildCombatLookup();
    }
#endif
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catalogo de todos los items. Resuelve ID -> item (lo unico que viaja por red).
/// Arrastrar aca todos los items del juego y referenciar este SO desde PlayerInventory.
/// </summary>
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "New Items/Item Catalog")]
public class ItemCatalog : ScriptableObject
{
    public const int EmptyID = -1;

    [SerializeField] private List<ItemDataBase> _items = new List<ItemDataBase>();
    public IReadOnlyList<ItemDataBase> Items => _items;

    private Dictionary<int, ItemDataBase> _lookup;

    private void OnEnable() => _lookup = null;

    private void BuildLookup()
    {
        _lookup = new Dictionary<int, ItemDataBase>();
        foreach (ItemDataBase item in _items)
        {
            if (item == null) continue;
            if (!_lookup.TryAdd(item.itemID, item))
                Debug.LogError($"[ItemCatalog] ID duplicado {item.itemID}: {item.name} y {_lookup[item.itemID].name}", this);
        }
    }

    public bool TryGetItem(int id, out ItemDataBase item)
    {
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out item);
    }

    public bool TryGetItem<T>(int id, out T item) where T : ItemDataBase
    {
        item = null;
        if (!TryGetItem(id, out ItemDataBase baseItem)) return false;
        item = baseItem as T;
        return item != null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _lookup = null;
        var seen = new HashSet<int>();
        foreach (ItemDataBase item in _items)
        {
            if (item == null) continue;
            if (item.itemID < 0)
                Debug.LogWarning($"[ItemCatalog] {item.name} tiene ID negativo (-1 esta reservado para vacio)", this);
            else if (!seen.Add(item.itemID))
                Debug.LogError($"[ItemCatalog] ID duplicado {item.itemID} en {item.name}", this);
        }
    }
#endif
}

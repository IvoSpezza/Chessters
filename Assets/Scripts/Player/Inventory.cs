using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    private List<ItemDataBase> _inventory = new();

    public void AddItem(ItemDataBase item)
    {
        _inventory.Add(item);
    }
    public ItemDataBase RemoveItem(ItemDataBase item)
    {
        ItemDataBase itemRemovido = item;
        _inventory.Remove(item);
        return itemRemovido;
    }
    public IReadOnlyList<ItemDataBase> GetInventory() 
    {
        return _inventory;
    }
}

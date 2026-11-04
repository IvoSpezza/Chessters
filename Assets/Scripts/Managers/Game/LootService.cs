using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reparto de items aleatorios. Solo servidor.
/// Sin filtrar por tipo, salvo las maldiciones (salen de cofres y recompensas, no del loot comun).
/// TODO: pools por piso / rareza cuando existan.
/// </summary>
public static class LootService
{
    /// <summary>Misma cantidad para todos. No repite items entre jugadores mientras alcance el catalogo.</summary>
    public static void DealToAll(IReadOnlyList<PlayerInventory> inventories, int countPerPlayer, ItemCatalog catalog)
    {
        var pool = new List<int>();
        foreach (PlayerInventory inventory in inventories)
            GiveRandom(inventory, countPerPlayer, catalog, pool);
    }

    /// <summary>Para resurrecciones y cofres de tesoro.</summary>
    public static void GiveRandom(PlayerInventory inventory, int count, ItemCatalog catalog)
    {
        GiveRandom(inventory, count, catalog, new List<int>());
    }

    private static void GiveRandom(PlayerInventory inventory, int count, ItemCatalog catalog, List<int> pool)
    {
        if (catalog == null || catalog.ItemsAmount() == 0)
        {
            Debug.LogWarning("[LootService] Catalogo vacio o sin asignar.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (pool.Count == 0) Refill(pool, catalog);
            if (pool.Count == 0) return;

            int index = Random.Range(0, pool.Count);
            inventory.AddItem(pool[index]);
            pool.RemoveAt(index);
        }
    }

    private static void Refill(List<int> pool, ItemCatalog catalog)
    {
        foreach (ItemDataBase item in catalog.Weapons)
        {
            if (item == null || item.itemID < 0) continue;
            if (item is CurseData) continue;
            pool.Add(item.itemID);
        }
        foreach (ItemDataBase item in catalog.Accesoris)
        {
            if (item == null || item.itemID < 0) continue;
            if (item is CurseData) continue;
            pool.Add(item.itemID);
        }
        foreach (ItemDataBase item in catalog.Armor)
        {
            if (item == null || item.itemID < 0) continue;
            if (item is CurseData) continue;
            pool.Add(item.itemID);
        }
        foreach (ItemDataBase item in catalog.Consumables)
        {
            if (item == null || item.itemID < 0) continue;
            if (item is CurseData) continue;
            pool.Add(item.itemID);
        }
    }
}
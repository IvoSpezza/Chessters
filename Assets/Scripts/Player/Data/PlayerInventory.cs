using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Slots de equipo. Las manos son genericas: Hand1 se usa con un click, Hand2 con el otro.
/// </summary>
public enum EquipSlot
{
    Helmet = 0,
    Chest = 1,
    Legs = 2,
    Boots = 3,
    Hand1 = 4,
    Hand2 = 5
}

/// <summary>
/// Inventario del player en red. Va en el prefab del player, junto a PlayerStats y Health.
///
/// - Equipados (6 slots) y accesorios: visibles para todos.
/// - Mochila: visible solo para el dueño.
/// - Todo se guarda como IDs (int). El servidor es el unico que escribe.
/// - Todos los peers recalculan los modifiers de PlayerStats a partir de los IDs equipados.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerInventory : NetworkBehaviour
{
    public const int EmptyID = ItemCatalog.EmptyID;   // -1: slot vacio
    public const int TwoHandedOccupied = -2;          // Hand2 ocupada por un arma de dos manos que esta en Hand1
    private const int SlotCount = 6;

    [SerializeField] private ItemCatalog _catalog;

    [Header("DEBUG - IDs que recibe la mochila al spawnear (solo servidor)")]
    [SerializeField] private int[] _startingBackpackIds;

    private NetworkList<int> _equipped;     // indexado por EquipSlot
    private NetworkList<int> _accessories;
    private NetworkList<int> _backpack;     // lectura solo para el dueño

    private PlayerStats _stats;
    private bool _dirty;
    private readonly List<StatModifier> _modifierBuffer = new List<StatModifier>();

    /// <summary>Se dispara (una vez por frame como maximo) cuando cambia lo equipado o los accesorios.</summary>
    public event Action OnEquipmentChanged;
    /// <summary>Se dispara cuando cambia la mochila (solo en el dueño y el servidor).</summary>
    public event Action OnBackpackChanged;

    public int BackpackCount => _backpack.Count;
    public ItemCatalog Catalog => _catalog;
    // ---------------------------------------------------------------- Ciclo de vida

    private void Awake()
    {
        // Las NetworkList se crean en Awake (no con inicializador de campo)
        _equipped = new NetworkList<int>(null, NetworkVariableReadPermission.Everyone);
        _accessories = new NetworkList<int>(null, NetworkVariableReadPermission.Everyone);
        _backpack = new NetworkList<int>(null, NetworkVariableReadPermission.Owner);
    }

    public override void OnNetworkSpawn()
    {
        _stats = GetComponent<PlayerStats>();

        _equipped.OnListChanged += HandleEquipmentListChanged;
        _accessories.OnListChanged += HandleEquipmentListChanged;
        _backpack.OnListChanged += HandleBackpackListChanged;

        if (IsServer)
        {
            if (_equipped.Count == 0)
                for (int i = 0; i < SlotCount; i++) _equipped.Add(EmptyID);

            foreach (int id in _startingBackpackIds) AddItem(id);
        }

        // OnListChanged no se dispara con el estado inicial: forzamos un primer calculo
        // (cubre a los clientes que se unen tarde).
        _dirty = true;
    }

    public override void OnNetworkDespawn()
    {
        _equipped.OnListChanged -= HandleEquipmentListChanged;
        _accessories.OnListChanged -= HandleEquipmentListChanged;
        _backpack.OnListChanged -= HandleBackpackListChanged;
    }

    private void HandleEquipmentListChanged(NetworkListEvent<int> changeEvent) => _dirty = true;
    private void HandleBackpackListChanged(NetworkListEvent<int> changeEvent) => OnBackpackChanged?.Invoke();

    // Los cambios se juntan y se aplican una sola vez por frame: asi un intercambio
    // (sacar un casco y poner otro) no deja pasar un estado intermedio con MaxHealth mas bajo.
    private void LateUpdate()
    {
        if (!_dirty || !IsSpawned) return;
        _dirty = false;

        RefreshModifiers();
        OnEquipmentChanged?.Invoke();
    }

    private void RefreshModifiers()
    {
        _modifierBuffer.Clear();

        for (int i = 0; i < _equipped.Count; i++)
            CollectModifiers(_equipped[i]);

        for (int i = 0; i < _accessories.Count; i++)
            CollectModifiers(_accessories[i]);

        _stats.SetModifiersFromSource(StatSource.Equipment, _modifierBuffer);
    }

    private void CollectModifiers(int id)
    {
        if (id < 0) return; // vacio u ocupado por arma de dos manos
        if (!_catalog.TryGetItem(id, out EquipableItemData item)) return;

        foreach (StatModifier m in item.modifiers) _modifierBuffer.Add(m);
    }

    // ---------------------------------------------------------------- Lectura (cualquier peer)

    public int GetEquippedId(EquipSlot slot)
    {
        int idx = (int)slot;
        return idx < _equipped.Count ? _equipped[idx] : EmptyID;
    }

    /// <summary>Item equipado en el slot, o null si esta vacio (o es la mano ocupada por un arma de dos manos).</summary>
    public ItemDataBase GetEquippedItem(EquipSlot slot)
    {
        int id = GetEquippedId(slot);
        return id >= 0 && _catalog.TryGetItem(id, out ItemDataBase item) ? item : null;
    }

    public bool IsHandOccupiedByTwoHanded(EquipSlot slot) => GetEquippedId(slot) == TwoHandedOccupied;

    public List<ItemDataBase> GetAccessories() => Resolve(_accessories);

    /// <summary>Solo tiene datos en el dueño y el servidor.</summary>
    public List<ItemDataBase> GetBackpack() => Resolve(_backpack);

    private List<ItemDataBase> Resolve(NetworkList<int> list)
    {
        var result = new List<ItemDataBase>(list.Count);
        for (int i = 0; i < list.Count; i++)
            if (_catalog.TryGetItem(list[i], out ItemDataBase item)) result.Add(item);
        return result;
    }

    // ---------------------------------------------------------------- Pedidos del cliente (-> RPC)

    /// <summary>slotIndex: Hand1/Hand2 para elegir mano, o -1 para tomar la primera libre.</summary>
    public void RequestEquip(int itemId, int slotIndex = -1) => EquipRpc(itemId, slotIndex);
    public void RequestUnequip(EquipSlot slot) => UnequipRpc((int)slot);
    public void RequestUnequipAccessory(int itemId) => UnequipAccessoryRpc(itemId);

    [Rpc(SendTo.Server)]
    private void EquipRpc(int itemId, int slotIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return; // solo el dueño
        ServerTryEquip(itemId, slotIndex);
    }

    [Rpc(SendTo.Server)]
    private void UnequipRpc(int slotIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        ServerTryUnequip(slotIndex);
    }

    [Rpc(SendTo.Server)]
    private void UnequipAccessoryRpc(int itemId, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        ServerTryUnequipAccessory(itemId);
    }

    // ---------------------------------------------------------------- Solo servidor (loot, donacion, trade, muerte)

    /// <summary>TODO: conectar con la fase de turno del DungeonManager (no equipar en planificacion/combate).</summary>
    public bool CanModifyEquipment() => true;

    public bool AddItem(ItemDataBase item) => item != null && AddItem(item.itemID);

    public bool AddItem(int itemId)
    {
        if (!IsServer) return false;
        if (!_catalog.TryGetItem(itemId, out ItemDataBase _))
        {
            Debug.LogWarning($"[PlayerInventory] AddItem: ID {itemId} no existe en el catalogo");
            return false;
        }
        _backpack.Add(itemId);
        return true;
    }

    /// <summary>Quita un item de la MOCHILA. Devuelve false si no estaba.</summary>
    public bool RemoveItem(ItemDataBase item) => item != null && RemoveItem(item.itemID);

    public bool RemoveItem(int itemId)
    {
        if (!IsServer) return false;
        return _backpack.Remove(itemId);
    }

    // ---------------------------------------------------------------- Logica de equipar (servidor)

    private bool ServerTryEquip(int itemId, int preferredSlot)
    {
        if (!IsServer || !CanModifyEquipment()) return false;
        if (!_backpack.Contains(itemId)) return false;
        if (!_catalog.TryGetItem(itemId, out EquipableItemData item)) return false;

        switch (item)
        {
            case ArmorData armor:
                EquipSlotItem(ToEquipSlot(armor.slot), itemId);
                return true;

            case AccessoryData _:
                _backpack.Remove(itemId);
                _accessories.Add(itemId);
                return true;

            case WeaponDataBase weapon:
                EquipWeapon(itemId, weapon, preferredSlot);
                return true;
        }
        return false;
    }

    private void EquipSlotItem(EquipSlot slot, int itemId)
    {
        MoveSlotToBackpack(slot);          // si habia algo, vuelve a la mochila
        _backpack.Remove(itemId);
        _equipped[(int)slot] = itemId;
    }

    private void EquipWeapon(int itemId, WeaponDataBase weapon, int preferredSlot)
    {
        if (weapon.handsRequired == HandsRequired.Two)
        {
            MoveSlotToBackpack(EquipSlot.Hand1);
            MoveSlotToBackpack(EquipSlot.Hand2);
            _backpack.Remove(itemId);
            _equipped[(int)EquipSlot.Hand1] = itemId;
            _equipped[(int)EquipSlot.Hand2] = TwoHandedOccupied;
            return;
        }

        // Una mano: si hay un arma de dos manos equipada, se desequipa entera primero
        if (_equipped[(int)EquipSlot.Hand2] == TwoHandedOccupied)
            MoveSlotToBackpack(EquipSlot.Hand1);

        EquipSlot target = ChooseHandSlot(preferredSlot);
        EquipSlotItem(target, itemId);
    }

    private EquipSlot ChooseHandSlot(int preferredSlot)
    {
        if (preferredSlot == (int)EquipSlot.Hand1 || preferredSlot == (int)EquipSlot.Hand2)
            return (EquipSlot)preferredSlot;                       // eligio mano (arrastrando)

        if (_equipped[(int)EquipSlot.Hand1] == EmptyID) return EquipSlot.Hand1;   // primera libre
        if (_equipped[(int)EquipSlot.Hand2] == EmptyID) return EquipSlot.Hand2;
        return EquipSlot.Hand1;                                    // ambas ocupadas: reemplaza la primera
    }

    private bool ServerTryUnequip(int slotIndex)
    {
        if (!IsServer || !CanModifyEquipment()) return false;
        if (slotIndex < 0 || slotIndex >= SlotCount) return false;
        if (_equipped[slotIndex] == EmptyID) return false;

        MoveSlotToBackpack((EquipSlot)slotIndex);
        return true;
    }

    private bool ServerTryUnequipAccessory(int itemId)
    {
        if (!IsServer || !CanModifyEquipment()) return false;
        if (!_accessories.Remove(itemId)) return false;

        _backpack.Add(itemId);
        return true;
    }

    /// <summary>Mueve lo que haya en el slot a la mochila (sin validar fase). Maneja las armas de dos manos.</summary>
    private void MoveSlotToBackpack(EquipSlot slot)
    {
        // Si piden vaciar la mano 2 y esta ocupada por un arma de dos manos, el arma esta en la mano 1
        if (slot == EquipSlot.Hand2 && _equipped[(int)EquipSlot.Hand2] == TwoHandedOccupied)
            slot = EquipSlot.Hand1;

        int idx = (int)slot;
        int id = _equipped[idx];
        if (id < 0) return; // vacio

        _equipped[idx] = EmptyID;
        if (slot == EquipSlot.Hand1 && _equipped[(int)EquipSlot.Hand2] == TwoHandedOccupied)
            _equipped[(int)EquipSlot.Hand2] = EmptyID;

        _backpack.Add(id);
    }

    private static EquipSlot ToEquipSlot(ArmorSlot armorSlot)
    {
        switch (armorSlot)
        {
            case ArmorSlot.Helmet: return EquipSlot.Helmet;
            case ArmorSlot.Chest: return EquipSlot.Chest;
            case ArmorSlot.Legs: return EquipSlot.Legs;
            case ArmorSlot.Boots: return EquipSlot.Boots;
            default: return EquipSlot.Helmet;
        }
    }
}

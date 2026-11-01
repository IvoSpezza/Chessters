using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UiInventoryManager : MonoBehaviour
{
    private static UiInventoryManager _instance;
    public static UiInventoryManager Instance => _instance;

    [SerializeField] private GameObject _uiItemPrefab;
    [SerializeField] private RectTransform _backpack;
    [SerializeField] private UiHealth _playerHealth;
    [SerializeField] private RectTransform _accessories;

    [SerializeField] private TextMeshProUGUI _statsText;

    [SerializeField] private Ui_ItemInInv _helmet;
    [SerializeField] private Ui_ItemInInv _chest;
    [SerializeField] private Ui_ItemInInv _leggins;
    [SerializeField] private Ui_ItemInInv _boots;
  
    [SerializeField] private Ui_ItemInInv _leftWeapon;
    [SerializeField] private Ui_ItemInInv _rigthWeapon;

    private Stats _stats;
    private PlayerInventory _inventory;
    private bool _backpackDirty;

    private readonly Dictionary<Ui_ItemInInv, EquipSlot> _slots = new Dictionary<Ui_ItemInInv, EquipSlot>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        RegisterSlot(_helmet, EquipSlot.Helmet);
        RegisterSlot(_chest, EquipSlot.Chest);
        RegisterSlot(_leggins, EquipSlot.Legs);
        RegisterSlot(_boots, EquipSlot.Boots);
        RegisterSlot(_leftWeapon, EquipSlot.Hand1);
        RegisterSlot(_rigthWeapon, EquipSlot.Hand2);
    }

    private void RegisterSlot(Ui_ItemInInv ui, EquipSlot slot)
    {
        if (ui != null) _slots[ui] = slot;
    }
    public void SetUpInv(PlayerInventory inventory, Stats stats)
    {
        ClearInv(_inventory);
        _inventory = inventory;
        _stats = stats;

        _inventory.OnEquipmentChanged += RefreshEquipment;
        _inventory.OnBackpackChanged += MarkBackpackDirty;

        _stats.OnStatChanged += OnStatsChange;
        foreach (Ui_ItemInInv ui in _slots.Keys) ui.OnClicked += OnSlotClicked;

        // Los eventos no avisan el estado inicial: dibujamos lo que ya existe
        RefreshEquipment();
        RefreshBackpack();
    }

    /// <summary>Solo limpia si el inventario que se despawnea es el que está enlazado.</summary>
    public void ClearInv(PlayerInventory inventory)
    {
        if (_inventory == null || _inventory != inventory) return;

        _inventory.OnEquipmentChanged -= RefreshEquipment;
        _inventory.OnBackpackChanged -= MarkBackpackDirty;
        foreach (Ui_ItemInInv ui in _slots.Keys)
        {
            ui.OnClicked -= OnSlotClicked;
            ui.Clear();
        }

        ClearContainer(_backpack);
        ClearContainer(_accessories);
        _inventory = null;
        _backpackDirty = false;
    }

    public void SetUpHealth(Health playerHealth) => _playerHealth.SetUpHealth(playerHealth);

    public void ClearHealth() => _playerHealth.ClearHealth();

    // ---------------------------------------------------------------- Mostrar / ocultar

    /// <summary>Se llama desde el input. Activa o desactiva el padre de la mochila.</summary>
    public void OnInventoryToggle()
    {
        Transform panel = _backpack != null ? _backpack.parent : null;
        if (panel == null) return;
        panel.gameObject.SetActive(!panel.gameObject.activeSelf);
    }

    // ---------------------------------------------------------------- Redibujado

    private void MarkBackpackDirty() => _backpackDirty = true;

    private void RefreshEquipment()
    {
        if (_inventory == null) return;
        
        foreach (KeyValuePair<Ui_ItemInInv, EquipSlot> pair in _slots)
            RefreshSlot(pair.Key, pair.Value);

        BuildList(_accessories, _inventory.GetAccessories(), OnAccessoryClicked);
    }

    private void RefreshSlot(Ui_ItemInInv ui, EquipSlot slot)
    {      
        if (_inventory.IsHandOccupiedByTwoHanded(slot))
        {
            ui.SetItem(_inventory.GetEquippedItem(EquipSlot.Hand1), true);
            return;
        }
        ui.SetItem(_inventory.GetEquippedItem(slot));
    }

    private void RefreshBackpack()
    {
        if (_inventory == null) return;
        BuildList(_backpack, _inventory.GetBackpack(), OnBackpackItemClicked);
    }

    // Destruye y recrea los items del contenedor (sin pooling, la mochila es chica)
    private void BuildList(RectTransform container, List<ItemDataBase> items, System.Action<Ui_ItemInInv> onClick)
    {
        if (container == null || _uiItemPrefab == null) return;

        ClearContainer(container);

        foreach (ItemDataBase item in items)
        {
            GameObject go = Instantiate(_uiItemPrefab, container, false);
            if (!go.TryGetComponent(out Ui_ItemInInv ui))
            {
                Debug.LogError("[UiInventoryManager] El prefab de item no tiene Ui_ItemInInv.", _uiItemPrefab);
                Destroy(go);
                return;
            }

            ui.SetItem(item);
            ui.OnClicked += onClick;
        }
    }

    private static void ClearContainer(RectTransform container)
    {
        if (container == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
            child.SetActive(false);   // Destroy es diferido: lo ocultamos para que el layout no lo cuente
            Destroy(child);
        }
    }
    private void LateUpdate()
    {
        if (!_backpackDirty) return;
        _backpackDirty = false;
        RefreshBackpack();
    }
    //--------------------------------------------------------------Stats

    private void OnStatsChange(StatType none)
    {
        _statsText.text = StatsText();
    }
    private string StatsText()
    {
        string text = "";

        text += $"PLayerStats: \n Damage: {_stats.GetStat(StatType.Damage)}\n";
        text += $"Level: {_stats.Level}\n";
        text += $"Max Health: {_stats.GetStat(StatType.MaxHealth)}\n";
        text += $"Atack Speed: {_stats.GetStat(StatType.AttackSpeed)}\n";
        text += $"Move Speed: {_stats.GetStat(StatType.MoveSpeed)}\n";
        return text;
    }
    // ---------------------------------------------------------------- Clicks

    private void OnSlotClicked(Ui_ItemInInv ui)
    {
        if (_inventory == null) return;
        if (_slots.TryGetValue(ui, out EquipSlot slot)) _inventory.RequestUnequip(slot);
    }

    private void OnBackpackItemClicked(Ui_ItemInInv ui)
    {
        if (_inventory == null || !(ui.Item is EquipableItemData)) return;   // consumibles: todavía sin uso
        _inventory.RequestEquip(ui.Item.itemID);                             // -1 = primera mano libre
    }

    private void OnAccessoryClicked(Ui_ItemInInv ui)
    {
        if (_inventory == null) return;
        _inventory.RequestUnequipAccessory(ui.Item.itemID);
    }

    private void OnDestroy()
    {
        ClearInv(_inventory);
        if (_instance == this) _instance = null;
    }
    
}

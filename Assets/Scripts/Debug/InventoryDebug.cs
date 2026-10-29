using UnityEngine;

/// <summary>
/// DEBUG TEMPORAL: panel en pantalla (OnGUI) para probar el inventario sin UI ni input.
/// Ponerlo en el prefab del player. Solo se muestra en el player del dueño.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class InventoryDebug : MonoBehaviour
{
    [SerializeField] private bool _visible = true;
    [SerializeField] private float _width = 330f;

    private PlayerInventory _inventory;
    private PlayerStats _stats;
    private Health _health;
    private Vector2 _scroll;

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<Health>();
    }

    private void OnGUI()
    {
        if (!_inventory.IsSpawned || !_inventory.IsOwner) return;

        if (GUI.Button(new Rect(10, 10, 120, 25), _visible ? "Ocultar inventario" : "Ver inventario"))
            _visible = !_visible;
        if (!_visible) return;

        GUILayout.BeginArea(new Rect(10, 40, _width, Screen.height - 50), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);

        // Stats finales
        GUILayout.Label("<b>STATS</b>");
        GUILayout.Label($"Vida {_health.CurrentHealth:0.#}/{_health.MaxHealth:0.#}  |  Dmg {_stats.GetStat(StatType.Damage):0.##}" +
                        $"  |  Vel {_stats.GetStat(StatType.MoveSpeed):0.##}  |  AtkSpd {_stats.GetStat(StatType.AttackSpeed):0.##}");
        if (GUILayout.Button("Test: recibir 5 de daño")) _health.TestDamageRpc(5);

        // Equipados
        GUILayout.Space(6);
        GUILayout.Label("<b>EQUIPADOS</b>");
        foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
        {
            GUILayout.BeginHorizontal();
            string text;
            if (_inventory.IsHandOccupiedByTwoHanded(slot)) text = "(arma de dos manos)";
            else
            {
                ItemDataBase item = _inventory.GetEquippedItem(slot);
                text = item != null ? ItemLabel(item) : "-";
            }
            GUILayout.Label($"{slot}: {text}", GUILayout.Width(_width - 110));
            if (GUILayout.Button("Quitar", GUILayout.Width(70))) _inventory.RequestUnequip(slot);
            GUILayout.EndHorizontal();
        }

        // Accesorios
        GUILayout.Space(6);
        GUILayout.Label("<b>ACCESORIOS</b>");
        foreach (ItemDataBase item in _inventory.GetAccessories())
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(ItemLabel(item), GUILayout.Width(_width - 110));
            if (GUILayout.Button("Quitar", GUILayout.Width(70))) _inventory.RequestUnequipAccessory(item.itemID);
            GUILayout.EndHorizontal();
        }

        // Mochila
        GUILayout.Space(6);
        GUILayout.Label($"<b>MOCHILA ({_inventory.BackpackCount})</b>");
        foreach (ItemDataBase item in _inventory.GetBackpack())
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(ItemLabel(item), GUILayout.Width(_width - 190));

            if (item is WeaponDataBase w && w.handsRequired == HandsRequired.One)
            {
                if (GUILayout.Button("Auto", GUILayout.Width(45))) _inventory.RequestEquip(item.itemID);
                if (GUILayout.Button("M1", GUILayout.Width(35))) _inventory.RequestEquip(item.itemID, (int)EquipSlot.Hand1);
                if (GUILayout.Button("M2", GUILayout.Width(35))) _inventory.RequestEquip(item.itemID, (int)EquipSlot.Hand2);
            }
            else if (item is EquipableItemData)
            {
                if (GUILayout.Button("Equipar", GUILayout.Width(70))) _inventory.RequestEquip(item.itemID);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static string ItemLabel(ItemDataBase item)
    {
        string n = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
        return $"[{item.itemID}] {n}";
    }
}

using UnityEngine;

public abstract class EquipableItemData : ItemDataBase
{
    [SerializeField] private ItemType _itemType;

    public abstract void Equip();
}

public enum ItemType
{
    Weapon,
    Armor,
    Accesory
}

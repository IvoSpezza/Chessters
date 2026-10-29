using UnityEngine;

public enum AccessoryCategoryType
{
    Ring,
    Amulet,
    Trinket
}

/// <summary>Items que no ocupan slot: se equipan de forma ilimitada.</summary>
[CreateAssetMenu(fileName = "New Accessory", menuName = "New Items/Accessory")]
public class AccessoryData : EquipableItemData
{
    [SerializeField] private AccessoryCategoryType _category;

    public AccessoryCategoryType category => _category;
}

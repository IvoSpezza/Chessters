using UnityEngine;

public enum ArmorSlot
{
    Helmet,
    Chest,
    Legs,
    Boots
}

public enum ArmorCategoryType
{
    Robe,
    Light,
    Heavy
}

[CreateAssetMenu(fileName = "New Armor", menuName = "New Items/Armor")]
public class ArmorData : EquipableItemData
{
    [SerializeField] private ArmorSlot _slot;
    [SerializeField] private ArmorCategoryType _category;
    [SerializeField] private GameObject _visualPrefab;

    public ArmorSlot slot => _slot;
    public ArmorCategoryType category => _category;
    public GameObject visualPrefab => _visualPrefab;
}
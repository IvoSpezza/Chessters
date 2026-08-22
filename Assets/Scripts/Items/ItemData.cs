using UnityEngine;

//Define los datos de un objeto
public class ItemData : ScriptableObject 
{
    [SerializeField] private string _itemName;
    [SerializeField] private Sprite _icon;
    [SerializeField] private GameObject _mesh;
    [SerializeField] private EquipmentSlot _slot;
    public EquipmentSlot Slot => _slot;
    
}

public enum EquipmentSlot
{
    Head,
    Armor,
    Boots,
    Hand,
    BackPack
}

[System.Serializable]
public class ItemInstance
{
    [SerializeField] private ItemData _data;
    public ItemData Data => _data;

    [SerializeField] string _id;
    public string Id => _id;


}

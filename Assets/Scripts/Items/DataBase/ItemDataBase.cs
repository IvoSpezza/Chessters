using UnityEngine;

public abstract class ItemDataBase : ScriptableObject
{
  [SerializeField] private string _itemName;
  [SerializeField] private string _itemDescription;
  [SerializeField] private int _itemID;
  [SerializeField] private Sprite _itemIcon;
  
  public string itemName => _itemName;
  public string itemDescription => _itemDescription;
  public int itemID => _itemID;
  public Sprite itemIcon => _itemIcon;
  
}

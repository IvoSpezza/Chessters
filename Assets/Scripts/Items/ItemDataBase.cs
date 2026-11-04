using UnityEngine;

/// <summary>
/// Base de todos los items. Es solo DATOS (un SO es un asset compartido por todos los jugadores).
/// En red viaja unicamente el itemID. El ID -1 se reserva para "slot vacio".
/// </summary>
public abstract class ItemDataBase : ScriptableObject
{
    [SerializeField] private int _itemID;
    [SerializeField] private string _itemName;
    [TextArea][SerializeField] private string _itemDescription;
    [SerializeField] private Sprite _itemIcon;

    public int itemID => _itemID;
    public string itemName => _itemName;
    public string itemDescription => _itemDescription;
    public Sprite itemIcon => _itemIcon;
}
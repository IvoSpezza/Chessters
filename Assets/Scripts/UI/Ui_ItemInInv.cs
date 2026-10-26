using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Ui_ItemInInv : MonoBehaviour
{
    [SerializeField] private Image _itemImage;
    [SerializeField] private TMP_Text _itemName;
    public ItemDataBase _item;

    private void Awake()
    {        
        _itemImage.sprite = _item.itemIcon;
        _itemName.text = _item.itemName;
    }

}

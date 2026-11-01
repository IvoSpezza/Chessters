using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Un casillero de item (slot de equipo o item de mochila/accesorios).
/// No decide nada: muestra un item y avisa cuando le hacen click izquierdo.
/// </summary>
public class Ui_ItemInInv : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image _itemImage;
    [SerializeField] private TMP_Text _itemName;
    [Tooltip("Transparencia cuando se muestra atenuado (mano 2 ocupada por arma de dos manos).")]
    [SerializeField, Range(0f, 1f)] private float _dimmedAlpha = 0.4f;

    public ItemDataBase Item { get; private set; }

    /// <summary>Se dispara con click izquierdo sobre un casillero que tiene item.</summary>
    public event Action<Ui_ItemInInv> OnClicked;

    public void SetItem(ItemDataBase item, bool dimmed = false)
    {
        Item = item;
        if (item == null)
        {
            Clear();
            return;
        }

        float alpha = dimmed ? _dimmedAlpha : 1f;

        if (_itemImage != null)
        {
            _itemImage.enabled = true;
            _itemImage.sprite = item.itemIcon;
            Color c = _itemImage.color;
            c.a = alpha;
            _itemImage.color = c;
        }

        if (_itemName != null)
        {
            _itemName.text = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
            Color c = _itemName.color;
            c.a = alpha;
            _itemName.color = c;
        }
    }

    public void Clear()
    {
        Item = null;
        if (_itemImage != null)
        {
            _itemImage.sprite = null;
            _itemImage.enabled = false;
        }
        if (_itemName != null) _itemName.text = string.Empty;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (Item == null) return;
        OnClicked?.Invoke(this);
    }
}
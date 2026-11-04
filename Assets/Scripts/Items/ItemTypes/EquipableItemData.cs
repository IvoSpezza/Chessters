using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Item que se puede equipar. Su efecto "global" es una lista de StatModifier
/// (flat o porcentual) que PlayerInventory aplica sobre PlayerStats.
/// </summary>
public abstract class EquipableItemData : ItemDataBase
{
    [SerializeField] private List<StatModifier> _modifiers = new List<StatModifier>();

    public IReadOnlyList<StatModifier> modifiers => _modifiers;
}
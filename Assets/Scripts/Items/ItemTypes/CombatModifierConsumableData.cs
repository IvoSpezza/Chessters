using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Consumible de planificación que aplica uno o más modificadores al objetivo elegido.
/// Sus efectos se replican y se eliminan cuando termina el combate actual.
/// </summary>
[CreateAssetMenu(fileName = "New Combat Modifier", menuName = "New Items/Consumables/Combat Modifier")]
public class CombatModifierConsumableData : ConsumableDataBase
{
    [SerializeField] private List<StatModifier> _modifiers = new List<StatModifier>();

    public IReadOnlyList<StatModifier> modifiers => _modifiers;
    public override ConsumableType consumableType => ConsumableType.CombatModifier;
}

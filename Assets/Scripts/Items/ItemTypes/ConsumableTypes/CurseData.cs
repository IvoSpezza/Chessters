using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maldicion. Es un consumible (ItemDataBase), asi que se resuelve por ID con el ItemCatalog.
/// Permanente hasta que la limpie un efecto o muera el jugador (ver PlayerCurses).
/// Salen de cofres de maldicion y, mas adelante, de recompensas (consumible que se aplica a otro jugador).
/// Rango de ID sugerido: 400 a 499 (consumibles).
/// </summary>
[CreateAssetMenu(fileName = "New Curse", menuName = "New Items/Consumables/Curse")]
public class CurseData : ConsumableDataBase
{
    [SerializeField] private List<StatModifier> _modifiers = new List<StatModifier>();

    public IReadOnlyList<StatModifier> modifiers => _modifiers;

    public override ConsumableType consumableType => ConsumableType.Curse;

    // TODO: restricciones (soledad: nadie puede ayudarte; bloqueo de una pieza de armadura). Se consultan desde PlayerCurses.
    // TODO: uso como consumible aplicado a otro jugador.
}

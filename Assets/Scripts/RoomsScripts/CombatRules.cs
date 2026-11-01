using System;
using UnityEngine;

public enum BadThingType
{
    None,
    Death,
    LoseItems,
    LoseLevels
}

public enum EscapeComparison
{
    GreaterThan,
    LessThan
}

/// <summary>Castigo por perder un combate. Se aplica si el d20 no cumple la EscapeCondition. TODO 2B: aplicarlo.</summary>
[Serializable]
public class BadThing
{
    public BadThingType type = BadThingType.None;
    [Min(0)] public int amount = 1;
}

/// <summary>Regla del d20 para salvarse. Ej: GreaterThan 15 = hay que sacar mas de 15.</summary>
[Serializable]
public class EscapeCondition
{
    public EscapeComparison comparison = EscapeComparison.GreaterThan;
    [Range(1, 20)] public int value = 10;

    public bool IsEscaped(int roll) => comparison == EscapeComparison.GreaterThan ? roll > value : roll < value;
}

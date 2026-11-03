public enum ConsumableType
{
    Potion,
    TepmModifier,
    Spawn,
    Curse,
    CombatModifier
}

public abstract class ConsumableDataBase : ItemDataBase
{
    public abstract ConsumableType consumableType { get; }
}

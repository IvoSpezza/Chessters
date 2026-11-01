public enum ConsumableType
{
    Potion,
    TepmModifier,
    Spawn,
    Curse
}

public abstract class ConsumableDataBase : ItemDataBase
{
    public abstract ConsumableType consumableType { get; }
}
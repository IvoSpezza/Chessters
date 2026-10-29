public enum ConsumableType
{
    Potion,
    Bomb,
    Trap
}

public abstract class ConsumableDataBase : ItemDataBase
{
    public abstract ConsumableType consumableType { get; }
}
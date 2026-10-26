using UnityEngine;

public enum ConsumableDataType
{
  Potion,
  Arrow
}

public abstract class ConsumableDataBase : ItemDataBase
{
  private ConsumableDataType _consumableDataType;
  
  public ConsumableDataType consumableDataType => _consumableDataType;
  
  protected void SetConsumableType(ConsumableDataType consumableType)
  {
    _consumableDataType = consumableType;
  }
  
  protected abstract void DefineConsumableType();
}

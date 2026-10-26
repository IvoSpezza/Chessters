using UnityEngine;

[CreateAssetMenu(fileName = "New Consumable Arrow", menuName = "New Items/Consumable/Arrow")]
public class ConsumableArrowData : ConsumableDataBase
{
  protected override void DefineConsumableType()
  {
    SetConsumableType(ConsumableDataType.Arrow);
  }
}

using UnityEngine;

[CreateAssetMenu(fileName = "New Consumable Potion", menuName = "New Items/Consumable/Potion")]
public class ConsumablePotionData : ConsumableDataBase
{
  protected override void DefineConsumableType()
  {
    SetConsumableType(ConsumableDataType.Potion);
  }
}

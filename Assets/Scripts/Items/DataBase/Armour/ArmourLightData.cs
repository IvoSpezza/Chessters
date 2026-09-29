using UnityEngine;

[CreateAssetMenu(fileName = "New Armour Light", menuName = "New Items/Armours/Light")]
public class ArmourLightData : ArmourDataBase
{
  protected override void DefineArmourCategory()
  {
    SetArmourCategory(ArmourCategoryType.Light);
  }
}

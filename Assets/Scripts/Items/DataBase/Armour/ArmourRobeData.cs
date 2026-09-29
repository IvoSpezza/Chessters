using UnityEngine;

[CreateAssetMenu(fileName = "New Armour Robe", menuName = "New Items/Armours/Robe")]
public class ArmourRobeData : ArmourDataBase
{
  protected override void DefineArmourCategory()
  {
    SetArmourCategory(ArmourCategoryType.Robe);
  }
}
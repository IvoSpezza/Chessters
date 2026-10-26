using UnityEngine;

[CreateAssetMenu(fileName = "New Armour Heavy", menuName = "New Items/Armours/Heavy")]
public class ArmourHeavyData : ArmourDataBase
{
    protected override void DefineArmourCategory()
  {
    SetArmourCategory(ArmourCategoryType.Heavy);
  }
}

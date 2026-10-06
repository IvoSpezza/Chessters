using UnityEngine;

[CreateAssetMenu(fileName = "New Jewelry Amulet", menuName = "New Items/Jewelry/Amulet")]
public class JewelryAmuletData : JeweleryDataBase
{
  protected override void DefineJewelryCategory()
  {
    SetJewelryCategory(JeweleryCategoryType.Amulet);
  }
}

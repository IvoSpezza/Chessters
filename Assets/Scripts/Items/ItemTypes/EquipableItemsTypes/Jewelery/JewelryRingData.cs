using UnityEngine;

[CreateAssetMenu(fileName = "New Jewelry Ring", menuName = "New Items/Jewelry/Ring")]
public class JewelryRingData : JeweleryDataBase
{
  protected override void DefineJewelryCategory()
  {
    SetJewelryCategory(JeweleryCategoryType.Ring);
  }
}

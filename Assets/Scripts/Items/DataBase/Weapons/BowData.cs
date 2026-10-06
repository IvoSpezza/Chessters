using UnityEngine;

public enum BowType
{
  ShotBow,
  LargeBow
}

[CreateAssetMenu(fileName = "New Weapon Bow", menuName = "New Items/Weapons/Bow")]
public class BowData : WeaponDataBase
{
  [SerializeField] private BowType _bowType;
  
  public BowType bowType => _bowType;

  protected override void DefineWeaponCategory()
  {
    SetWeaponCategory(WeaponCategoryType.Bow);
  }
  
  
}

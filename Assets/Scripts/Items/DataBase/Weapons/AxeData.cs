using UnityEngine;

public enum AxeType
{
  OneHanded,
  TwoHanded
}

[CreateAssetMenu(fileName = "New Weapon Axe", menuName = "New Items/Weapons/Axe")]
public class AxeData : WeaponDataBase
{ 
  
  [SerializeField] private AxeType _axeType;
  
  public AxeType axeType => _axeType;

  protected override void DefineWeaponCategory()
  {
    SetWeaponCategory(WeaponCategoryType.Axe);
  }
  
}

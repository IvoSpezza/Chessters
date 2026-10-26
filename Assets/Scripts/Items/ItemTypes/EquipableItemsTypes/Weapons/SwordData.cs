using UnityEngine;

public enum SwordType
{
  OneHanded,
  TwoHanded
}

[CreateAssetMenu(fileName = "New Weapon Sword", menuName = "New Items/Weapons/Sword")]
public class SwordData : WeaponDataBase
{ 
  
  [SerializeField] private SwordType _swordType;
  
  public SwordType swordType => _swordType;

  protected override void DefineWeaponCategory()
  {
    SetWeaponCategory(WeaponCategoryType.Sword);
  }
  
}

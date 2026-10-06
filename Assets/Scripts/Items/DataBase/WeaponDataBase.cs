using UnityEngine;

public enum WeaponCategoryType
{
  Sword,
  Axe,
  Bow,
  Polearm
}

public abstract class WeaponDataBase : ItemDataBase
{
  
  private WeaponCategoryType _weaponCategoryType;
  [SerializeField] private float _damageWeapon;
  [SerializeField] private float _attackSpeed;
  [SerializeField] private float _distanceRange;
  
    
  public WeaponCategoryType weaponCategoryType => _weaponCategoryType;
  public float damageWeapon => _damageWeapon;
  public float attackSpeed => _attackSpeed;
  public float distanceRange => _distanceRange;
  
  
  protected void SetWeaponCategory(WeaponCategoryType weaponCategory)
  {
    _weaponCategoryType = weaponCategory;
  }
  
  protected abstract void DefineWeaponCategory();
  
}

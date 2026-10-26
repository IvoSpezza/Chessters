using UnityEngine;

public enum WeaponCategoryType
{
  Sword,
  Axe,
  Bow,
  Polearm
}

public abstract class WeaponDataBase : EquipableItemData
{
  
    private WeaponCategoryType _weaponCategoryType;
    [SerializeField] private Mesh _weaponMesh;
    [SerializeField] private float _damageWeapon;
    [SerializeField] private float _attackSpeed;
    [SerializeField] private float _distanceRange;
  
    
    public WeaponCategoryType weaponCategoryType => _weaponCategoryType;
    public float damageWeapon => _damageWeapon;
    public float attackSpeed => _attackSpeed;
    public float distanceRange => _distanceRange;

    public Mesh Mesh => _weaponMesh;
  
  
    protected void SetWeaponCategory(WeaponCategoryType weaponCategory)
    {
        _weaponCategoryType = weaponCategory;
    }

    public override void Equip()
    {
        
    }
  
    protected abstract void DefineWeaponCategory();
  
}

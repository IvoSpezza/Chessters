using UnityEngine;

public enum ArmourCategoryType
{
  Robe,
  Light,
  Heavy
}

public enum ArmourPieceType
{
  Gloves,
  Boots,
  BodyArmour,
  Legs
}

public abstract class ArmourDataBase : ItemDataBase
{
  private ArmourCategoryType _armourCategoryType;
  [SerializeField] private ArmourPieceType _armourPieceType;
  [SerializeField] private float _defenseArmour;
  
  public ArmourCategoryType armourCategoryType => _armourCategoryType;
  public ArmourPieceType armourPieceType => _armourPieceType;
  public float defenseArmour => _defenseArmour;
  
  protected void SetArmourCategory(ArmourCategoryType armourCategory)
  {
    _armourCategoryType = armourCategory;
  }
  
  protected abstract void DefineArmourCategory();
  
}
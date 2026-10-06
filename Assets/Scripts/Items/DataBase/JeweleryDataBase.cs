using UnityEngine;

public enum JeweleryCategoryType
{
  Ring,
  Amulet
}

public abstract class JeweleryDataBase : ItemDataBase
{
  private JeweleryCategoryType _jeweleryCategoryType;
  
  public JeweleryCategoryType jeweleryCategoryType => _jeweleryCategoryType;
  
  protected void SetJewelryCategory(JeweleryCategoryType jeweleryCategory)
  {
    _jeweleryCategoryType = jeweleryCategory;
  }
  
  protected abstract void DefineJewelryCategory();
}

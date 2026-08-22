using UnityEngine;

public abstract class WeaponsData : ItemData
{
    [SerializeField] private float _damage;
    [SerializeField] private float _atackSpeed;
    [SerializeField] private AnimationClip _atackAnimation;

    public abstract IWeaponBehaviour CreateBehaviour();
}

public interface IWeaponBehaviour
{
    void Execute(AttackContext context);
}

//Info Needed to do an atack
public class AttackContext
{
    private GameObject _owner;
    private LayerMask _targetObjetive;
}


//---------------------MELEE---------------------------//

[CreateAssetMenu(menuName ="Weapons/Melee")]
public class MeleeWeaponData : WeaponsData
{
    [SerializeField] private Vector3 _hitBoxSize;
    [SerializeField] private Vector3 _hitBoxOffSet;
    [SerializeField] private float _hitBoxStart;
    [SerializeField] private float _hitBoxEnd;
    public override IWeaponBehaviour CreateBehaviour() => new MeleeAttackBehaviour(this);
    
}

public class MeleeAttackBehaviour : IWeaponBehaviour
{
    private MeleeWeaponData _data;
    public MeleeAttackBehaviour(MeleeWeaponData data) => _data = data;

    public void Execute(AttackContext context)
    {

    }
}

//---------------------RANGED-----------------------------//

[CreateAssetMenu(menuName = "Weapons/Ranged")]
public class RangedWeaponData : WeaponsData
{
    [SerializeField] private GameObject _proyectile;
    [SerializeField] private float _proyectileSpeed;
    [SerializeField] private Transform _shootingPoint;

    public override IWeaponBehaviour CreateBehaviour() => new RangedAttackBehaviour(this);
}

public class RangedAttackBehaviour : IWeaponBehaviour
{
    private RangedWeaponData _data;
    public RangedAttackBehaviour(RangedWeaponData data) => _data = data;

    public void Execute(AttackContext context)
    {

    }
}


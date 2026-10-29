using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum WeaponCategoryType
{
    Sword,
    Hammer,
    Ranged
}

public enum HandsRequired
{
    One = 1,
    Two = 2
}

/// <summary>
/// Base de armas. Cada arma conserva sus propios datos (no pasan por PlayerStats) y sabe atacar (Use).
/// Daño del golpe = stat Damage final del player * damageMultiplier.
/// El SO es un asset COMPARTIDO: Use() no debe guardar estado en campos.
/// </summary>
public abstract class WeaponDataBase : EquipableItemData
{
    [SerializeField] private HandsRequired _handsRequired = HandsRequired.One;

    [Tooltip("Coeficiente del daño final del player. 0.3 = 30%, 1 = golpe completo")]    
    [SerializeField, Range(0, 10)] private float _finalDamage = 1f;

    [Tooltip("Coeficiente de velocidad de ataque del arma: 1 = normal, 2 = el doble de rápido, 0.5 = la mitad. Se multiplica por el stat AttackSpeed del player.")]
    [SerializeField, Range(0, 10)] private float _finalAttackSpeed = 1f;

    [SerializeField] private GameObject _visualPrefab;

    public abstract WeaponCategoryType weaponCategory { get; }

    public HandsRequired handsRequired => _handsRequired;
    public float damageMultiplier => _finalDamage;
    public float finalAttackSpeed => _finalAttackSpeed;
    public GameObject visualPrefab => _visualPrefab;

    public struct AttackContext
    {
        public GameObject Owner;
        public Vector3 AtackOrigin;
        public Vector3 AtackDirection;
        public float Damage;
    }

    /// <summary>Solo servidor. Ejecuta el ataque del arma.</summary>
    public abstract void Use(AttackContext context, Collider[] hitsBuffer, HashSet<IDamageable> alreadyHit, LayerMask layer);

    /// <summary>
    /// True si el collider pertenece a un blanco válido: tiene IDamageable y no es el atacante.
    /// (Evitar golpear dos veces al mismo blanco lo controla cada arma con alreadyHit.)
    /// </summary>
    protected static bool TryGetTarget(Collider col, AttackContext context, out IDamageable target)
    {
        target = col.GetComponentInParent<IDamageable>();
        if (target == null) return false;

        if (context.Owner != null && col.transform.IsChildOf(context.Owner.transform)) return false;
        return true;
    }
}
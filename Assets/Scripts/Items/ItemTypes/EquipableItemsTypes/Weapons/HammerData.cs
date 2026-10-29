using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Hammer", menuName = "New Items/Weapons/Hammer")]
public class HammerData : WeaponDataBase
{
    [Tooltip("Radio de la esfera de impacto")]
    [SerializeField] private float _impactRadius = 1.5f;
    [Tooltip("Distancia al jugador donde ocurre el impacto")]
    [SerializeField] private float _impactDistance = 2f;

    public override WeaponCategoryType weaponCategory => WeaponCategoryType.Hammer;

    public override void Use(AttackContext context, Collider[] hitsBuffer, HashSet<IDamageable> alreadyHit, LayerMask layer)
    {
        Vector3 center = context.AtackOrigin + context.AtackDirection * _impactDistance;
        int count = Physics.OverlapSphereNonAlloc(center, _impactRadius, hitsBuffer, layer, QueryTriggerInteraction.Ignore);

        alreadyHit.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!TryGetTarget(hitsBuffer[i], context, out IDamageable target) || alreadyHit.Contains(target)) continue;

            alreadyHit.Add(target);
            target.TakeDamage(context.Damage);
        }
    }
}
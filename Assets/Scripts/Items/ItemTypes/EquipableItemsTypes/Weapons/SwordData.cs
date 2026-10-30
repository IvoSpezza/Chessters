using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Sword", menuName = "New Items/Weapons/Sword")]
public class SwordData : WeaponDataBase
{
    [Tooltip("Amplitud del slash en grados. 90 = tajo al frente, 360 = vuelta entera")]
    [SerializeField] private float _slashAngle = 90f;
    [Tooltip("Distancia que cubre el slash")]
    [SerializeField] private float _slashRange = 2f;

    public override WeaponCategoryType weaponCategory => WeaponCategoryType.Sword;

    public override void Use(AttackContext context, Collider[] hitsBuffer, HashSet<IDamageable> alreadyHit, LayerMask layer)
    {
        int count = Physics.OverlapSphereNonAlloc(context.AtackOrigin, _slashRange, hitsBuffer, layer, QueryTriggerInteraction.Ignore);
        float halfAngle = Mathf.Clamp(_slashAngle, 0f, 360f) * 0.5f;

        alreadyHit.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider col = hitsBuffer[i];
            if (!TryGetTarget(col, context, out IDamageable target) || alreadyHit.Contains(target)) continue;

            if (halfAngle < 180f)
            {
                // Punto más cercano del bounds: sirve para cualquier tipo de collider
                Vector3 toTarget = col.bounds.ClosestPoint(context.AtackOrigin) - context.AtackOrigin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(context.AtackDirection, toTarget) > halfAngle) continue;
            }

            alreadyHit.Add(target);
            target.TakeDamage(context.Damage);
        }
    }

    public override IEnumerator PlayAttackVisual(VisualContext ctx)
    {
        float angle = Mathf.Clamp(_slashAngle, 0f, 360f);
        float half = angle * 0.5f;

        // Abanico con el mismo ángulo y alcance que el hitbox
        AttackEffects.SpawnFan(ctx.Runner, ctx.EffectMaterial, ctx.AtackOrigin, ctx.AtackDirection,
                               angle, _slashRange, ctx.Duration * 1.5f);

        // El arma barre el arco de -half a +half
        float t = 0f;

        Quaternion initRot = ctx.Socket.rotation;
        ctx.Socket.Rotate(new(90,0,0),Space.Self);

        while (t < ctx.Duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / ctx.Duration);
            Vector3 dir = Quaternion.AngleAxis(Mathf.Lerp(-half, half, k), Vector3.forward) * ctx.AtackDirection;
            ctx.Socket.rotation = Quaternion.LookRotation(dir, Vector3.up);
            yield return null;
        }
    }
}
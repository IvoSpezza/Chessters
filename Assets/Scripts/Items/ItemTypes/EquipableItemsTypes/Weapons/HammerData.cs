using System.Collections;
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

    public override IEnumerator PlayAttackVisual(VisualContext ctx)
    {
        Transform socket = ctx.Socket;

        // Tiempos: el impacto cae en impactFraction (el mismo momento en que se aplica el daño)
        float impactTime = ctx.Duration * impactFraction;
        float windup = impactTime * 0.7f;
        float slam = impactTime - windup;
        float recover = ctx.Duration - impactTime;

        // Centro del hitbox proyectado al suelo
        Vector3 center = ctx.AtackOrigin + ctx.AtackDirection * _impactDistance;
        Vector3 ground = new Vector3(center.x, ctx.Body.position.y + 0.05f, center.z);

        Quaternion start = socket.rotation;

        
        Quaternion raised = Quaternion.LookRotation((Vector3.up - ctx.AtackDirection * 0.3f).normalized, Vector3.up);
        Vector3 toGround = ground - socket.position;
        Quaternion slamRot = Quaternion.LookRotation(
            toGround.sqrMagnitude > 0.0001f ? toGround.normalized : ctx.AtackDirection, Vector3.up);

        // 1) Sube sobre el hombro
        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;
            socket.rotation = Quaternion.Slerp(start, raised, Mathf.Clamp01(t / windup));
            yield return null;
        }

        // 2) Baja al punto de impacto (acelerando)
        t = 0f;
        while (t < slam)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / slam);
            socket.rotation = Quaternion.Slerp(raised, slamRot, k * k);
            yield return null;
        }
        socket.rotation = slamRot;

        // 3) Impacto: anillo en el suelo
        AttackEffects.SpawnRing(ctx.Runner, ctx.EffectMaterial, ground, _impactRadius, 0.35f);

        // 4) Vuelve a la posición inicial
        t = 0f;
        while (t < recover)
        {
            t += Time.deltaTime;
            socket.rotation = Quaternion.Slerp(slamRot, start, Mathf.Clamp01(t / recover));
            yield return null;
        }
    }
}
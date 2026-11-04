using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Ranged", menuName = "New Items/Weapons/Ranged")]
public class RangedData : WeaponDataBase
{
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private float _projectileSpeed = 15f;
    [SerializeField] private float _projectileLifetime = 2f;
    [Tooltip("Distancia al frente del origen del ataque donde nace el proyectil.")]
    [SerializeField] private float _proyectileSpawnDistance = 0.08f;
    [Tooltip("Proyectiles que se crean de antemano en el pool del servidor.")]
    [SerializeField, Min(0)] private int _preWarmCount = 5;
    [Tooltip("Capas que frenan el proyectil (suelo, paredes), además de los enemigos.")]
    [SerializeField] private LayerMask _obstacleMask;

    private const float RecoilDistance = 0.12f;
    public override float impactFraction => 0f;
    public override WeaponCategoryType weaponCategory => WeaponCategoryType.Ranged;

    public GameObject projectilePrefab => _projectilePrefab;
    public float projectileSpeed => _projectileSpeed;
    public float projectileLifetime => _projectileLifetime;
    public int prewarmCount => _preWarmCount;

    /// <summary>Predicción visual local que espera confirmación del servidor.</summary>
    public void PredictProjectile(Vector3 origin, Vector3 direction, ulong ownerClientId, uint sequence)
    {
        if (_projectilePrefab == null) return;
        Vector3 spawnPos = origin + direction * _proyectileSpawnDistance;
        Projectile.SpawnPredictedVisual(_projectilePrefab, spawnPos, Quaternion.LookRotation(direction),
            direction, _projectileSpeed, _projectileLifetime, ownerClientId, sequence, awaitConfirmation: true);
    }

    /// <summary>Visual independiente en los peers; no tiene colisiones ni replica transform.</summary>
    public void ShowRemoteProjectile(Vector3 position, Vector3 direction, ulong ownerClientId, uint sequence)
    {
        if (_projectilePrefab == null) return;
        Projectile.SpawnPredictedVisual(_projectilePrefab, position, Quaternion.LookRotation(direction),
            direction, _projectileSpeed, _projectileLifetime, ownerClientId, sequence, awaitConfirmation: false);
    }

    public override void Use(AttackContext context, Collider[] hitsBuffer, HashSet<IDamageable> alreadyHit, LayerMask layer)
    {
        if (_projectilePrefab == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        Vector3 spawnPos = context.AtackOrigin + context.AtackDirection * _proyectileSpawnDistance;
        Projectile projectile = ProjectilePool.Get(_projectilePrefab, spawnPos, Quaternion.LookRotation(context.AtackDirection));
        if (projectile == null) return;

        ulong ownerNetworkId = context.Owner != null && context.Owner.TryGetComponent(out NetworkObject ownerObject)
            ? ownerObject.NetworkObjectId
            : ulong.MaxValue;
        ulong predictionOwner = context.HasPredictionKey ? context.PredictionOwnerClientId : ulong.MaxValue;
        projectile.SetServerPredictionKey(predictionOwner, context.PredictionSequence);
        projectile.ServerLaunch(context.AtackDirection, _projectileSpeed, context.Damage, _projectileLifetime,
            layer | _obstacleMask, ownerNetworkId);

        if (context.HasPredictionKey && GameManager.Instance != null)
            GameManager.Instance.ServerBroadcastProjectileLaunched(itemID, spawnPos, context.AtackDirection,
                predictionOwner, context.PredictionSequence);
    }

    public override IEnumerator PlayAttackVisual(VisualContext ctx)
    {
        Transform socket = ctx.Socket;
        Transform parent = socket.parent;
        Vector3 restLocal = socket.localPosition;
        Quaternion aim = Quaternion.LookRotation(ctx.AtackDirection, Vector3.up);

        AttackEffects.SpawnFlash(ctx.Runner, ctx.EffectMaterial, socket.position + ctx.AtackDirection * 0.5f,
            ctx.AtackDirection, 0.25f, 0.12f);

        float t = 0f;
        while (t < ctx.Duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / ctx.Duration);
            float recoil = k < 0.2f ? k / 0.2f : 1f - (k - 0.2f) / 0.8f;
            socket.rotation = aim;
            socket.position = parent.TransformPoint(restLocal) - ctx.AtackDirection * (recoil * RecoilDistance);
            yield return null;
        }
    }
}

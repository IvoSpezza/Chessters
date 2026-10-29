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
    [Tooltip("Proyectiles que se crean de antemano en el pool (el pool crece solo si hace falta).")]
    [SerializeField, Min(0)] private int _preWarmCount = 5;
    [Tooltip("Capas que frenan el proyectil (suelo, paredes), además de los enemigos.")]
    [SerializeField] private LayerMask _obstacleMask;

    public override WeaponCategoryType weaponCategory => WeaponCategoryType.Ranged;

    public GameObject projectilePrefab => _projectilePrefab;
    public float projectileSpeed => _projectileSpeed;
    public float projectileLifetime => _projectileLifetime;
    public int prewarmCount => _preWarmCount;

    public override void Use(AttackContext context, Collider[] hitsBuffer, HashSet<IDamageable> alreadyHit, LayerMask layer)
    {
        if (_projectilePrefab == null) return;

        Vector3 spawnPos = context.AtackOrigin + context.AtackDirection * _proyectileSpawnDistance;
        NetworkObject projectileObject = ProjectilePool.Get(_projectilePrefab, spawnPos, Quaternion.LookRotation(context.AtackDirection));
        if (projectileObject == null) return;

        projectileObject.Spawn(true);   // true = se despawnea con la escena

        if (projectileObject.TryGetComponent(out Projectile projectile))
            projectile.ServerLaunch(context.AtackDirection, _projectileSpeed, context.Damage, _projectileLifetime, layer | _obstacleMask);
        else
            Debug.LogError($"[RangedData] El prefab {_projectilePrefab.name} no tiene el script Projectile.", _projectilePrefab);
    }
}
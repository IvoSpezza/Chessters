using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Le da a un enemigo con Stats y Health el poder equiparles un arma y que persigan a l jugador mas cercano pata atacarlo.</summary>
[RequireComponent(typeof(NetworkObject), typeof(Stats), typeof(Health))]
public class MonsterCombatAI : NetworkBehaviour
{
    private const float MinAttackInterval = 0.05f;

    [Header("Arma: asignar Hammer o Sword")]
    [SerializeField]private WeaponDataBase Weapon;   

    [Header("Visual del ataque")]
    [Tooltip("Desde donde sale el ataque")]
    [SerializeField] private Transform _attackSocket;
    [SerializeField] private Material _effectMaterial;

    [Header("Movimiento")]
    [SerializeField, Min(0.1f)] private float _attackRange = 2f;
    [SerializeField, Min(0.1f)] private float _retreatDistance = 4f;
    [SerializeField, Min(0f)] private float _retreatSpeedMultiplier = 1f;

    [Header("Impacto")]
    [SerializeField, Min(0f)] private float _attackOriginHeight = 0.5f;
    [SerializeField] private LayerMask _hitMask = ~0;

    private Stats _stats;
    private Health _health;
    private float _nextAttackTime;
    private Vector3 _socketRestPosition;
    private Quaternion _socketRestRotation;
    private Coroutine _visualRoutine;
    private readonly Collider[] _hitBuffer = new Collider[32];
    private readonly HashSet<IDamageable> _alreadyHit = new HashSet<IDamageable>();


    private void Awake()
    {
        _stats = GetComponent<Stats>();
        _health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        if (_attackSocket != null)
        {
            _socketRestPosition = _attackSocket.localPosition;
            _socketRestRotation = _attackSocket.localRotation;
        }        
    }

    public override void OnNetworkDespawn()
    {
        if (_visualRoutine != null) StopCoroutine(_visualRoutine);
        _visualRoutine = null;
        ResetAttackSocket();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || _health == null || _health.IsDead ||
            GameManager.Instance == null || GameManager.Instance.Phase != GamePhase.Combat)
            return;

        WeaponDataBase weapon = Weapon;
        if (weapon == null) return;

        if (!TryGetNearestParticipant(out Health targetHealth, out Vector3 offset, out float distance)) return;

        Vector3 direction = offset.normalized;
        Face(direction);

        if (distance > _attackRange)
        {
            Move(direction, _stats.GetStat(StatType.MoveSpeed));
            return;
        }

        if (Time.time >= _nextAttackTime)
        {
            Attack(weapon, targetHealth, direction);
            return;
        }

        
        if (distance < _retreatDistance) Move(-direction, _stats.GetStat(StatType.MoveSpeed) * _retreatSpeedMultiplier);
    }

    private bool TryGetNearestParticipant(out Health nearestHealth, out Vector3 nearestOffset, out float nearestDistance)
    {
        nearestHealth = null;
        nearestOffset = Vector3.zero;
        nearestDistance = float.PositiveInfinity;

        foreach (PlayerSoul soul in PlayerSoul.All)
        {
            if (soul == null || !GameManager.Instance.IsParticipant(soul.OwnerClientId) ||
                !soul.TryGetBodyComponent(out Health candidate) || candidate.IsDead)
                continue;

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance >= nearestDistance) continue;

            nearestHealth = candidate;
            nearestOffset = offset;
            nearestDistance = distance;
        }

        return nearestHealth != null;
    }

    private void Attack(WeaponDataBase weapon, Health target, Vector3 direction)
    {
        float attacksPerSecond = _stats.GetStat(StatType.AttackSpeed) * weapon.finalAttackSpeed;
        float interval = Mathf.Max(1f / Mathf.Max(attacksPerSecond, 0.0001f), MinAttackInterval);
        _nextAttackTime = Time.time + interval;

        var context = new WeaponDataBase.AttackContext
        {
            Owner = gameObject,
            Target = target,
            AtackOrigin = transform.position + Vector3.up * _attackOriginHeight,
            AtackDirection = direction,
            Damage = _stats.GetStat(StatType.Damage) * weapon.damageMultiplier
        };

        float duration = Mathf.Min(weapon.visualDuration, interval * 0.8f);
        PlayAttackVisualRpc(context.AtackOrigin, direction, duration);

        float impactDelay = duration * weapon.impactFraction;
        if (impactDelay <= 0f) ApplyImpact(weapon, context);
        else StartCoroutine(ServerApplyImpactAfter(impactDelay, weapon, context));
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayAttackVisualRpc(Vector3 origin, Vector3 direction, float duration)
    {
        WeaponDataBase weapon = Weapon;
        if (weapon == null || _attackSocket == null) return;

        if (_visualRoutine != null) StopCoroutine(_visualRoutine);
        ResetAttackSocket();

        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        var visualContext = new WeaponDataBase.VisualContext
        {
            Runner = this,
            Body = transform,
            Socket = _attackSocket,
            AtackOrigin = origin,
            AtackDirection = direction,
            Duration = Mathf.Max(duration, 0.01f),
            EffectMaterial = _effectMaterial != null ? _effectMaterial : AttackEffects.DefaultMaterial
        };
        _visualRoutine = StartCoroutine(RunAttackVisual(weapon.PlayAttackVisual(visualContext)));
    }

    private IEnumerator RunAttackVisual(IEnumerator visual)
    {
        while (visual.MoveNext()) yield return visual.Current;
        ResetAttackSocket();
        _visualRoutine = null;
    }

    private IEnumerator ServerApplyImpactAfter(float delay, WeaponDataBase weapon, WeaponDataBase.AttackContext context)
    {
        yield return new WaitForSeconds(delay);
        ApplyImpact(weapon, context);
    }

    private void ApplyImpact(WeaponDataBase weapon, WeaponDataBase.AttackContext context)
    {
        Health target = context.Target as Health;
        if (!IsServer || !IsSpawned || _health.IsDead || target == null || target.IsDead) return;
        weapon.Use(context, _hitBuffer, _alreadyHit, _hitMask);
    }

    private void ResetAttackSocket()
    {
        if (_attackSocket == null) return;
        _attackSocket.localPosition = _socketRestPosition;
        _attackSocket.localRotation = _socketRestRotation;
    }

    private void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private void Move(Vector3 direction, float speed)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f || speed <= 0f) return;
        transform.position += direction.normalized * speed * Time.deltaTime;
    }
}

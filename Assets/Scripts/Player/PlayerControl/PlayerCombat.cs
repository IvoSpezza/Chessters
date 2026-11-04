using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Ataque por mano.
///
/// Flujo:
///  - El dueÃ±o lee el click de cada mano (MainAttack = Hand1, SecondAtack = Hand2). Mantener apretado repite.
///  - Calcula su cooldown local solo para no mandar RPCs de mÃ¡s, y avisa al servidor (mano + direcciÃ³n).
///  - El SERVIDOR valida (dueÃ±o, vivo, arma equipada, cooldown propio) y llama a weapon.Use():
///    cada arma sabe cÃ³mo atacar. Agregar un tipo de arma nuevo no requiere tocar este script.
///
/// DaÃ±o del golpe  = stat Damage final * damageMultiplier del arma.
/// Ataques por seg = stat AttackSpeed final * finalAttackSpeed del arma.
/// Arma de dos manos: ambos clicks hacen el mismo ataque y comparten cooldown.
/// </summary>
[RequireComponent(typeof(PlayerInventory), typeof(Stats), typeof(Health))]
public class PlayerCombat : NetworkBehaviour
{
    private const float MIN_ATTACK_INTERVAL = 0.05f;

    [Header("Input (nombres exactos en el Input Actions asset)")]
    [SerializeField] private string _mainAttackAction = "MainAttack";
    [SerializeField] private string _secondAttackAction = "SecondAtack";

    [Header("Golpes")]
    [Tooltip("Capas que reciben daÃ±o.")]
    [SerializeField] private LayerMask _enemyMask;
    [Tooltip("Altura sobre el pivote del cuerpo desde donde salen los golpes y proyectiles.")]

    [SerializeField] private float _attackOriginHeight = 0.5f;
    [Tooltip("Margen en segundos para absorber el jitter de red al validar el cooldown en el servidor.")]
    [SerializeField, Min(0)] private float _cooldownTolerance = 0.1f;


    [Header("Debug")]
    [SerializeField] private bool _debugLogs;



    private PlayerInventory _inventory;
    private Stats _stats;
    private Health _health;
    private LookToMause _lookToMause;
    private PlayerInput _playerInput;
    private InputAction _mainAction;
    private InputAction _secondAction;

    private PlayerAttackVisuals _visuals;

  
    private readonly float[] _localNext = new float[2]; //CoolDown local para no spamear RCP 
    private readonly float[] _serverNext = new float[2]; // CoolDown de la verdad
    private uint _nextShotSequence = 1;

    // Buffers que se reutilizan en cada ataque (se los pasamos a weapon.Use para no generar basura)
    private readonly Collider[] _hitBuffer = new Collider[32];
    private readonly HashSet<IDamageable> _alreadyHit = new HashSet<IDamageable>();

    // ---------------------------------------------------------------- 

    public override void OnNetworkSpawn()
    {
        _inventory = GetComponent<PlayerInventory>();
        _stats = GetComponent<Stats>();
        _health = GetComponent<Health>();
        _visuals = GetComponent<PlayerAttackVisuals>();        

        _playerInput = GetComponent<PlayerInput>();
        _playerInput.enabled = IsOwner;

        ProjectilePool.RegisterAll(_inventory.Catalog);

        if (!IsOwner) return;

        _lookToMause = GetComponent<LookToMause>();
        FindActions();
    }

    private void FindActions()
    {
        if (_playerInput == null || _playerInput.actions == null) return;

        _mainAction = _playerInput.actions.FindAction(_mainAttackAction, false);
        _secondAction = _playerInput.actions.FindAction(_secondAttackAction, false);

        if (_mainAction == null) Debug.LogError($"[PlayerCombat] No existe la acciÃ³n '{_mainAttackAction}'.", this);
        if (_secondAction == null) Debug.LogError($"[PlayerCombat] No existe la acciÃ³n '{_secondAttackAction}'.", this);
    }

    /// <summary>TODO: conectar con la fase de turno (no atacar fuera de combate, ni en planificaciÃ³n).</summary>
    public bool CanAttack()
    {
        PlayerSoul soul = PlayerSoul.GetFor(OwnerClientId);
        return (soul == null || soul.IsAlive) &&
               (GameManager.Instance == null || GameManager.Instance.CanAttack(OwnerClientId));
    }

    // ---------------------------------------------------------------- Detecto Imputs de Cliente dueÃ±o
       
    public void OnMainAttack(InputValue input)
    {
        if (!CanAttack() || _health.IsDead) return;
        if (!TryResolveWeapon(EquipSlot.Hand1, out WeaponDataBase weapon, out int cooldownIndex)) return;
        if (Time.time < _localNext[cooldownIndex]) return;

        _localNext[cooldownIndex] = Time.time + GetAttackInterval(weapon);

        Vector3 aim = _lookToMause.GetOrientation();
        uint shotSequence = _nextShotSequence++;
        PlayVisual(cooldownIndex, weapon, aim);
        PredictRangedProjectile(weapon, aim, shotSequence);
        AttackRpc(0, aim, shotSequence);
    }
    public void OnSecondAtack(InputValue input)
    {
        if (!CanAttack() || _health.IsDead) return;
        if (!TryResolveWeapon(EquipSlot.Hand2, out WeaponDataBase weapon, out int cooldownIndex)) return;
        if (Time.time < _localNext[cooldownIndex]) return;

        _localNext[cooldownIndex] = Time.time + GetAttackInterval(weapon);

        Vector3 aim = _lookToMause.GetOrientation();
        uint shotSequence = _nextShotSequence++;
        PlayVisual(cooldownIndex, weapon, aim);
        PredictRangedProjectile(weapon, aim, shotSequence);
        AttackRpc(1, aim, shotSequence);
    }

    // ---------------------------------------------------------------- Servidor

    [Rpc(SendTo.Server)]
    private void AttackRpc(int handIndex, Vector3 aimDirection, uint shotSequence, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;   // solo el dueÃ±o
        if (handIndex < 0 || handIndex > 1)
        {
            AttackResultRpc(shotSequence, false, false);
            return;
        }

        bool hasProjectile;
        bool accepted = ServerAttack(handIndex == 0 ? EquipSlot.Hand1 : EquipSlot.Hand2, aimDirection, shotSequence, out hasProjectile);
        AttackResultRpc(shotSequence, accepted, hasProjectile);
    }

    private bool ServerAttack(EquipSlot hand, Vector3 aimDirection, uint shotSequence, out bool hasProjectile)
    {
        hasProjectile = false;
        if (!CanAttack() || _health.IsDead) return false;
        if (!TryResolveWeapon(hand, out WeaponDataBase weapon, out int cooldownIndex)) return false;

        float now = Time.time;
        if (now < _serverNext[cooldownIndex] - _cooldownTolerance) return false;   // demasiado pronto
        _serverNext[cooldownIndex] = now + GetAttackInterval(weapon);
                
        aimDirection.y = 0f;
        Vector3 dir = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : transform.forward;                

        var context = new WeaponDataBase.AttackContext
        {
            Owner = gameObject,
            AtackOrigin = transform.position + Vector3.up * _attackOriginHeight,
            AtackDirection = dir,
            Damage = _stats.GetStat(StatType.Damage) * weapon.damageMultiplier,
            PredictionOwnerClientId = OwnerClientId,
            PredictionSequence = shotSequence,
            HasPredictionKey = weapon is RangedData
        };

        EquipSlot slot = cooldownIndex == 0 ? EquipSlot.Hand1 : EquipSlot.Hand2;
        float delay = GetVisualDuration(weapon) * weapon.impactFraction;

        if (_debugLogs)
            Debug.Log($"[Combat] {name} {hand} {weapon.name} daÃ±o={context.Damage:F2} intervalo={GetAttackInterval(weapon):F2}s delayImpacto={delay:F3}s");

        AttackVisualRpc(cooldownIndex, dir);   // los demÃ¡s peers ven el swing (el dueÃ±o ya lo predijo)

        if (delay <= 0f) ServerApplyImpact(slot, weapon, context);
        else StartCoroutine(ServerImpactAfter(delay, slot, weapon, context));

        hasProjectile = weapon is RangedData ranged && ranged.projectilePrefab != null;
        return true;
    }
    private IEnumerator ServerImpactAfter(float delay, EquipSlot slot, WeaponDataBase weapon, WeaponDataBase.AttackContext context)
    {
        yield return new WaitForSeconds(delay);
        ServerApplyImpact(slot, weapon, context);
    }

    /// <summary>
    /// Momento del impacto (solo servidor). Punto Ãºnico donde mÃ¡s adelante se podrÃ­a sumar
    /// tolerancia de hitbox o rewind sin reescribir nada.
    /// </summary>
    private void ServerApplyImpact(EquipSlot slot, WeaponDataBase weapon, WeaponDataBase.AttackContext context)
    {
        if (!IsSpawned || _health.IsDead) return;                   // muriÃ³ durante el swing
        if (_inventory.GetEquippedItem(slot) != weapon) return;     // desequipÃ³ o cambiÃ³ el arma: se cancela

        LayerMask targetMask = _enemyMask;
        if (GameManager.Instance != null && GameManager.Instance.IsFinalDuel)
            targetMask |= LayerMask.GetMask("Player");

        weapon.Use(context, _hitBuffer, _alreadyHit, targetMask);
    }

    /// <summary>El servidor avisa a los demÃ¡s peers que hubo un ataque. handIndex = mano ya resuelta (0 = Hand1, 1 = Hand2).</summary>
    [Rpc(SendTo.NotOwner)]
    private void AttackVisualRpc(int handIndex, Vector3 aimDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;   // solo el servidor lo manda
        if (handIndex < 0 || handIndex > 1) return;

        EquipSlot slot = handIndex == 0 ? EquipSlot.Hand1 : EquipSlot.Hand2;
        if (_inventory.GetEquippedItem(slot) is WeaponDataBase weapon)
            PlayVisual(handIndex, weapon, aimDirection);
    }

    private void PlayVisual(int handIndex, WeaponDataBase weapon, Vector3 aimDirection)
    {
        if (_visuals == null) return;

        Vector3 origin = transform.position + Vector3.up * _attackOriginHeight;
        _visuals.PlayAttack(handIndex, weapon, origin, aimDirection, GetVisualDuration(weapon));
    }

    /// <summary>DuraciÃ³n del visual: la base del arma, acortada para que un buff de velocidad no pise el golpe siguiente.</summary>
    private void PredictRangedProjectile(WeaponDataBase weapon, Vector3 aimDirection, uint shotSequence)
    {
        RangedData ranged = weapon as RangedData;
        if (ranged == null) return;
        aimDirection.y = 0f;
        if (aimDirection.sqrMagnitude <= 0.0001f) aimDirection = transform.forward;
        aimDirection.Normalize();
        Vector3 origin = transform.position + Vector3.up * _attackOriginHeight;
        ranged.PredictProjectile(origin, aimDirection, OwnerClientId, shotSequence);
    }

    [Rpc(SendTo.Owner)]
    private void AttackResultRpc(uint shotSequence, bool accepted, bool hasProjectile, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        if (accepted && hasProjectile) Projectile.ConfirmPredictedVisual(OwnerClientId, shotSequence);
        else Projectile.CancelPredictedVisual(OwnerClientId, shotSequence);
    }
    private float GetVisualDuration(WeaponDataBase weapon) =>
        Mathf.Min(weapon.visualDuration, GetAttackInterval(weapon) * 0.8f);

    // ---------------------------------------------------------------- Helpers

    /// <summary>Devuelve el arma que corresponde a la mano. Un arma de dos manos responde desde ambas.</summary>
    private bool TryResolveWeapon(EquipSlot hand, out WeaponDataBase weapon, out int cooldownIndex)
    {
        EquipSlot slot = _inventory.IsHandOccupiedByTwoHanded(hand) ? EquipSlot.Hand1 : hand;
        cooldownIndex = slot == EquipSlot.Hand1 ? 0 : 1;
        weapon = _inventory.GetEquippedItem(slot) as WeaponDataBase;
        return weapon != null;
    }

    private float GetAttackInterval(WeaponDataBase weapon)
    {
        float attacksPerSecond = _stats.GetStat(StatType.AttackSpeed) * weapon.finalAttackSpeed;
        return Mathf.Max(1f / Mathf.Max(attacksPerSecond, 0.0001f), MIN_ATTACK_INTERVAL);
    }
}

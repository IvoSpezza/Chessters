using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ataque por mano. Va en el CUERPO del jugador.
///
/// Flujo:
///  - El dueño lee el click de cada mano (MainAttack = Hand1, SecondAtack = Hand2). Mantener apretado repite.
///  - Calcula su cooldown local solo para no mandar RPCs de más, y avisa al servidor (mano + dirección).
///  - El SERVIDOR valida (dueño, vivo, arma equipada, cooldown propio) y llama a weapon.Use():
///    cada arma sabe cómo atacar. Agregar un tipo de arma nuevo no requiere tocar este script.
///
/// Daño del golpe  = stat Damage final * damageMultiplier del arma.
/// Ataques por seg = stat AttackSpeed final * finalAttackSpeed del arma.
/// Arma de dos manos: ambos clicks hacen el mismo ataque y comparten cooldown.
/// </summary>
[RequireComponent(typeof(PlayerInventory), typeof(PlayerStats), typeof(Health))]
public class PlayerCombat : NetworkBehaviour
{
    private const float MIN_ATTACK_INTERVAL = 0.05f;

    [Header("Input (nombres exactos en el Input Actions asset)")]
    [SerializeField] private string _mainAttackAction = "MainAttack";
    [SerializeField] private string _secondAttackAction = "SecondAtack";

    [Header("Golpes")]
    [Tooltip("Capas que reciben daño. No incluir la capa de los jugadores.")]
    [SerializeField] private LayerMask _enemyMask;
    [Tooltip("Altura sobre el pivote del cuerpo desde donde salen los golpes y proyectiles.")]
    [SerializeField] private float _attackOriginHeight = 0.5f;
    [Tooltip("Margen en segundos para absorber el jitter de red al validar el cooldown en el servidor.")]
    [SerializeField, Min(0)] private float _cooldownTolerance = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs;

    private PlayerInventory _inventory;
    private PlayerStats _stats;
    private Health _health;
    private LookToMause _lookToMause;
    private PlayerInput _playerInput;
    private InputAction _mainAction;
    private InputAction _secondAction;

    // Un cooldown por mano (índice 0 = Hand1, 1 = Hand2). Un arma de dos manos usa el 0 para ambos clicks.
    private readonly float[] _localNext = new float[2];
    private readonly float[] _serverNext = new float[2];

    // Buffers que se reutilizan en cada ataque (se los pasamos a weapon.Use para no generar basura)
    private readonly Collider[] _hitBuffer = new Collider[32];
    private readonly HashSet<IDamageable> _alreadyHit = new HashSet<IDamageable>();

    // ---------------------------------------------------------------- Ciclo de vida

    public override void OnNetworkSpawn()
    {
        _inventory = GetComponent<PlayerInventory>();
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<Health>();

        // Todos los peers necesitan el pool registrado para poder recibir proyectiles
        ProjectilePool.RegisterAll(_inventory.Catalog);

        if (!IsOwner) return;

        _lookToMause = GetComponent<LookToMause>();
        _playerInput = GetComponent<PlayerInput>();
        FindActions();
    }

    private void FindActions()
    {
        if (_playerInput == null || _playerInput.actions == null) return;

        _mainAction = _playerInput.actions.FindAction(_mainAttackAction, false);
        _secondAction = _playerInput.actions.FindAction(_secondAttackAction, false);

        if (_mainAction == null) Debug.LogError($"[PlayerCombat] No existe la acción '{_mainAttackAction}'.", this);
        if (_secondAction == null) Debug.LogError($"[PlayerCombat] No existe la acción '{_secondAttackAction}'.", this);
    }

    /// <summary>TODO: conectar con la fase de turno (no atacar fuera de combate, ni en planificación).</summary>
    public bool CanAttack() => true;

    // ---------------------------------------------------------------- Cliente dueño

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;

        if (_mainAction == null || _secondAction == null) FindActions();

        TryAttack(EquipSlot.Hand1, 0, _mainAction);
        TryAttack(EquipSlot.Hand2, 1, _secondAction);
    }

    private void TryAttack(EquipSlot hand, int handIndex, InputAction action)
    {
        if (action == null || !action.IsPressed()) return;
        if (!CanAttack() || _health.IsDead) return;
        if (!TryResolveWeapon(hand, out WeaponDataBase weapon, out int cooldownIndex)) return;
        if (Time.time < _localNext[cooldownIndex]) return;

        _localNext[cooldownIndex] = Time.time + GetAttackInterval(weapon);
        AttackRpc(handIndex, _lookToMause.GetOrientation());
    }

    // ---------------------------------------------------------------- Servidor

    [Rpc(SendTo.Server)]
    private void AttackRpc(int handIndex, Vector3 aimDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;   // solo el dueño
        if (handIndex < 0 || handIndex > 1) return;

        ServerAttack(handIndex == 0 ? EquipSlot.Hand1 : EquipSlot.Hand2, aimDirection);
    }

    private void ServerAttack(EquipSlot hand, Vector3 aimDirection)
    {
        if (!CanAttack() || _health.IsDead) return;
        if (!TryResolveWeapon(hand, out WeaponDataBase weapon, out int cooldownIndex)) return;

        float now = Time.time;
        if (now < _serverNext[cooldownIndex] - _cooldownTolerance) return;   // demasiado pronto
        _serverNext[cooldownIndex] = now + GetAttackInterval(weapon);

        // No confiamos en lo que manda el cliente: dirección horizontal y normalizada
        aimDirection.y = 0f;
        Vector3 dir = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : transform.forward;

        var context = new WeaponDataBase.AttackContext
        {
            Owner = gameObject,
            AtackOrigin = transform.position + Vector3.up * _attackOriginHeight,
            AtackDirection = dir,
            Damage = _stats.GetStat(StatType.Damage) * weapon.damageMultiplier
        };

        if (_debugLogs)
            Debug.Log($"[Combat] {name} {hand} {weapon.name} daño={context.Damage:F2} intervalo={GetAttackInterval(weapon):F2}s");

        weapon.Use(context, _hitBuffer, _alreadyHit, _enemyMask);
    }

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
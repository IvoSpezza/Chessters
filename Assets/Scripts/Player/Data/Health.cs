using System;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour, IDamageable
{
    [Header("World Health UI")]
    [SerializeField, Min(0.01f)] private float _worldHealthBarWidth = 200f;
    [SerializeField, Min(0.01f)] private float _worldHealthBarHeight = 24f;
    [SerializeField, Min(0f)] private float _worldHealthBarOffset = 6f;

    private readonly NetworkVariable<float> _actualHealth = new NetworkVariable<float>();
    private Stats _stats;
    private float _lastMaxHealth;

    public float CurrentHealth => _actualHealth.Value;
    public float MaxHealth => _stats != null ? _stats.GetStat(StatType.MaxHealth) : 0f;
    public bool IsDead => _actualHealth.Value <= 0f;

    // para la ui (vida actual, vida maxima) \\
    public event Action<float, float> OnHealthChanged; 
    public event Action<Health> OnDied;

    public override void OnNetworkSpawn()
    {
        _stats = GetComponent<Stats>();

        _actualHealth.OnValueChanged += HandleHealthChanged;
        if (_stats != null) _stats.OnStatChanged += HandleStatChanged;

        OnDied += OnDead; //TEST

        _lastMaxHealth = MaxHealth;
        if (IsServer) _actualHealth.Value = _lastMaxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        bool isPlayerBody = TryGetComponent(out PlayerInventory _);
        if (IsOwner && isPlayerBody && UiInventoryManager.Instance != null) UiInventoryManager.Instance.SetUpHealth(this);

        // La UI sobre la cabeza es local a cada peer. Se omite para el jugador
        // local, que ya tiene su barra fija en la interfaz.
        bool isLocalPlayer = IsOwner && isPlayerBody;
        if (!isLocalPlayer)
        {
            WorldHealthBar worldBar = GetComponent<WorldHealthBar>();
            if (worldBar == null) worldBar = gameObject.AddComponent<WorldHealthBar>();
            worldBar.SetUp(this, _worldHealthBarWidth, _worldHealthBarHeight, _worldHealthBarOffset, isPlayerBody);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && TryGetComponent(out PlayerInventory _) && UiInventoryManager.Instance != null) UiInventoryManager.Instance.ClearHealth();
        WorldHealthBar worldBar = GetComponent<WorldHealthBar>();
        if (worldBar != null) worldBar.Clear();
        OnDied -= OnDead;

        _actualHealth.OnValueChanged -= HandleHealthChanged;
        if (_stats != null) _stats.OnStatChanged -= HandleStatChanged;
    }

    //SERVER ONLY\\
    public void TakeDamage(float damage)
    {
        Debug.Log("SentiElToke");
        if (!IsServer || IsDead || damage <= 0) return;
        Debug.Log(" y perdi vida");
        _actualHealth.Value = Mathf.Max(0f, _actualHealth.Value - damage);
    }

    public void Heal(float amount)
    {
        if (!IsServer || IsDead || amount <= 0f) return;
        _actualHealth.Value = Mathf.Min(MaxHealth, _actualHealth.Value + amount);
    }

    /// <summary>
    /// Cura total instantanea (sala de descanso entre combates, resurreccion al bajar de piso).
    /// A diferencia de Heal, tambien funciona si el jugador esta muerto.
    /// </summary>
    public void RestoreFullHealth()
    {
        if (!IsServer) return;
        _actualHealth.Value = MaxHealth;
    }

    /// <summary>Restaura un porcentaje de la vida maxima; tambien puede levantar a un jugador caido.</summary>
    public void RestoreHealthPercent(float maxHealthPercent)
    {
        if (!IsServer || maxHealthPercent <= 0f) return;
        _actualHealth.Value = Mathf.Min(MaxHealth, _actualHealth.Value + MaxHealth * maxHealthPercent);
    }

    //Reacciones
    private void HandleHealthChanged(float previous, float current)
    {
        Debug.Log($"[Health] {name} IsOwner={IsOwner} {previous}/{MaxHealth} -> {current}/{MaxHealth}");
        OnHealthChanged?.Invoke(current, MaxHealth);
        if (previous > 0f && current <= 0f) OnDied?.Invoke(this);
    }

    private void HandleStatChanged(StatType type)
    {
        if (type != StatType.MaxHealth) return;

        // Mantiene la misma proporcion de vida cuando cambia el maximo por nivel
        // o por equipo. Asi no se pierde vida por cambios intermedios de slots.
        float newMaxHealth = MaxHealth;
        if (IsServer && _lastMaxHealth > 0f && !Mathf.Approximately(_lastMaxHealth, newMaxHealth))
        {
            float healthRatio = Mathf.Clamp01(_actualHealth.Value / _lastMaxHealth);
            _actualHealth.Value = healthRatio * newMaxHealth;
        }
        _lastMaxHealth = newMaxHealth;

        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth); // la barra cambia de proporcion
    }   

    [Rpc(SendTo.Server)]
    public void TestDamageRpc(float damage)
    {
        TakeDamage(damage);
    }

    private void OnDead(Health deadMonster)
    {
        Debug.Log($"Murio{deadMonster.gameObject}, vida restante: {_actualHealth.Value}");
    }
}

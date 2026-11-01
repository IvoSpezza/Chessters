using System;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour, IDamageable
{
    private readonly NetworkVariable<float> _actualHealth = new NetworkVariable<float>();
    private Stats _stats;

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
        _stats.OnStatChanged += HandleStatChanged;

        OnDied += OnDead; //TEST

        if (IsServer) _actualHealth.Value = MaxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        if (IsOwner && TryGetComponent(out PlayerInventory _) && UiInventoryManager.Instance != null) UiInventoryManager.Instance.SetUpHealth(this);        
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && TryGetComponent(out PlayerInventory _) && UiInventoryManager.Instance != null) UiInventoryManager.Instance.ClearHealth();
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

        // Subir la vida maxima solo amplia la barra (la vida actual no cambia).
        // Bajarla (te sacas un casco) recorta la vida actual si quedo por encima del nuevo maximo.
        if (IsServer && _actualHealth.Value > MaxHealth)
            _actualHealth.Value = MaxHealth;

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
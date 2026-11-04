using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// IDs de origen de los modifiers. Cada "fuente" se puede quitar entera con RemoveModifiersFromSource.
/// Equipment y LevelUp son fijos. Buffs, ayudas y sabotajes futuros: usar valores >= 100.
/// </summary>
public static class StatSource
{
    public const int Equipment = 1;
    public const int LevelUp = 2;
    public const int Curse = 100;
    public const int Combat = 101;
}

public class Stats : NetworkBehaviour
{
    [SerializeField] private float _maxHealth = 50f;
    [SerializeField] private float _moveSpeed = 10;
    [SerializeField] private float _damage = 5f;
    [SerializeField] private float _atackSpeed = 1f; //ataques por segundo base

    [SerializeField] private StatsPerLevel _perLevel;
    private int _level = 0;

    public int Level => _level;
    public void SetLevel(int level)
    {
        level = Mathf.Max(0, level);
        if (_level == level) return;
        _level = level;

        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            if (!_finalStats.TryGetValue(type, out float previous)) continue;
            Recalculate(type);
            if (!Mathf.Approximately(previous, _finalStats[type])) OnStatChanged?.Invoke(type);
        }
    }

    private readonly List<StatModifier> _modifiers = new List<StatModifier>();
    private readonly Dictionary<StatType, float> _finalStats = new Dictionary<StatType, float>();

    // Modo batch: mientras esta abierto no se recalcula ni se notifica nada.
    private readonly HashSet<StatType> _dirtyStats = new HashSet<StatType>();
    private int _batchDepth;
    private NetworkList<StatModifier> _combatModifiers;
    private bool _combatModifiersDirty;
    private readonly List<StatModifier> _combatModifierBuffer = new List<StatModifier>();

    public Action<StatType> OnStatChanged;

    private void Awake()
    {
        _combatModifiers = new NetworkList<StatModifier>(null, NetworkVariableReadPermission.Everyone);
    }

    public override void OnNetworkSpawn()
    {
        _combatModifiers.OnListChanged += HandleCombatModifiersChanged;
        _combatModifiersDirty = true;
    }

    public override void OnNetworkDespawn()
    {
        _combatModifiers.OnListChanged -= HandleCombatModifiersChanged;
    }

    private void LateUpdate()
    {
        if (!_combatModifiersDirty || !IsSpawned) return;
        RefreshCombatModifiers();
    }

    private void RefreshCombatModifiers()
    {
        _combatModifiersDirty = false;
        _combatModifierBuffer.Clear();
        for (int i = 0; i < _combatModifiers.Count; i++)
            _combatModifierBuffer.Add(_combatModifiers[i]);
        SetModifiersFromSource(StatSource.Combat, _combatModifierBuffer);
    }

    private void HandleCombatModifiersChanged(NetworkListEvent<StatModifier> changeEvent) => _combatModifiersDirty = true;

    /// <summary>Agrega los efectos temporales del combate en el servidor; la NetworkList los replica a todos.</summary>
    public bool ServerAddCombatModifiers(IReadOnlyList<StatModifier> modifiers)
    {
        if (!IsServer || modifiers == null || modifiers.Count == 0) return false;

        for (int i = 0; i < modifiers.Count; i++)
        {
            StatModifier modifier = modifiers[i];
            if (!Enum.IsDefined(typeof(StatType), modifier.Type) ||
                !Enum.IsDefined(typeof(ModifierType), modifier.Modifier) ||
                float.IsNaN(modifier.Value) || float.IsInfinity(modifier.Value))
                return false;
        }

        for (int i = 0; i < modifiers.Count; i++)
        {
            StatModifier modifier = modifiers[i];
            modifier.Id = StatSource.Combat;
            _combatModifiers.Add(modifier);
        }
        RefreshCombatModifiers();
        return true;
    }

    public void ServerClearCombatModifiers()
    {
        if (!IsServer || _combatModifiers.Count == 0) return;
        _combatModifiers.Clear();
        RefreshCombatModifiers();
    }

    public float GetStat(StatType type)
    {
        if (!_finalStats.TryGetValue(type, out float value))
        {
            Recalculate(type);          // primera consulta: calcula con la base
            value = _finalStats[type];
        }
        return value;
    }

    // ---------- Modifiers ----------

    public void AddModifier(StatModifier modifier, int sourceId)
    {
        modifier.Id = sourceId;         // es una copia, no toca el original del ItemData
        _modifiers.Add(modifier);
        MarkChanged(modifier.Type);
    }

    public void RemoveModifiersFromSource(int sourceId)
    {
        var changed = new HashSet<StatType>();
        _modifiers.RemoveAll(m =>
        {
            if (m.Id != sourceId) return false;
            changed.Add(m.Type);
            return true;
        });

        foreach (StatType t in changed) MarkChanged(t);
    }

    /// <summary>
    /// Reemplaza todos los modifiers de una fuente por una lista nueva, notificando UNA sola vez
    /// por stat y con el valor final (evita valores intermedios, ej: MaxHealth bajando un instante).
    /// </summary>
    public void SetModifiersFromSource(int sourceId, IEnumerable<StatModifier> newModifiers)
    {
        BeginBatch();
        RemoveModifiersFromSource(sourceId);
        foreach (StatModifier m in newModifiers) AddModifier(m, sourceId);
        EndBatch();
    }

    // ---------- Batch ----------

    public void BeginBatch() => _batchDepth++;

    public void EndBatch()
    {
        if (_batchDepth <= 0)
        {
            Debug.LogWarning("[PlayerStats] EndBatch sin BeginBatch");
            return;
        }

        _batchDepth--;
        if (_batchDepth > 0 || _dirtyStats.Count == 0) return;

        var toNotify = new List<StatType>(_dirtyStats);
        _dirtyStats.Clear();

        foreach (StatType type in toNotify)
        {
            bool hadValue = _finalStats.TryGetValue(type, out float oldValue);
            Recalculate(type);
            // Solo avisamos si el valor final realmente cambio
            if (!hadValue || !Mathf.Approximately(oldValue, _finalStats[type]))
                OnStatChanged?.Invoke(type);
        }
    }

    private void MarkChanged(StatType type)
    {
        if (_batchDepth > 0)
        {
            _dirtyStats.Add(type);
            return;
        }

        Recalculate(type);
        OnStatChanged?.Invoke(type);
    }

    // ---------- Calculo ----------

    private void Recalculate(StatType type)
    {
        float flat = 0f, percent = 0f;
        foreach (StatModifier m in _modifiers)
        {
            if (m.Type != type) continue;
            if (m.Modifier == ModifierType.Flat) flat += m.Value;
            else percent += m.Value;  
        }
        _finalStats[type] = Mathf.Max(0f, (GetBase(type) + flat) * (1f + percent));
    }

    private float GetBase(StatType type)
    {
        return type switch
        {
            StatType.MoveSpeed => _moveSpeed + _perLevel.MoveSpeed * _level,
            StatType.AttackSpeed => _atackSpeed + _perLevel.AttackSpeed * _level,
            StatType.MaxHealth => _maxHealth + _perLevel.MaxHealth * _level,
            StatType.Damage => _damage + _perLevel.Damage * _level,
            _ => 0f,
        };
    }
}

public enum StatType
{
    MaxHealth = 0,
    MoveSpeed = 1,
    Damage = 2,
    AttackSpeed = 3
}
public enum ModifierType { Flat, Percent }

[System.Serializable]
public struct StatModifier : INetworkSerializable, IEquatable<StatModifier>
{
    public StatType Type;
    public ModifierType Modifier;
    public float Value;
    [HideInInspector] public int Id;

    public bool Equals(StatModifier other) => Type == other.Type && Modifier == other.Modifier &&
        Value.Equals(other.Value) && Id == other.Id;

    public override bool Equals(object obj) => obj is StatModifier other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Type;
            hash = (hash * 397) ^ (int)Modifier;
            hash = (hash * 397) ^ Value.GetHashCode();
            return (hash * 397) ^ Id;
        }
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref Modifier);
        serializer.SerializeValue(ref Value);
        serializer.SerializeValue(ref Id);
    }
}

[System.Serializable]
public struct StatsPerLevel
{
    public float MaxHealth;
    public float Damage;
    public float AttackSpeed;
    public float MoveSpeed;
}

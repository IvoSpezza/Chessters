using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IDs de origen de los modifiers. Cada "fuente" se puede quitar entera con RemoveModifiersFromSource.
/// Equipment y LevelUp son fijos. Buffs, ayudas y sabotajes futuros: usar valores >= 100.
/// </summary>
public static class StatSource
{
    public const int Equipment = 1;
    public const int LevelUp = 2;
}

public class PlayerStats : MonoBehaviour
{
    [SerializeField] private float _maxHealth = 50f;
    [SerializeField] private float _moveSpeed = 10;
    [SerializeField] private float _damage = 5f;
    [SerializeField] private float _atackSpeed = 1f; //ataques por segundo base

    private readonly List<StatModifier> _modifiers = new List<StatModifier>();
    private readonly Dictionary<StatType, float> _finalStats = new Dictionary<StatType, float>();

    // Modo batch: mientras esta abierto no se recalcula ni se notifica nada.
    private readonly HashSet<StatType> _dirtyStats = new HashSet<StatType>();
    private int _batchDepth;

    public Action<StatType> OnStatChanged;

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
            StatType.MoveSpeed => _moveSpeed,
            StatType.AttackSpeed => _atackSpeed,
            StatType.MaxHealth => _maxHealth,
            StatType.Damage => _damage,
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
public struct StatModifier
{
    public StatType Type;
    public ModifierType Modifier;
    public float Value;
    [HideInInspector] public int Id;
}
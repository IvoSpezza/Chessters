
using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [SerializeField] private float _maxHealth = 50f;
    [SerializeField] private float _moveSpeed = 10;
    [SerializeField] private float _damage = 5f;
    [SerializeField] private float _atackSpeed = 0.2f; //ataques por segundo

    private List<StatModifier> _modifiers = new List<StatModifier>();

    public Action<StatType> OnStatChanged;

    public Dictionary<StatType, float> _finalStats = new Dictionary<StatType, float>();

    private void OnEnable()
    {
        OnStatChanged += UpdateStats;        
    }
    private void OnDisable()
    {
        OnStatChanged -= UpdateStats;
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

    public void UpdateStats(StatType type)
    {
        float flat = 0f, percent = 0f;
        foreach (StatModifier m in _modifiers)
        {
            if (m.Type != type) continue;
            if (m.Modifier == ModifierType.Flat) flat += m.Value;
            else percent += m.Value;   // 0.1 = +10%
        }

        if (_finalStats.ContainsKey(type)) _finalStats[type] = Mathf.Max(0f, (GetBase(type) + flat) * (1f + percent));
        else _finalStats.Add(type, Mathf.Max(0f, (GetBase(type) + flat) * (1f + percent)));        
    }

    public void AddModifier(StatModifier modifier, int sourceId)
    {
        modifier.Id = sourceId;         // es una copia, no toca el original del ItemData
        _modifiers.Add(modifier);
        Recalculate(modifier.Type);
        OnStatChanged?.Invoke(modifier.Type);
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

        foreach (StatType t in changed)
        {
            Recalculate(t);
            OnStatChanged?.Invoke(t);
        }
    }

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
public enum ModifierType {Flat,Percent}

[System.Serializable]
public struct StatModifier
{
    public StatType Type;
    public ModifierType Modifier;
    public float Value;
    [HideInInspector]public int Id;
}
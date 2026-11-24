using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Estado de un combate en curso. Solo servidor, no es un componente.
/// Nace al abrir la sala y se descarta al terminar la resolucion. La sala sigue existiendo.
/// </summary>
public class CombatEncounter
{
    public BaseCombatData Data { get; }
    public RoomLayout Layout { get; }

    /// <summary>Quienes pelean: el dueño del turno y el ayudante que aceptó.</summary>
    public readonly List<ulong> Participants = new List<ulong>();
    public readonly List<NetworkObject> Monsters = new List<NetworkObject>();
    public readonly HashSet<Stats> AffectedStats = new HashSet<Stats>();

    private readonly List<Health> _monsterHealths = new List<Health>();

    public event Action WIN;
    public event Action DEFEAT;

    public CombatEncounter(BaseCombatData data, RoomLayout layout)
    {
        Data = data;
        Layout = layout;        
    }

    public NetworkObject SpawnMonster(GameObject prefab, int spawnIndex)
    {
        if (prefab == null)
        {
            Debug.LogError($"[Combat] {Data.name} no tiene prefab de monstruo asignado.", Data);
            return null;
        }               

        int monsterLevel = Mathf.Max(0, GameManager.Instance.Floor - 1);
        NetworkObject monster = Layout.Spawn(prefab, spawnIndex, spawnedMonster =>
        {
            if (spawnedMonster.TryGetComponent(out Stats stats))
                stats.SetLevel(monsterLevel);
        });
        if (monster == null) return null;

        if (!monster.TryGetComponent(out Health health))
        {
            Debug.LogError($"[Combat] El monstruo {prefab.name} necesita Health.", prefab);
            monster.Despawn(true);
            return null;
        }

        Monsters.Add(monster);
        _monsterHealths.Add(health);
        health.OnDied += MonsterDied;
        return monster;
    }

    private void MonsterDied(Health deadMonster)
    {
        deadMonster.OnDied -= MonsterDied;

        _monsterHealths.Remove(deadMonster);
        if (_monsterHealths.Count <= 0) WIN?.Invoke();
    }    

    public bool AllMonstersDead() => _monsterHealths.Count == 0;

    public void RegisterCombatEffect(Stats stats)
    {
        if (stats != null) AffectedStats.Add(stats);
    }

    public bool AllParticipantsDown()
    {
        // Una caída individual no termina el encuentro: los participantes restantes pueden ganar.
        foreach (ulong id in Participants)
        {
            PlayerSoul soul = PlayerSoul.GetFor(id);
            if (soul != null && soul.TryGetBodyComponent(out Health health) && !health.IsDead) return false;
        }
        return true;
    }

    /// <summary>Despawnea los monstruos. La sala no se toca.</summary>
    public void Cleanup()
    {
        foreach (NetworkObject monster in Monsters)
            if (monster != null && monster.IsSpawned) monster.Despawn(true);
    }
}

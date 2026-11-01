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

    /// <summary>Quienes pelean. Hoy solo el dueño del turno. TODO: ayudantes.</summary>
    public readonly List<ulong> Participants = new List<ulong>();
    public readonly List<NetworkObject> Monsters = new List<NetworkObject>();

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

        NetworkObject monster = Layout.Spawn(prefab, SpawnKind.Monster, spawnIndex);
        if (monster == null) return null;

        if (!monster.TryGetComponent(out Health health))
        {
            Debug.LogError($"[Combat] El monstruo {prefab.name} necesita Health.", prefab);
            monster.Despawn(true);
            return null;
        }

        if(monster.TryGetComponent<Stats>(out Stats monsterStats))
        {
            Debug.Log("levelAplied");
            monsterStats.SetLevel(GameManager.Instance.Floor - 1);
            Debug.Log(monsterStats.Level);
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

    public bool AllParticipantsDown()
    {
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

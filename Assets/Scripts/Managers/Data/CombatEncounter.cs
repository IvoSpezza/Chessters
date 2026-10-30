using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Estado de un combate en curso. Solo servidor, no es un componente.
/// Lo crea el GameManager al patear la puerta y lo descarta al terminar.
/// </summary>
public class CombatEncounter
{
    public BaseCombatData Data { get; }
    public RoomLayout Layout { get; }
    public NetworkObject Room { get; }

    /// <summary>Quienes pelean. Hoy solo el dueño del turno. TODO: ayudantes.</summary>
    public readonly List<ulong> Participants = new List<ulong>();
    /// <summary>Quienes viajaron a la sala (para devolverlos al hub).</summary>
    public readonly List<ulong> Travelers = new List<ulong>();
    public readonly List<NetworkObject> Monsters = new List<NetworkObject>();

    private readonly List<Health> _monsterHealths = new List<Health>();

    public CombatEncounter(BaseCombatData data, RoomLayout layout, NetworkObject room)
    {
        Data = data;
        Layout = layout;
        Room = room;
    }

    public NetworkObject SpawnMonster(GameObject prefab, int spawnIndex)
    {
        if (prefab == null)
        {
            Debug.LogError($"[Combat] {Data.name} no tiene prefab de monstruo asignado.", Data);
            return null;
        }

        GameObject instance = Object.Instantiate(prefab, Layout.GetMonsterSpawn(spawnIndex), Quaternion.identity);
        if (!instance.TryGetComponent(out NetworkObject networkObject) || !instance.TryGetComponent(out Health health))
        {
            Debug.LogError($"[Combat] El monstruo {prefab.name} necesita NetworkObject y Health.", prefab);
            Object.Destroy(instance);
            return null;
        }

        networkObject.Spawn(true);
        Monsters.Add(networkObject);
        _monsterHealths.Add(health);
        return networkObject;
    }

    public bool AllMonstersDead()
    {
        foreach (Health health in _monsterHealths)
            if (health != null && health.IsSpawned && !health.IsDead) return false;
        return true;
    }

    public bool AllParticipantsDown()
    {
        foreach (ulong id in Participants)
        {
            PlayerSoul soul = PlayerSoul.GetFor(id);
            if (soul != null && soul.TryGetBodyComponent(out Health health) && !health.IsDead) return false;
        }
        return true;
    }

    public void Cleanup()
    {
        foreach (NetworkObject monster in Monsters)
            if (monster != null && monster.IsSpawned) monster.Despawn(true);

        if (Room != null && Room.IsSpawned) Room.Despawn(true);
    }
}

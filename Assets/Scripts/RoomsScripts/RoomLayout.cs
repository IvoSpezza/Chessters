using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Va en la raiz del prefab de sala. Define las zonas de la sala:
///  - Llegada: donde aparece todo el grupo al entrar (y donde miran los no combatientes).
///  - Arena: donde se teletransportan los participantes al empezar el combate.
///  - Puntos de aparicion (RoomSpawnPoint): monstruos o cofres, segun como se configure cada punto.
/// </summary>
public class RoomLayout : MonoBehaviour
{
    [SerializeField] private Transform[] _arrivalSpawns;
    [SerializeField] private Transform[] _arenaSpawns;
    [Tooltip("Si queda vacio se toman los RoomSpawnPoint hijos")]
    [SerializeField] private RoomSpawnPoint[] _spawnPoints;

    private void Awake()
    {
        if (_spawnPoints == null || _spawnPoints.Length == 0)
            _spawnPoints = GetComponentsInChildren<RoomSpawnPoint>();
    }

    public Vector3 GetArrivalSpawn(int index) => Pick(_arrivalSpawns, index);
    public Vector3 GetArenaSpawn(int index) => Pick(_arenaSpawns, index);

    public int CountOf(SpawnKind kind)
    {
        int count = 0;
        foreach (RoomSpawnPoint point in _spawnPoints)
            if (point != null && point.kind == kind) count++;
        return count;
    }

    /// <summary>Solo servidor. Instancia el prefab en el punto numero index de ese tipo y lo spawnea en red.</summary>
    public NetworkObject Spawn(GameObject prefab, SpawnKind kind, int index)
    {
        int count = CountOf(kind);
        if (prefab == null || count == 0)
        {
            Debug.LogError($"[RoomLayout] No se puede spawnear {kind}: falta el prefab o la sala no tiene RoomSpawnPoint de ese tipo.", this);
            return null;
        }

        RoomSpawnPoint point = GetPoint(kind, index % count);
        GameObject instance = Instantiate(prefab, point.transform.position, point.transform.rotation);
        if (!instance.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[RoomLayout] El prefab {prefab.name} no tiene NetworkObject.", prefab);
            Destroy(instance);
            return null;
        }

        networkObject.Spawn(true);
        return networkObject;
    }

    private RoomSpawnPoint GetPoint(SpawnKind kind, int indexOfKind)
    {
        int current = 0;
        foreach (RoomSpawnPoint point in _spawnPoints)
        {
            if (point == null || point.kind != kind) continue;
            if (current == indexOfKind) return point;
            current++;
        }
        return null;
    }

    private Vector3 Pick(Transform[] points, int index)
    {
        if (points == null || points.Length == 0) return transform.position;
        return points[index % points.Length].position;
    }
}
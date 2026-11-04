using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Geometria de una sala. Sus grupos contienen transforms de llegada, arena y contenido;
/// el RoomData decide que contenido se coloca en los puntos de contenido.
/// </summary>
public class RoomLayout : MonoBehaviour
{
    [Header("Grupos de puntos (cada hijo directo es un punto)")]
    [Tooltip("Grupo cuyos hijos son posiciones de llegada del grupo.")]
    [SerializeField] private Transform _arrivalPointsRoot;
    [Tooltip("Grupo cuyos hijos son posiciones para jugadores en la arena y el duelo final.")]
    [SerializeField] private Transform _arenaPointsRoot;
    [Tooltip("Grupo cuyos hijos son posiciones de contenido: monstruos o cofres según el RoomData.")]
    [SerializeField] private Transform _contentPointsRoot;

    private Transform[] _arrivalPoints = Array.Empty<Transform>();
    private Transform[] _arenaPoints = Array.Empty<Transform>();
    private Transform[] _contentPoints = Array.Empty<Transform>();

    public int ArenaSpawnCount => _arenaPoints.Length;
    public int ContentSpawnCount => _contentPoints.Length;

    private void Awake()
    {
        _arrivalPoints = GetChildren(_arrivalPointsRoot);
        _arenaPoints = GetChildren(_arenaPointsRoot);
        _contentPoints = GetChildren(_contentPointsRoot);
    }

    public Vector3 GetArrivalSpawn(int index) => Pick(_arrivalPoints, index);
    public Vector3 GetArenaSpawn(int index) => Pick(_arenaPoints, index);

    /// <summary>Solo servidor. Instancia contenido en el punto correspondiente y lo spawnea en red.</summary>
    public NetworkObject Spawn(GameObject prefab, int index, Action<NetworkObject> beforeNetworkSpawn = null)
    {
        if (prefab == null || _contentPoints.Length == 0)
        {
            Debug.LogError($"[RoomLayout] No se puede spawnear {prefab?.name ?? "contenido"}: falta el prefab o la sala no tiene puntos en Content.", this);
            return null;
        }

        int pointIndex = ((index % _contentPoints.Length) + _contentPoints.Length) % _contentPoints.Length;
        Transform point = _contentPoints[pointIndex];
        GameObject instance = Instantiate(prefab, point.position, point.rotation);
        if (!instance.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[RoomLayout] El prefab {prefab.name} no tiene NetworkObject.", prefab);
            Destroy(instance);
            return null;
        }

        // Configurar stats antes de OnNetworkSpawn para inicializar vida y datos correctos.
        beforeNetworkSpawn?.Invoke(networkObject);
        networkObject.Spawn(true);
        return networkObject;
    }

    private static Transform[] GetChildren(Transform root)
    {
        if (root == null || root.childCount == 0) return Array.Empty<Transform>();

        var points = new Transform[root.childCount];
        for (int i = 0; i < root.childCount; i++)
            points[i] = root.GetChild(i);
        return points;
    }

    private Vector3 Pick(Transform[] points, int index)
    {
        if (points == null || points.Length == 0) return transform.position;
        int wrappedIndex = ((index % points.Length) + points.Length) % points.Length;
        return points[wrappedIndex].position;
    }
}

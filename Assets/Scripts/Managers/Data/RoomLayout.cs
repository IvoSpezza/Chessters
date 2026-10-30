using UnityEngine;

/// <summary>
/// Va en la raiz del prefab de sala. Define donde aparecen jugadores y monstruos.
/// No usa SpawnPoint a proposito: SessionManager busca esos en toda la escena para el hub.
/// </summary>
public class RoomLayout : MonoBehaviour
{
    [SerializeField] private Transform[] _playerSpawns;
    [SerializeField] private Transform[] _monsterSpawns;

    public Vector3 GetPlayerSpawn(int index) => Pick(_playerSpawns, index);
    public Vector3 GetMonsterSpawn(int index) => Pick(_monsterSpawns, index);

    private Vector3 Pick(Transform[] points, int index)
    {
        if (points == null || points.Length == 0) return transform.position;
        return points[index % points.Length].position;
    }
}

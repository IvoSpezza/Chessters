using UnityEngine;

public enum SpawnKind
{
    Monster,
    Chest
}

/// <summary>
/// Punto de aparicion dentro del prefab de sala. Segun su tipo, ahi aparecen monstruos o cofres.
/// Distinto de SpawnPoint, que es solo para el arranque de los jugadores en la escena Game.
/// </summary>
public class RoomSpawnPoint : MonoBehaviour
{
    [SerializeField] private SpawnKind _kind;

    public SpawnKind kind => _kind;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = _kind == SpawnKind.Monster ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
#endif
}

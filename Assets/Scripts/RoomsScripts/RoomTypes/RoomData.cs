using UnityEngine;

public enum RoomType
{
    Combat,
    Treasure
    // TODO: Rest, Trap, Shop
}

/// <summary>Base de las salas. Solo datos. Cada tipo de sala agrega su propio contenido (CombatRoomData, TreasureRoomData).</summary>
public abstract class RoomData : ScriptableObject
{
    [SerializeField] private int _roomID;
    [SerializeField] private string _roomName;
    [Tooltip("Prefab de red: NetworkObject + RoomLayout. Sin NetworkObject hijos.")]
    [SerializeField] private GameObject _roomPrefab;

    public int roomID => _roomID;
    public string roomName => _roomName;
    public GameObject roomPrefab => _roomPrefab;

    public abstract RoomType type { get; }
}
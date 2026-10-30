using System.Collections.Generic;
using UnityEngine;

public enum RoomType
{
    Combat
    // TODO etapa 3: Treasure, Curse, Trap
}

/// <summary>Sala del mazmorra. Solo datos: el prefab define el escenario y los combates posibles el contenido.</summary>
[CreateAssetMenu(fileName = "New Room", menuName = "New Rooms/Room")]
public class RoomData : ScriptableObject
{
    [SerializeField] private int _roomID;
    [SerializeField] private string _roomName;
    [SerializeField] private RoomType _type = RoomType.Combat;
    [Tooltip("Prefab de red: NetworkObject + RoomLayout. Sin NetworkObject hijos.")]
    [SerializeField] private GameObject _roomPrefab;
    [SerializeField] private List<BaseCombatData> _possibleCombats = new List<BaseCombatData>();

    public int roomID => _roomID;
    public string roomName => _roomName;
    public RoomType type => _type;
    public GameObject roomPrefab => _roomPrefab;

    public BaseCombatData GetRandomCombat()
    {
        return _possibleCombats.Count > 0 ? _possibleCombats[Random.Range(0, _possibleCombats.Count)] : null;
    }
}

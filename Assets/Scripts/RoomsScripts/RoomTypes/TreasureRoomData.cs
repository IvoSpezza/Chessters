using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Treasure Room", menuName = "New Rooms/Treasure Room")]
public class TreasureRoomData : RoomData
{
    [Tooltip("Prefab de red del cofre: NetworkObject + Chest")]
    [SerializeField] private GameObject _chestPrefab;
    [Tooltip("Probabilidad de que el cofre sea de maldicion (0 a 1)")]
    [SerializeField, Range(0f, 1f)] private float _curseChance = 0.25f;
    [Tooltip("Items que da un cofre de tesoro")]
    [SerializeField, Min(1)] private int _treasureItemCount = 2;
    [SerializeField] private List<CurseData> _possibleCurses = new List<CurseData>();

    public override RoomType type => RoomType.Treasure;

    public GameObject chestPrefab => _chestPrefab;
    public int treasureItemCount => _treasureItemCount;

    /// <summary>Sortea si el cofre es de maldicion. Sin maldiciones configuradas siempre es de tesoro.</summary>
    public bool RollCurse() => _possibleCurses.Count > 0 && Random.value < _curseChance;

    public CurseData GetRandomCurse()
    {
        return _possibleCurses.Count > 0 ? _possibleCurses[Random.Range(0, _possibleCurses.Count)] : null;
    }
}

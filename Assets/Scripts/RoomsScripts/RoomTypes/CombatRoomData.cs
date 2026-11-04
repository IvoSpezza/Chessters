using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Combat Room", menuName = "New Rooms/Combat Room")]
public class CombatRoomData : RoomData
{
    [SerializeField] private List<BaseCombatData> _possibleCombats = new List<BaseCombatData>();

    public override RoomType type => RoomType.Combat;

    public BaseCombatData GetRandomCombat()
    {
        return _possibleCombats.Count > 0 ? _possibleCombats[Random.Range(0, _possibleCombats.Count)] : null;
    }
}

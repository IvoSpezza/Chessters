using UnityEngine;

/// <summary>Combate simple: se gana al matar a todos los monstruos.</summary>
[CreateAssetMenu(fileName = "New Kill Combat", menuName = "New Combats/Kill")]
public class KillCombatData : BaseCombatData
{
    [SerializeField, Min(1)] private int _monsterCount = 1;

    public override void ServerSpawn(CombatEncounter encounter)
    {
        for (int i = 0; i < _monsterCount; i++)
            encounter.SpawnMonster(monsterToKill, i);
    }

    public override bool IsVictory(CombatEncounter encounter) => encounter.AllMonstersDead();
}

using UnityEngine;

/// <summary>
/// Base de los combates. Es solo DATOS y reglas (un SO es un asset compartido): no guardar estado en campos.
/// El estado de un combate en curso vive en CombatEncounter (solo servidor).
/// Por red viaja unicamente el combatID; la UI lo resuelve con RoomCatalog.
/// </summary>
public abstract class BaseCombatData : ScriptableObject
{
    [Header("Presentacion")]
    [SerializeField] private int _combatID;
    [SerializeField] private string _combatName;
    [TextArea][SerializeField] private string _description;
    [SerializeField] private Sprite _icon;

    [Header("Combate")]
    [Tooltip("Prefab del monstruo (NetworkObject + Health + PlayerStats, capa Monster)")]
    [SerializeField] private GameObject _monsterToKill;
    [SerializeField, Min(0)] private int _treasureAmount = 1;
    [SerializeField, Min(0)] private int _levelsReward = 1;

    [Header("Derrota")]
    [SerializeField] private BadThing _badThing = new BadThing();
    [SerializeField] private EscapeCondition _escapeCondition = new EscapeCondition();

    public int combatID => _combatID;
    public string combatName => _combatName;
    public string description => _description;
    public Sprite icon => _icon;
    public GameObject monsterToKill => _monsterToKill;
    public int treasureAmount => _treasureAmount;
    public int levelsReward => _levelsReward;
    public BadThing badThing => _badThing;
    public EscapeCondition escapeCondition => _escapeCondition;

    /// <summary>Solo servidor. Crea los monstruos del combate.</summary>
    public abstract void ServerSpawn(CombatEncounter encounter);

    public abstract bool IsVictory(CombatEncounter encounter);

    /// <summary>Por defecto se pierde cuando todos los participantes quedan sin vida.</summary>
    public virtual bool IsDefeat(CombatEncounter encounter) => encounter.AllParticipantsDown();
}

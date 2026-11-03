/// <summary>Combate en tiempo real. Los participantes pasan a la arena; el resto mira desde la zona de llegada.</summary>
public class CombatState : GameState
{
    public CombatState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.Resolution, GamePhase.ChoosingDoor, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);
    private CombatEncounter _thisEnconter;
    public override void Enter()
    {
        _thisEnconter = _game.Encounter;
        _game.ServerMoveParticipantsToArena();
        _thisEnconter.WIN += Win;
    }

    public override void Update()
    {
        CombatEncounter encounter = _game.Encounter;
        if (encounter == null) return;


        if (encounter.Data.IsDefeat(encounter)) _game.ServerResolveCombat(false);

    }

    public override void Exit()
    {
        _thisEnconter.WIN -= Win;
    }

    private void Win()
    {
        _game.ServerResolveCombat(true);
    }
    private void Defeat()
    {
        _game.ServerResolveCombat(false);
    }
}

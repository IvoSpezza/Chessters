/// <summary>Resultado del combate. Al terminar, los participantes vuelven a la zona de llegada y pasa el turno.</summary>
public class ResolutionState : GameState
{
    public ResolutionState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.ChoosingDoor, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Enter()
    {
        _game.ServerApplyCombatResolution();
    }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerEndCombat();
    }
}

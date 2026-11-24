/// <summary>Desempate entre quienes alcanzaron el nivel objetivo al final del mismo piso.</summary>
public class FinalDuelState : GameState
{
    public FinalDuelState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.GameOver);

    public override void Enter() => _game.ServerMoveFinalDuelistsToArena();

    public override void Update() => _game.ServerCheckFinalDuel();
}

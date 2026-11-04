/// <summary>Preparacion grupal en la zona inicial (equiparse con el loot). Con tiempo.</summary>
public class PreparationState : GameState
{
    public PreparationState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.ChoosingDoor, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerBeginNextTurn();
    }
}

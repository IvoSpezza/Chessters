/// <summary>Espera a que todas las almas tengan cuerpo. Despues reparte el loot inicial.</summary>
public class WaitingForBodiesState : GameState
{
    public WaitingForBodiesState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.Preparation, GamePhase.ChoosingDoor);

    public override void Update()
    {
        if (!_game.ServerAllBodiesReady()) return;

        _game.ServerStartGame();   // reinicia el piso y reparte el loot

        if (_game.PreparationSeconds > 0f) TransitionTo(GamePhase.Preparation, _game.PreparationSeconds);
        else _game.ServerBeginNextTurn();
    }
}

/// <summary>Espera a que todas las almas tengan cuerpo. Despues reparte el loot inicial.</summary>
public class WaitingForBodiesState : GameState
{
    public WaitingForBodiesState(GameManager game) : base(game) { }

    public override void Update()
    {
        if (!_game.ServerAllBodiesReady()) return;

        _game.ServerStartGame();   // reinicia el piso y reparte el loot

        if (_game.PreparationSeconds > 0f) _game.ChangePhase(GamePhase.Preparation, _game.PreparationSeconds);
        else _game.ServerBeginNextTurn();
    }
}

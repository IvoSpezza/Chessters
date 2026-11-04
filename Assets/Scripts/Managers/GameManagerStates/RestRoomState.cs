/// <summary>
/// Descanso en el Hub: el grupo vuelve, recupera toda la vida y luego inicia el piso siguiente.
/// </summary>
public class RestRoomState : GameState
{
    public RestRoomState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.ChoosingDoor, GamePhase.RestRoom,
        GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Enter()
    {
        _game.ServerEnterRestRoom();
        if (_game.RestSeconds <= 0f) _game.ServerStartNextFloor();
    }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerStartNextFloor();
    }
}

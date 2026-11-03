/// <summary>El jugador de turno elige patear la puerta (KickDoorRpc). Los demas pueden seguir equipandose.</summary>
public class ChoosingDoorState : GameState
{
    public ChoosingDoorState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.ChoosingDoor, GamePhase.EnteringRoom, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Update()
    {
        // [TEMP] si hay limite de tiempo y se acaba, patea la puerta automaticamente. TODO: elegir una puerta al azar
        if (_game.PhaseTimeUp) _game.ServerKickDoor();
    }
}

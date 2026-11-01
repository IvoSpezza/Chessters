/// <summary>
/// El jugador de turno decide que hacer con el cofre (la decision la maneja el propio Chest, ver Chest.OpenRpc y Chest.PassRpc).
/// Si se acaba el tiempo, el cofre se ignora y pasa el turno.
/// TODO: ofrecerlo a otro jugador al azar si el de turno lo rechaza; si nadie lo quiere, vuelve al dueño del turno.
/// </summary>
public class ChestDecisionState : GameState
{
    public ChestDecisionState(GameManager game) : base(game) { }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerOnChestResolved();
    }
}

/// <summary>
/// El grupo ya fue teletransportado a la sala nueva. Se espera un momento a que todos
/// la tengan cargada y recien ahi se destruye la sala anterior. Despues sigue segun el tipo de sala.
/// </summary>
public class EnteringRoomState : GameState
{
    public EnteringRoomState(GameManager game) : base(game) { }

    public override void Update()
    {
        if (!_game.PhaseTimeUp) return;

        _game.ServerDespawnPreviousRoom();

        if (_game.CurrentRoomType == RoomType.Treasure) _game.ChangePhase(GamePhase.ChestDecision, _game.ChestDecisionSeconds);
        else _game.ChangePhase(GamePhase.CombatPlanning, _game.PlanningSeconds);
    }
}
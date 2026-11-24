/// <summary>
/// El grupo ya fue teletransportado a la sala nueva. Se espera un momento a que todos
/// la tengan cargada y recien ahi se destruye la sala anterior. Despues sigue segun el tipo de sala.
/// </summary>
public class EnteringRoomState : GameState
{
    public EnteringRoomState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.CombatPlanning, GamePhase.ChestDecision,
        GamePhase.ChoosingDoor, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Update()
    {
        if (!_game.PhaseTimeUp) return;

        _game.ServerDespawnPreviousRoom();

        if (_game.CurrentRoomType == RoomType.Treasure) TransitionTo(GamePhase.ChestDecision, _game.ChestDecisionSeconds);
        else TransitionTo(GamePhase.CombatPlanning, _game.PlanningSeconds);
    }
}

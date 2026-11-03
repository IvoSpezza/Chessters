/// <summary>
/// Ventana de ayuda, sabotaje y negociacion. El tiempo se extiende con ServerExtendPlanning.
/// Termina al acabarse el tiempo o cuando todos los participantes dan Listo (CombatReadyRpc).
/// </summary>
public class CombatPlanningState : GameState
{
    public CombatPlanningState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.CombatCountdown, GamePhase.Combat, GamePhase.ChoosingDoor, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerStartCountdown();
    }
}

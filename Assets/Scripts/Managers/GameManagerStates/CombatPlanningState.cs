/// <summary>
/// Ventana de ayuda, sabotaje y negociacion. El tiempo se extiende con ServerExtendPlanning.
/// Termina al acabarse el tiempo o cuando todos los participantes dan Listo (CombatReadyRpc).
/// </summary>
public class CombatPlanningState : GameState
{
    public CombatPlanningState(GameManager game) : base(game) { }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerStartCountdown();
    }
}

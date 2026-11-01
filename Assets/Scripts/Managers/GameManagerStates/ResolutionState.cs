/// <summary>Resultado del combate. Al terminar, los participantes vuelven a la zona de llegada y pasa el turno.</summary>
public class ResolutionState : GameState
{
    public ResolutionState(GameManager game) : base(game) { }

    public override void Enter()
    {
        // TODO 2B: victoria -> tesoros y niveles. Derrota -> d20, EscapeCondition y BadThing.
        // TODO 2B: curar a los participantes vivos hasta el minimo de vida configurable.
    }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerEndCombat();
    }
}

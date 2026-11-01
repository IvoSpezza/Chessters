/// <summary>Cuenta regresiva antes de empezar el combate.</summary>
public class CombatCountdownState : GameState
{
    public CombatCountdownState(GameManager game) : base(game) { }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ChangePhase(GamePhase.Combat);
    }
}

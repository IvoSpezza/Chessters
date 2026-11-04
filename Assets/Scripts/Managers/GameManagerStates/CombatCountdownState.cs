/// <summary>Cuenta regresiva antes de empezar el combate.</summary>
public class CombatCountdownState : GameState
{
    public CombatCountdownState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => Allows(next, GamePhase.Combat, GamePhase.ChoosingDoor, GamePhase.RestRoom, GamePhase.FinalDuel, GamePhase.GameOver);

    public override void Update()
    {
        if (_game.PhaseTimeUp) TransitionTo(GamePhase.Combat);
    }
}

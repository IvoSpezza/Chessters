/// <summary>La partida terminó. El ganador queda replicado en GameManager.WinnerClientId.</summary>
public class GameOverState : GameState
{
    public GameOverState(GameManager game) : base(game) { }
    public override bool CanTransitionTo(GamePhase next) => false;

    public override void Enter()
    {
        if (SessionManager.instance != null)
            SessionManager.instance.ServerReturnToLobby();
    }
}

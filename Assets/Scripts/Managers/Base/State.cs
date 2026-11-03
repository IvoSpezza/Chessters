using UnityEngine;

/// <summary>
/// Estado base
/// </summary>
public abstract class State
{
    protected Animator _animator;
    protected StateMachine _stateMachine;
    public virtual void Enter() { }

    public virtual void Update() { }

    public virtual void Exit() { }

}

/// <summary>
/// Base de los estados de la partida. Corren solo en el servidor (la maquina vive en el GameManager).
/// Para cambiar de fase: _game.ChangePhase(...). Los clientes leen la fase replicada, no la maquina.
/// </summary>
public abstract class GameState : State
{
    protected readonly GameManager _game;
    protected StateMachine _machine;

    protected GameState(GameManager game)
    {
        _game = game;
    }

    internal void Attach(StateMachine machine) => _machine = machine;

    /// <summary>Declara las fases a las que este estado puede ceder el control.</summary>
    public virtual bool CanTransitionTo(GamePhase next) => false;

    protected static bool Allows(GamePhase next, params GamePhase[] allowed)
    {
        foreach (GamePhase phase in allowed)
            if (phase == next) return true;
        return false;
    }

    protected bool TransitionTo(GamePhase next, float seconds = 0f)
    {
        if (_machine == null || !_machine.CanChangeState(next)) return false;
        return _game.ChangePhase(next, seconds);
    }
}

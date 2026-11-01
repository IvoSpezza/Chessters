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

    protected GameState(GameManager game)
    {
        _game = game;
    }
}
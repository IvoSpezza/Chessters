using System;
using System.Collections.Generic;
using UnityEngine;

public class StateMachine
{
    private State _currentState;

    public State Current => _currentState;

    private Dictionary<Enum, State> _states;
    public StateMachine()
    {
        _states = new Dictionary<Enum, State>();
    }

    public void MachineUpdate()
    {
        _currentState?.Update();
    }
    public void AddState(State state, Enum key)
    {
        _states.Add(key, state);
        if (state is GameState gameState) gameState.Attach(this);
    }

    public bool CanChangeState(Enum key)
    {
        if (!_states.TryGetValue(key, out State next)) return false;
        if (_currentState == null) return Equals(key, GamePhase.WaitingForBodies);
        return _currentState is GameState current && key is GamePhase phase && current.CanTransitionTo(phase);
    }

    public bool ChangeState(Enum key)
    {        
        if (!CanChangeState(key)) return false;
        _currentState?.Exit();
        _currentState = _states[key];
        _currentState?.Enter();
        return true;
    }
}

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
    }
    public void ChangeState(Enum key)
    {        
        _currentState?.Exit();
        _currentState = _states[key];
        _currentState?.Enter();
    }
}

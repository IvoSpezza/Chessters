/// <summary>
/// [TEMP] Sala de descanso: espera en la sala actual y pasa al piso siguiente.
/// TODO etapa 4: curar a los vivos y habilitar el trade entre ellos.
/// </summary>
public class RestRoomState : GameState
{
    public RestRoomState(GameManager game) : base(game) { }

    public override void Update()
    {
        if (_game.PhaseTimeUp) _game.ServerStartNextFloor();
    }
}

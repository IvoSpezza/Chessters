using System.Collections.Generic;
using Unity.Netcode;

/// <summary>Sala instanciada en la escena Game. Solo servidor. Dura hasta que se patea la puerta siguiente.</summary>
public class ActiveRoom
{
    public RoomData Data { get; }
    public RoomLayout Layout { get; }
    public NetworkObject NetworkObject { get; }

    /// <summary>Solo salas de tesoro. Los cofres no son hijos de la sala, asi que se despawnean aparte.</summary>
    public readonly List<NetworkObject> Chests = new List<NetworkObject>();

    public ActiveRoom(RoomData data, RoomLayout layout, NetworkObject networkObject)
    {
        Data = data;
        Layout = layout;
        NetworkObject = networkObject;
    }

    public void Despawn()
    {
        foreach (NetworkObject chest in Chests)
            if (chest != null && chest.IsSpawned) chest.Despawn(true);

        if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
    }
}
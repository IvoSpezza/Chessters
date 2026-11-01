using Unity.Netcode;
using UnityEngine;

public enum ChestType
{
    Treasure,
    Curse
}

/// <summary>
/// Cofre de la sala de tesoro. Prefab de red: NetworkObject + Chest (sin NetworkObject hijos).
/// El tipo y su contenido solo existen en el servidor (nunca se replican): nadie sabe que hay adentro.
/// El propio cofre maneja la decision (abrir / pasar); GameManager solo conoce la fase.
/// </summary>
public class Chest : NetworkBehaviour
{
    private readonly NetworkVariable<bool> _opened = new NetworkVariable<bool>();

    // Solo servidor
    private ChestType _type;
    private int _itemCount;
    private CurseData _curse;

    public bool IsOpened => _opened.Value;

    // TODO: visual de cofre abierto (suscribirse a _opened.OnValueChanged en OnNetworkSpawn)

    public void ServerSetup(ChestType type, int itemCount, CurseData curse)
    {
        if (!IsServer) return;

        _type = type;
        _itemCount = itemCount;
        _curse = curse;
    }

    // ---------------------------------------------------------------- Pedidos del cliente

    [Rpc(SendTo.Server)]
    public void OpenRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanDecide(sender)) return;

        ServerOpen(sender);
        GameManager.Instance.ServerOnChestResolved();
    }

    /// <summary>[TEMP] Hoy el jugador de turno solo lo ignora. TODO: ofrecerlo a otro jugador al azar; si nadie lo quiere vuelve al dueño del turno.</summary>
    [Rpc(SendTo.Server)]
    public void PassRpc(RpcParams rpcParams = default)
    {
        if (!CanDecide(rpcParams.Receive.SenderClientId)) return;

        GameManager.Instance.ServerOnChestResolved();
    }

    // El que decide hoy es el jugador de turno. TODO: variable con el decisor actual cuando exista la oferta.
    private bool CanDecide(ulong sender)
    {
        GameManager game = GameManager.Instance;
        return game != null
            && game.Phase == GamePhase.ChestDecision
            && sender == game.CurrentTurnPlayer
            && !_opened.Value;
    }

    // ---------------------------------------------------------------- Servidor

    private void ServerOpen(ulong opener)
    {
        _opened.Value = true;

        PlayerSoul soul = PlayerSoul.GetFor(opener);
        if (soul == null) return;

        switch (_type)
        {
            case ChestType.Treasure:
                if (soul.TryGetBodyComponent(out PlayerInventory inventory))
                    LootService.GiveRandom(inventory, _itemCount, inventory.Catalog);
                break;

            case ChestType.Curse:
                if (_curse != null && soul.TryGetBodyComponent(out PlayerCurses curses))
                    curses.ServerAddCurse(_curse);
                break;
        }

        Debug.Log($"[Chest] El jugador {opener} abrio un cofre de {_type}.");
    }
}

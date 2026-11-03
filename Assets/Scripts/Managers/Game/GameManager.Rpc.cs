using Unity.Netcode;
using UnityEngine;

/// <summary>Unificador de RPC.</summary>
public partial class GameManager
{
    /// <summary>El jugador de turno ofrece parte del botín del combate a un posible ayudante.</summary>
    public void RequestCombatHelp(ulong targetClientId, int reward) => RequestCombatHelpRpc(targetClientId, reward);

    /// <summary>El jugador solicitado acepta o rechaza la oferta de ayuda.</summary>
    public void RespondToCombatHelp(bool accept) => RespondToCombatHelpRpc(accept);

    /// <summary>Solicita aplicar un consumible temporal al jugador o monstruo seleccionado.</summary>
    public void RequestCombatModifier(int itemId, ulong targetNetworkObjectId) =>
        ApplyCombatModifierRpc(itemId, targetNetworkObjectId);

    /// <summary>El servidor anuncia el disparo para crear visuales locales sin replicar la trayectoria.</summary>
    public void ServerBroadcastProjectileLaunched(int rangedItemId, Vector3 position, Vector3 direction,
        ulong shooterClientId, uint sequence)
    {
        if (!IsServer) return;
        DisplayProjectile(rangedItemId, position, direction, shooterClientId, sequence);
        ProjectileLaunchedRpc(rangedItemId, position, direction, shooterClientId, sequence);
    }

    /// <summary>El servidor elimina la predicción/local visual cuando termina la bala física.</summary>
    public void ServerBroadcastProjectileFinished(ulong shooterClientId, uint sequence)
    {
        if (!IsServer || shooterClientId == ulong.MaxValue) return;
        Projectile.CancelPredictedVisual(shooterClientId, sequence);
        ProjectileFinishedRpc(shooterClientId, sequence);
    }

    [Rpc(SendTo.NotServer)]
    private void ProjectileLaunchedRpc(int rangedItemId, Vector3 position, Vector3 direction,
        ulong shooterClientId, uint sequence, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        DisplayProjectile(rangedItemId, position, direction, shooterClientId, sequence);
    }

    [Rpc(SendTo.NotServer)]
    private void ProjectileFinishedRpc(ulong shooterClientId, uint sequence, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        Projectile.CancelPredictedVisual(shooterClientId, sequence);
    }

    private void DisplayProjectile(int rangedItemId, Vector3 position, Vector3 direction,
        ulong shooterClientId, uint sequence)
    {
        if (_lootCatalog != null && _lootCatalog.TryGetItem(rangedItemId, out RangedData ranged))
            ranged.ShowRemoteProjectile(position, direction, shooterClientId, sequence);
    }

    [Rpc(SendTo.Server)]
    private void RequestCombatHelpRpc(ulong targetClientId, int reward, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!IsServer || _phase.Value != GamePhase.CombatPlanning || _encounter == null ||
            sender != _currentTurnPlayer.Value || _combatReady.Contains(sender) || HasPendingHelpOffer ||
            HasAcceptedCombatHelper || targetClientId == sender ||
            !NetworkManager.ConnectedClients.ContainsKey(targetClientId) ||
            reward < 0 || reward > _encounter.Data.treasureAmount)
            return;

        PlayerSoul target = PlayerSoul.GetFor(targetClientId);
        if (target == null || !target.IsAlive || !target.TryGetBodyComponent(out Health targetHealth) ||
            targetHealth.IsDead || _encounter.Participants.Contains(targetClientId)) return;

        _pendingHelpTarget.Value = targetClientId;
        _pendingHelpReward.Value = reward;
        UnityEngine.Debug.Log($"[Game] {sender} ofrece {reward} tesoros a {targetClientId} para ayudar en combate.");
    }

    [Rpc(SendTo.Server)]
    private void RespondToCombatHelpRpc(bool accept, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!IsServer || _phase.Value != GamePhase.CombatPlanning || !HasPendingHelpOffer ||
            sender != _pendingHelpTarget.Value || _encounter == null)
            return;

        if (accept)
        {
            PlayerSoul helper = PlayerSoul.GetFor(sender);
            if (helper == null || !helper.IsAlive || !helper.TryGetBodyComponent(out Health helperHealth) ||
                helperHealth.IsDead || !NetworkManager.ConnectedClients.ContainsKey(sender))
            {
                ServerClearHelpOffer();
                return;
            }

            _acceptedHelperClientId = sender;
            _acceptedHelperReward = _pendingHelpReward.Value;
            _encounter.Participants.Add(sender);
            _participants.Add(sender);
            UnityEngine.Debug.Log($"[Game] {sender} acepta ayudar por {_acceptedHelperReward} tesoros.");
            ServerExtendPlanning(_planningExtensionSeconds);
        }
        else
        {
            UnityEngine.Debug.Log($"[Game] {sender} rechaza la oferta de ayuda.");
        }

        ServerClearHelpOffer();
    }

    [Rpc(SendTo.Server)]
    private void ApplyCombatModifierRpc(int itemId, ulong targetNetworkObjectId, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!IsServer || _phase.Value != GamePhase.CombatPlanning || _encounter == null ||
            !NetworkManager.ConnectedClients.ContainsKey(sender)) return;

        PlayerSoul user = PlayerSoul.GetFor(sender);
        if (user == null || !user.IsAlive || !user.TryGetBodyComponent(out PlayerInventory inventory) ||
            !inventory.Catalog.TryGetItem(itemId, out CombatModifierConsumableData consumable) ||
            consumable.modifiers == null || consumable.modifiers.Count == 0 ||
            !inventory.GetBackpack().Exists(item => item != null && item.itemID == itemId))
            return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject target) ||
            target == null || !target.IsSpawned || !target.TryGetComponent(out Stats targetStats) ||
            !target.TryGetComponent(out Health targetHealth) || targetHealth.IsDead)
            return;

        bool isMonster = _encounter.Monsters.Contains(target);
        bool isCombatPlayer = !isMonster && _encounter.Participants.Contains(target.OwnerClientId);
        if (!isMonster && !isCombatPlayer) return;
        if (isCombatPlayer)
        {
            PlayerSoul targetSoul = PlayerSoul.GetFor(target.OwnerClientId);
            if (targetSoul == null || !targetSoul.IsAlive) return;
        }

        if (!inventory.RemoveItem(itemId) || !targetStats.ServerAddCombatModifiers(consumable.modifiers)) return;

        _encounter.RegisterCombatEffect(targetStats);
        ServerExtendPlanning(_planningExtensionSeconds);
        UnityEngine.Debug.Log($"[Game] {sender} aplicó {consumable.itemName} a {target.name}.");
    }

    [Rpc(SendTo.Server)]
    public void KickDoorRpc(RpcParams rpcParams = default)
    {
        if (_phase.Value != GamePhase.ChoosingDoor ||
            rpcParams.Receive.SenderClientId != _currentTurnPlayer.Value) return;
        ServerKickDoor();
    }

    [Rpc(SendTo.Server)]
    public void CombatReadyRpc(RpcParams rpcParams = default)
    {
        if (_phase.Value != GamePhase.CombatPlanning) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender == _currentTurnPlayer.Value && HasPendingHelpOffer) return;
        if (!_participants.Contains(sender) || _combatReady.Contains(sender)) return;

        _combatReady.Add(sender);
        ServerCheckAllReady();
    }

    /// <summary>Temporalmente extiende la planificación para probar eventos de sabotaje.</summary>
    [Rpc(SendTo.Server)]
    public void DebugModifyCombatRpc(RpcParams rpcParams = default)
    {
        if (_phase.Value != GamePhase.CombatPlanning) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        PlayerSoul soul = PlayerSoul.GetFor(sender);
        if (!NetworkManager.ConnectedClients.ContainsKey(sender) || soul == null || !soul.IsAlive) return;

        ServerExtendPlanning(_planningExtensionSeconds);
    }
}

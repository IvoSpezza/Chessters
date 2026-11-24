using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Panel OnGUI para probar fases, turnos, planificacion y cofres sin UI. 
/// Se borra cuando exista la UI real.
/// </summary>
public class GameDebugHud : MonoBehaviour
{
    private Chest _chest;
    private int _selectedCombatModifierId = ItemCatalog.EmptyID;
    private bool _showCombatModifierPicker;

    private void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.Phase != GamePhase.CombatPlanning)
        {
            _showCombatModifierPicker = false;
            _selectedCombatModifierId = ItemCatalog.EmptyID;
        }

        ulong me = NetworkManager.Singleton.LocalClientId;

        GUILayout.BeginArea(new Rect(Screen.width - 430, 10, 420, 650), GUI.skin.box);
        GUILayout.Label($"Fase: {gm.Phase}");
        GUILayout.Label($"Piso {gm.Floor} - Turno {gm.TurnIndex}");
        GUILayout.Label($"Turno de: {gm.CurrentTurnPlayer}{(gm.IsMyTurn ? " (VOS)" : "")}");
        if (gm.RemainingTime > 0f) GUILayout.Label($"Tiempo: {gm.RemainingTime:0.0}s");

        if (gm.TryGetCurrentCombat(out BaseCombatData combat))
            GUILayout.Label($"Combate: {combat.combatName} (id {combat.combatID})");

        if (gm.Phase == GamePhase.ChoosingDoor && gm.IsMyTurn && GUILayout.Button("Patear puerta"))
            gm.KickDoorRpc();

        if (gm.Phase == GamePhase.CombatPlanning)
        {
            if (gm.HasPendingHelpOffer)
            {
                PlayerSoul requester = PlayerSoul.GetFor(gm.CurrentTurnPlayer);
                string requesterName = requester != null ? requester.PlayerName : $"Jugador {gm.CurrentTurnPlayer}";
                GUILayout.Label($"{requesterName} ofrece {gm.PendingHelpReward} tesoros por ayuda.");

                if (gm.PendingHelpTargetClientId == me)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Aceptar ayuda")) gm.RespondToCombatHelp(true);
                    if (GUILayout.Button("Rechazar")) gm.RespondToCombatHelp(false);
                    GUILayout.EndHorizontal();
                }
                else if (gm.IsMyTurn)
                {
                    GUILayout.Label("Esperando respuesta...");
                }
            }
            else if (gm.IsMyTurn && !gm.HasAcceptedCombatHelper && !gm.IsReadyForCombat(me) &&
                     gm.HasCurrentCombat)
            {
                GUILayout.Label("Pedir ayuda (se paga del botin del combate):");
                int maxReward = gm.CurrentCombatTreasureAmount;
                foreach (PlayerSoul candidate in PlayerSoul.All)
                {
                    if (candidate == null || candidate.OwnerClientId == me ||
                        !candidate.TryGetBodyComponent(out Health candidateHealth) || candidateHealth.IsDead ||
                        gm.IsParticipant(candidate.OwnerClientId)) continue;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(candidate.PlayerName);
                    for (int reward = 0; reward <= maxReward; reward++)
                    {
                        int offeredReward = reward;
                        if (GUILayout.Button($"Ofrecer {offeredReward}"))
                            gm.RequestCombatHelp(candidate.OwnerClientId, offeredReward);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else if (gm.HasAcceptedCombatHelper)
            {
                GUILayout.Label("Ya hay un ayudante aceptado.");
            }

            if (gm.IsParticipant(me) && !gm.IsReadyForCombat(me) &&
                !(gm.IsMyTurn && gm.HasPendingHelpOffer) && GUILayout.Button("Listo para combatir"))
                gm.CombatReadyRpc();

            PlayerSoul localSoul = PlayerSoul.Local;
            if (localSoul != null && localSoul.IsAlive)
                RenderCombatModifierPicker(gm);
        }

        if (gm.Phase == GamePhase.ChestDecision && gm.IsMyTurn)
        {
            if (_chest == null) _chest = FindFirstObjectByType<Chest>();

            if (_chest != null && !_chest.IsOpened)
            {
                if (GUILayout.Button("[TEMP] Abrir cofre")) _chest.OpenRpc();
                if (GUILayout.Button("[TEMP] Pasar del cofre")) _chest.PassRpc();
            }
        }

        GUILayout.EndArea();
    }

    private void RenderCombatModifierPicker(GameManager game)
    {
        if (!_showCombatModifierPicker)
        {
            if (GUILayout.Button("Modificar combate")) _showCombatModifierPicker = true;
            return;
        }

        GUILayout.Space(8);
        GUILayout.Label("<b>Consumibles de combate</b>");
        if (GUILayout.Button("Cerrar"))
        {
            _showCombatModifierPicker = false;
            _selectedCombatModifierId = ItemCatalog.EmptyID;
            return;
        }

        PlayerSoul localSoul = PlayerSoul.Local;
        if (localSoul == null || !localSoul.TryGetBodyComponent(out PlayerInventory inventory)) return;

        if (_selectedCombatModifierId == ItemCatalog.EmptyID)
        {
            GUILayout.Label("Elegí un consumible y después el objetivo:");
            bool foundConsumable = false;
            foreach (ItemDataBase item in inventory.GetBackpack())
            {
                if (!(item is CombatModifierConsumableData consumable)) continue;
                foundConsumable = true;
                if (GUILayout.Button($"Usar {consumable.itemName}: {FormatModifiers(consumable)}"))
                    _selectedCombatModifierId = consumable.itemID;
            }

            if (!foundConsumable) GUILayout.Label("No tenés consumibles de combate.");
            return;
        }

        GUILayout.Label("Aplicar el efecto a:");
        GUILayout.Label("<b>Equipo de jugadores</b>");
        foreach (PlayerSoul targetSoul in PlayerSoul.All)
        {
            if (targetSoul == null || !targetSoul.IsAlive || !game.IsParticipant(targetSoul.OwnerClientId) ||
                !targetSoul.TryGetBody(out NetworkObject targetBody) ||
                !targetBody.TryGetComponent(out Health targetHealth) || targetHealth.IsDead) continue;

            if (GUILayout.Button($"Jugador: {targetSoul.PlayerName}"))
            {
                game.RequestCombatModifier(_selectedCombatModifierId, targetBody.NetworkObjectId);
                _selectedCombatModifierId = ItemCatalog.EmptyID;
                return;
            }
        }

        GUILayout.Label("<b>Monstruos</b>");
        foreach (MonsterCombatAI monster in FindObjectsByType<MonsterCombatAI>(FindObjectsSortMode.None))
        {
            if (monster == null || !monster.TryGetComponent(out NetworkObject targetObject) || !targetObject.IsSpawned ||
                !monster.TryGetComponent(out Health monsterHealth) || monsterHealth.IsDead) continue;

            if (GUILayout.Button($"Monstruo: {monster.name}"))
            {
                game.RequestCombatModifier(_selectedCombatModifierId, targetObject.NetworkObjectId);
                _selectedCombatModifierId = ItemCatalog.EmptyID;
                return;
            }
        }

        if (GUILayout.Button("Cancelar")) _selectedCombatModifierId = ItemCatalog.EmptyID;
    }

    private static string FormatModifiers(CombatModifierConsumableData consumable)
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < consumable.modifiers.Count; i++)
        {
            StatModifier modifier = consumable.modifiers[i];
            if (i > 0) text.Append(", ");
            text.Append(modifier.Type).Append(' ');
            if (modifier.Modifier == ModifierType.Percent)
                text.AppendFormat("{0:+0.#;-0.#;0}%", modifier.Value * 100f);
            else
                text.AppendFormat("{0:+0.#;-0.#;0}", modifier.Value);
        }
        return text.ToString();
    }
}

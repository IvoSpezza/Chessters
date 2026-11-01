using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [TEMP] Panel OnGUI para probar fases, turnos, planificacion y cofres sin UI. Ponerlo en la escena Game.
/// Se borra cuando exista la UI real.
/// </summary>
public class GameDebugHud : MonoBehaviour
{
    private Chest _chest;

    private void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        ulong me = NetworkManager.Singleton.LocalClientId;

        GUILayout.BeginArea(new Rect(Screen.width - 290, 10, 280, 280), GUI.skin.box);
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
            if (gm.IsParticipant(me) && !gm.IsReadyForCombat(me) && GUILayout.Button("Listo para combatir"))
                gm.CombatReadyRpc();

            if (GUILayout.Button("[TEMP] Modificar combate (+tiempo)"))
                gm.DebugModifyCombatRpc();
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
}
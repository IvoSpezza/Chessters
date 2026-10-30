using System;
using UnityEngine;

/// <summary>
/// Ajustes de la partida. Por ahora se editan desde el inspector del GameManager.
/// TODO: que el host los defina en el lobby y se repliquen a los clientes (el limite de mochila lo va a necesitar la UI).
/// </summary>
[Serializable]
public class GameSettings
{
    [Min(2)] public int targetLevel = 10;
    [Min(0)] public int backpackLimit = 5;
    [Min(0)] public int initialLootCount = 3;
}

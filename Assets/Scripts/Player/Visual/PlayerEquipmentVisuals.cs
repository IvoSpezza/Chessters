using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Muestra las armas equipadas en las manos del cuerpo. Va en el CUERPO, junto a PlayerInventory.
///
/// - Escucha PlayerInventory.OnEquipmentChanged (se dispara en todos los peers, tambien en clientes tardios)
///   y compara el ID de cada mano con el que se esta mostrando: si cambio, destruye e instancia el modelo.
/// - Si no hay arma equipada en la mano, no se ve nada.
/// - Si el arma no tiene visualPrefab: error en consola y no se muestra nada (no hay placeholders).
/// - Los sockets son obligatorios: deben asignarse y ser hijos del cuerpo.
/// - El modelo del arma debe tener su punta/filo apuntando hacia +Z del prefab.
/// - Un arma de dos manos se ve solo en HandSocket1 (la mano 2 queda marcada como ocupada).
///
/// No sincroniza nada por red: todo sale de las NetworkList que ya replica PlayerInventory.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerEquipmentVisuals : NetworkBehaviour
{
    [Header("Sockets de las manos (obligatorios, hijos del cuerpo)")]
    [SerializeField] private Transform _handSocket1;
    [SerializeField] private Transform _handSocket2;

    private PlayerInventory _inventory;

    // Un modelo por mano (indice 0 = Hand1, 1 = Hand2) y el ID que estan mostrando
    private readonly GameObject[] _models = new GameObject[2];
    private readonly int[] _shownIds = { PlayerInventory.EmptyID, PlayerInventory.EmptyID };

    /// <summary>True si ambos sockets estan asignados y son hijos del cuerpo.</summary>
    public bool IsReady =>
        _handSocket1 != null && _handSocket2 != null &&
        _handSocket1.IsChildOf(transform) && _handSocket2.IsChildOf(transform);

    /// <summary>Socket de la mano (0 = Hand1, 1 = Hand2).</summary>
    public Transform GetSocket(int handIndex) => handIndex == 0 ? _handSocket1 : _handSocket2;

    // ---------------------------------------------------------------- Ciclo de vida

    public override void OnNetworkSpawn()
    {
        _inventory = GetComponent<PlayerInventory>();

        if (!IsReady)
        {
            Debug.LogError("[PlayerEquipmentVisuals] Asigná HandSocket1 y HandSocket2 (deben ser hijos del cuerpo). Los visuales de armas quedan desactivados.", this);
            return;
        }

        _inventory.OnEquipmentChanged += RefreshWeapons;
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null) _inventory.OnEquipmentChanged -= RefreshWeapons;
    }

    // ---------------------------------------------------------------- Armas

    private void RefreshWeapons()
    {
        for (int i = 0; i < 2; i++)
        {
            int id = _inventory.GetEquippedId(i == 0 ? EquipSlot.Hand1 : EquipSlot.Hand2);
            if (id == _shownIds[i]) continue;   // nada cambio en esta mano

            ClearModel(i);
            _shownIds[i] = id;
            if (id < 0) continue;               // mano vacia, o mano 2 ocupada por un arma de dos manos

            if (!_inventory.Catalog.TryGetItem(id, out WeaponDataBase weapon)) continue;

            if (weapon.visualPrefab == null)
            {
                Debug.LogError($"[PlayerEquipmentVisuals] El arma '{weapon.name}' no tiene visualPrefab; no se muestra.", weapon);
                continue;
            }

            GameObject model = Instantiate(weapon.visualPrefab, GetSocket(i));
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // El modelo es solo visual: sin colliders para que no interfiera con los golpes ni la fisica
            foreach (Collider col in model.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(col);

            _models[i] = model;
        }
    }

    private void ClearModel(int handIndex)
    {
        if (_models[handIndex] != null) Destroy(_models[handIndex]);
        _models[handIndex] = null;
    }
}

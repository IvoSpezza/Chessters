using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pool de proyectiles de red. Registra un INetworkPrefabInstanceHandler por prefab, asi que
/// cuando un proyectil se despawnea NO se destruye: se desactiva y vuelve al pool.
///
/// Debe registrarse en TODOS los peers (servidor y clientes) antes de que se spawnee un proyectil.
/// PlayerCombat lo hace al spawnear, leyendo los RangedData del ItemCatalog.
/// </summary>
public static class ProjectilePool
{
    private static readonly Dictionary<GameObject, Pool> _pools = new Dictionary<GameObject, Pool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _pools.Clear();

    /// <summary>Registra el pool de cada arma a distancia del catalogo. Es idempotente.</summary>
    public static void RegisterAll(ItemCatalog catalog)
    {
        Debug.Log($"[Pool] RegisterAll catálogo={(catalog != null ? catalog.Items.Count.ToString() : "NULL")}");
        if (catalog == null) return;
        foreach (ItemDataBase item in catalog.Items)
        {
            if (item is RangedData ranged && ranged.projectilePrefab != null)
                Register(ranged.projectilePrefab, ranged.prewarmCount);
        }
    }

    public static void Register(GameObject prefab, int prewarmCount)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || prefab == null) return;

        if (!prefab.TryGetComponent(out NetworkObject _))
        {
            Debug.LogError($"[ProjectilePool] El prefab {prefab.name} no tiene NetworkObject.", prefab);
            return;
        }

        if (!_pools.TryGetValue(prefab, out Pool pool))
        {
            pool = new Pool(prefab);
            _pools.Add(prefab, pool);
            pool.Prewarm(prewarmCount);
        }

        
        nm.PrefabHandler.AddHandler(prefab, pool);
        Debug.Log($"[Pool] {(nm.IsServer ? "SERVIDOR" : "CLIENTE")} registró el handler de {prefab.name}");
    }

    /// <summary>Solo servidor: saca un proyectil del pool (todavia sin spawnear en red).</summary>
    public static NetworkObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!_pools.ContainsKey(prefab)) Register(prefab, 0);
        if (!_pools.TryGetValue(prefab, out Pool pool)) return null;

        return pool.Instantiate(NetworkManager.ServerClientId, position, rotation);
    }

    // ------------------------------------------------------------------

    private class Pool : INetworkPrefabInstanceHandler
    {
        private readonly GameObject _prefab;
        private readonly Queue<NetworkObject> _available = new Queue<NetworkObject>();

        public Pool(GameObject prefab) => _prefab = prefab;

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++) _available.Enqueue(CreateInstance());
        }

        private NetworkObject CreateInstance()
        {
            GameObject go = Object.Instantiate(_prefab);
            Object.DontDestroyOnLoad(go);   
            go.SetActive(false);
            return go.GetComponent<NetworkObject>();
        }

        // NGO lo llama al spawnear (en el servidor lo llamamos nosotros via Get, en clientes lo llama NGO)
        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            NetworkObject obj = null;
            while (obj == null && _available.Count > 0) obj = _available.Dequeue();   
            if (obj == null) obj = CreateInstance();

            obj.transform.SetPositionAndRotation(position, rotation);
            obj.gameObject.SetActive(true);
            return obj;
        }

        // NGO lo llama al despawnear con destroy = true: lo guardamos en vez de destruirlo
        public void Destroy(NetworkObject networkObject)
        {
            networkObject.gameObject.SetActive(false);
            _available.Enqueue(networkObject);
        }
    }
}

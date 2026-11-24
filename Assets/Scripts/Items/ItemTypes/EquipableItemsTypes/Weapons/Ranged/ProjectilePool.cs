using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Pool local del servidor para balas físicas. No registra prefabs en NGO.</summary>
public static class ProjectilePool
{
    private static readonly Dictionary<GameObject, Pool> Pools = new Dictionary<GameObject, Pool>();
    private static readonly Dictionary<Projectile, Pool> Owners = new Dictionary<Projectile, Pool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Pools.Clear();
        Owners.Clear();
    }

    public static void RegisterAll(ItemCatalog catalog)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (catalog == null || manager == null || !manager.IsServer) return;

        foreach (ItemDataBase item in catalog.Weapons)
            if (item is RangedData ranged && ranged.projectilePrefab != null)
                Register(ranged.projectilePrefab, ranged.prewarmCount);
    }

    public static void Register(GameObject prefab, int prewarmCount)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (prefab == null || !prefab.TryGetComponent(out Projectile _))
        {
            Debug.LogError($"[ProjectilePool] El prefab {prefab?.name} no tiene el script Projectile.", prefab);
            return;
        }

        if (Pools.ContainsKey(prefab)) return;
        Pool pool = new Pool(prefab);
        Pools.Add(prefab, pool);
        pool.Prewarm(prewarmCount);
    }

    public static Projectile Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return null;
        if (!Pools.ContainsKey(prefab)) Register(prefab, 0);
        if (!Pools.TryGetValue(prefab, out Pool pool)) return null;
        return pool.Get(position, rotation);
    }

    public static void Return(Projectile projectile)
    {
        if (projectile == null) return;
        if (Owners.TryGetValue(projectile, out Pool pool)) pool.Return(projectile);
        else Object.Destroy(projectile.gameObject);
    }

    private sealed class Pool
    {
        private readonly GameObject _prefab;
        private readonly Queue<Projectile> _available = new Queue<Projectile>();

        public Pool(GameObject prefab) => _prefab = prefab;

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Projectile projectile = CreateInstance();
                if (projectile != null) _available.Enqueue(projectile);
            }
        }

        private Projectile CreateInstance()
        {
            GameObject instance = Object.Instantiate(_prefab);
            Object.DontDestroyOnLoad(instance);
            Projectile projectile = instance.GetComponent<Projectile>();
            if (projectile == null)
            {
                Object.Destroy(instance);
                return null;
            }

            Owners[projectile] = this;
            instance.SetActive(false);
            return projectile;
        }

        public Projectile Get(Vector3 position, Quaternion rotation)
        {
            Projectile projectile = null;
            while (projectile == null && _available.Count > 0) projectile = _available.Dequeue();
            if (projectile == null) projectile = CreateInstance();
            if (projectile == null) return null;

            projectile.transform.SetPositionAndRotation(position, rotation);
            projectile.gameObject.SetActive(true);
            return projectile;
        }

        public void Return(Projectile projectile)
        {
            projectile.ResetForPool();
            projectile.gameObject.SetActive(false);
            _available.Enqueue(projectile);
        }
    }
}

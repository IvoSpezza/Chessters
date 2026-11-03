using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tiene dos usos separados: bala física simulada solo por el servidor y visual local sin colisiones.
/// Ninguna instancia del proyectil es un NetworkObject; el servidor anuncia disparo e impacto por RPC.
/// </summary>
public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float _radius = 0.15f;

    private struct PredictionKey : IEquatable<PredictionKey>
    {
        public ulong ClientId;
        public uint Sequence;

        public bool Equals(PredictionKey other) => ClientId == other.ClientId && Sequence == other.Sequence;
        public override bool Equals(object obj) => obj is PredictionKey other && Equals(other);
        public override int GetHashCode() => unchecked((ClientId.GetHashCode() * 397) ^ (int)Sequence);
    }

    private static readonly Dictionary<PredictionKey, Projectile> Visuals = new Dictionary<PredictionKey, Projectile>();

    private readonly Collider[] _overlapBuffer = new Collider[8];
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];
    private Renderer[] _renderers;
    private bool[] _rendererDefaults;
    private Collider[] _colliders;

    private Vector3 _direction;
    private float _speed;
    private float _damage;
    private float _expireTime;
    private float _hardExpireTime;
    private LayerMask _hitMask;
    private bool _serverSimulation;
    private bool _visual;
    private bool _confirmed;
    private bool _firstStep;
    private ulong _ignoredOwnerNetworkId = ulong.MaxValue;
    private PredictionKey _key;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetVisuals() => Visuals.Clear();

    private void Awake()
    {
        CacheComponents();
        SetCollidersEnabled(false);
    }

    private void CacheComponents()
    {
        if (_renderers == null)
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _rendererDefaults = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _rendererDefaults[i] = _renderers[i] != null && _renderers[i].enabled;
        }
        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
    }

    private void SetRenderersEnabled(bool enabled)
    {
        CacheComponents();
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].enabled = enabled && _rendererDefaults[i];
    }

    private void SetCollidersEnabled(bool enabled)
    {
        CacheComponents();
        foreach (Collider collider in _colliders)
            if (collider != null) collider.enabled = enabled;
    }

    /// <summary>Vuela inmediatamente en el peer local; no calcula impactos ni aplica daño.</summary>
    public static void SpawnPredictedVisual(GameObject prefab, Vector3 position, Quaternion rotation,
        Vector3 direction, float speed, float lifetime, ulong clientId, uint sequence)
    {
        if (prefab == null) return;

        PredictionKey key = new PredictionKey { ClientId = clientId, Sequence = sequence };
        if (Visuals.TryGetValue(key, out Projectile existing) && existing != null)
        {
            existing._confirmed = true;
            return;
        }

        GameObject visualObject = Instantiate(prefab, position, rotation);
        if (!visualObject.TryGetComponent(out Projectile visual))
        {
            Destroy(visualObject);
            Debug.LogError($"[Projectile] El prefab {prefab.name} no tiene el script Projectile.", prefab);
            return;
        }

        visual.InitializeVisual(key, direction, speed, lifetime, confirmed: true);
        Visuals[key] = visual;
    }

    /// <summary>Registra el tiro local como predicción, pendiente de validación del servidor.</summary>
    public static void SpawnPredictedVisual(GameObject prefab, Vector3 position, Quaternion rotation,
        Vector3 direction, float speed, float lifetime, ulong clientId, uint sequence, bool awaitConfirmation)
    {
        if (!awaitConfirmation)
        {
            SpawnPredictedVisual(prefab, position, rotation, direction, speed, lifetime, clientId, sequence);
            return;
        }

        if (prefab == null) return;
        PredictionKey key = new PredictionKey { ClientId = clientId, Sequence = sequence };
        if (Visuals.TryGetValue(key, out Projectile existing) && existing != null) return;

        GameObject visualObject = Instantiate(prefab, position, rotation);
        if (!visualObject.TryGetComponent(out Projectile visual))
        {
            Destroy(visualObject);
            Debug.LogError($"[Projectile] El prefab {prefab.name} no tiene el script Projectile.", prefab);
            return;
        }

        visual.InitializeVisual(key, direction, speed, lifetime, confirmed: false);
        Visuals[key] = visual;
    }

    private void InitializeVisual(PredictionKey key, Vector3 direction, float speed, float lifetime, bool confirmed)
    {
        _key = key;
        _visual = true;
        _confirmed = confirmed;
        _serverSimulation = false;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        _speed = speed;
        _expireTime = Time.time + lifetime;
        _hardExpireTime = Time.time + lifetime + 5f;
        SetCollidersEnabled(false);
        SetRenderersEnabled(true);
    }

    public static void ConfirmPredictedVisual(ulong clientId, uint sequence)
    {
        PredictionKey key = new PredictionKey { ClientId = clientId, Sequence = sequence };
        if (Visuals.TryGetValue(key, out Projectile visual) && visual != null)
            visual._confirmed = true;
    }

    public static void CancelPredictedVisual(ulong clientId, uint sequence)
    {
        PredictionKey key = new PredictionKey { ClientId = clientId, Sequence = sequence };
        if (Visuals.TryGetValue(key, out Projectile visual) && visual != null)
            DestroyVisual(key, visual);
    }

    private static void DestroyVisual(PredictionKey key, Projectile visual)
    {
        Visuals.Remove(key);
        visual._visual = false;
        Destroy(visual.gameObject);
    }

    private void OnDestroy()
    {
        if (!_visual) return;
        if (Visuals.TryGetValue(_key, out Projectile registered) && registered == this)
            Visuals.Remove(_key);
    }

    /// <summary>Inicializa la instancia física del servidor, que es la única que consulta colisiones.</summary>
    public void ServerLaunch(Vector3 direction, float speed, float damage, float lifetime, LayerMask hitMask,
        ulong ignoredOwnerNetworkId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        _speed = speed;
        _damage = damage;
        _hitMask = hitMask;
        _expireTime = Time.time + lifetime;
        _firstStep = true;
        _ignoredOwnerNetworkId = ignoredOwnerNetworkId;
        _serverSimulation = true;
        _visual = false;
        SetRenderersEnabled(false);
        SetCollidersEnabled(false);
    }

    private void Update()
    {
        if (!_visual) return;
        if ((!_confirmed && Time.time >= _expireTime) || Time.time >= _hardExpireTime)
        {
            DestroyVisual(_key, this);
            return;
        }

        transform.position += _direction * (_speed * Time.deltaTime);
        if (_direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
    }

    private void FixedUpdate()
    {
        if (!_serverSimulation) return;

        if (Time.time >= _expireTime)
        {
            FinishServerProjectile();
            return;
        }

        Vector3 start = transform.position;
        if (_firstStep)
        {
            _firstStep = false;
            int count = Physics.OverlapSphereNonAlloc(start, _radius, _overlapBuffer, _hitMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsIgnoredOwner(_overlapBuffer[i])) continue;
                HitCollider(_overlapBuffer[i]);
                return;
            }
        }

        float distance = _speed * Time.fixedDeltaTime;
        int hitCount = Physics.SphereCastNonAlloc(start, _radius, _direction, _hitBuffer, distance, _hitMask,
            QueryTriggerInteraction.Ignore);
        int closestHit = -1;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            if (IsIgnoredOwner(_hitBuffer[i].collider) || _hitBuffer[i].distance >= closestDistance) continue;
            closestHit = i;
            closestDistance = _hitBuffer[i].distance;
        }

        if (closestHit >= 0)
        {
            RaycastHit hit = _hitBuffer[closestHit];
            transform.position = hit.point;
            HitCollider(hit.collider);
            return;
        }

        transform.position = start + _direction * distance;
    }

    private void HitCollider(Collider other)
    {
        IDamageable target = other.GetComponentInParent<IDamageable>();
        target?.TakeDamage(_damage);
        FinishServerProjectile();
    }

    private bool IsIgnoredOwner(Collider other)
    {
        if (_ignoredOwnerNetworkId == ulong.MaxValue || other == null) return false;
        NetworkObject owner = other.GetComponentInParent<NetworkObject>();
        return owner != null && owner.NetworkObjectId == _ignoredOwnerNetworkId;
    }

    private void FinishServerProjectile()
    {
        if (!_serverSimulation) return;
        _serverSimulation = false;
        GameManager.Instance?.ServerBroadcastProjectileFinished(_key.ClientId, _key.Sequence);
        ProjectilePool.Return(this);
    }

    /// <summary>Registra el identificador del disparo para avisar a los clientes cuando la bala física termina.</summary>
    public void SetServerPredictionKey(ulong clientId, uint sequence)
    {
        _key = new PredictionKey { ClientId = clientId, Sequence = sequence };
    }

    public void ResetForPool()
    {
        _serverSimulation = false;
        _visual = false;
        _confirmed = false;
        _firstStep = false;
        _ignoredOwnerNetworkId = ulong.MaxValue;
        _key = default;
    }
}

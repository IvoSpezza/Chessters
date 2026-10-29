using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Proyectil de red. Lo mueve y lo resuelve SOLO el servidor; los clientes lo ven por NetworkTransform.
/// Prefab: NetworkObject + NetworkTransform (autoridad del servidor) + este script, sin Rigidbody.
/// Se recicla con ProjectilePool: no se destruye, se despawnea y vuelve al pool.
/// </summary>
public class Projectile : NetworkBehaviour
{
    [SerializeField, Min(0.01f)] private float _radius = 0.15f;

    private readonly Collider[] _buffer = new Collider[8];

    private Vector3 _direction;
    private float _speed;
    private float _damage;
    private float _expireTime;
    private LayerMask _hitMask;
    private bool _launched;
    private bool _firstStep;

    /// <summary>Solo servidor. Llamar DESPUES de Spawn().</summary>
    public void ServerLaunch(Vector3 direction, float speed, float damage, float lifetime, LayerMask hitMask)
    {
        if (!IsServer) return;

        _direction = direction.normalized;
        _speed = speed;
        _damage = damage;
        _hitMask = hitMask;
        _expireTime = Time.time + lifetime;
        _firstStep = true;
        _launched = true;
    }

    public override void OnNetworkDespawn()
    {
        _launched = false;   // al volver al pool no debe seguir simulando
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsSpawned || !_launched) return;

        if (Time.time >= _expireTime)
        {
            Finish();
            return;
        }

        Vector3 start = transform.position;

        // Si nace tocando algo, SphereCast no lo detecta: lo chequeamos una vez con un overlap
        if (_firstStep)
        {
            _firstStep = false;
            int count = Physics.OverlapSphereNonAlloc(start, _radius, _buffer, _hitMask, QueryTriggerInteraction.Ignore);
            if (count > 0)
            {
                HitCollider(_buffer[0]);
                return;
            }
        }

        float distance = _speed * Time.fixedDeltaTime;
        if (Physics.SphereCast(start, _radius, _direction, out RaycastHit hit, distance, _hitMask, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point;
            HitCollider(hit.collider);
            return;
        }

        transform.position = start + _direction * distance;
    }

    private void HitCollider(Collider other)
    {
        // Si no es danable (pared, suelo) simplemente se corta
        IDamageable target = other.GetComponentInParent<IDamageable>();
        target?.TakeDamage(_damage);
        Finish();
    }

    private void Finish()
    {
        _launched = false;
        if (NetworkObject.IsSpawned) NetworkObject.Despawn();   // el handler lo devuelve al pool
    }
}

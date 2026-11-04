using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Corre la animacion visual de cada ataque. Va en el CUERPO, junto a PlayerEquipmentVisuals.
///
/// - Una corrutina por mano. Si llega un ataque nuevo mientras la anterior sigue, se corta y se reemplaza.
/// - La animacion en si la describe cada arma (WeaponDataBase.PlayAttackVisual); aca solo se ejecuta.
/// - Mueve el SOCKET de la mano (el arma es su hijo) y al terminar lo devuelve a su pose de reposo.
/// - Es 100% cosmetico y local: no sincroniza nada por red. PlayerCombat decide cuando llamarlo.
/// </summary>
[RequireComponent(typeof(PlayerEquipmentVisuals))]
public class PlayerAttackVisuals : NetworkBehaviour
{
    [Tooltip("Material de abanico, anillo y destello. Debe tener propiedad de color (_Color o [MainColor]). Vacio = Sprites/Default.")]
    [SerializeField] private Material _effectMaterial;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs;

    private PlayerEquipmentVisuals _equipment;
    private bool _ready;

    private readonly Coroutine[] _routines = new Coroutine[2];
    private readonly Vector3[] _restPosition = new Vector3[2];
    private readonly Quaternion[] _restRotation = new Quaternion[2];

    // ---------------------------------------------------------------- Ciclo de vida

    public override void OnNetworkSpawn()
    {
        _equipment = GetComponent<PlayerEquipmentVisuals>();
        if (_equipment == null)
        {
            Debug.LogError("[PlayerAttackVisuals] Falta PlayerEquipmentVisuals en el cuerpo.", this);
            return;
        }

        // Si los sockets no estan bien asignados, PlayerEquipmentVisuals ya mostro el error
        if (!_equipment.IsReady) return;

        // Pose de reposo de cada socket, a la que vuelven despues de cada ataque
        for (int i = 0; i < 2; i++)
        {
            Transform socket = _equipment.GetSocket(i);
            _restPosition[i] = socket.localPosition;
            _restRotation[i] = socket.localRotation;
        }
        _ready = true;
    }

    public override void OnNetworkDespawn()
    {
        StopAllCoroutines();
        _routines[0] = null;
        _routines[1] = null;
        _ready = false;
    }

    // ---------------------------------------------------------------- API (la llama PlayerCombat)

    /// <summary>
    /// Reproduce el visual del ataque en la mano indicada (0 = Hand1, 1 = Hand2).
    /// origin = AtackOrigin del golpe. duration = duracion ya escalada por la velocidad de ataque.
    /// </summary>
    public void PlayAttack(int handIndex, WeaponDataBase weapon, Vector3 origin, Vector3 direction, float duration)
    {
        if (!_ready || weapon == null || handIndex < 0 || handIndex > 1) return;

        // Si la mano todavia animaba un ataque anterior, se corta y se reemplaza
        if (_routines[handIndex] != null) StopCoroutine(_routines[handIndex]);
        ResetSocket(handIndex);

        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        var context = new WeaponDataBase.VisualContext
        {
            Runner = this,
            Body = transform,
            Socket = _equipment.GetSocket(handIndex),
            AtackOrigin = origin,
            AtackDirection = direction,
            Duration = Mathf.Max(duration, 0.01f),
            EffectMaterial = _effectMaterial != null ? _effectMaterial : AttackEffects.DefaultMaterial
        };

        if (_debugLogs)
            Debug.Log($"[AttackVisuals] {name} owner={IsOwner} mano={handIndex} {weapon.name} dur={context.Duration:F3}s");

        _routines[handIndex] = StartCoroutine(Run(handIndex, weapon.PlayAttackVisual(context)));
    }

    // ---------------------------------------------------------------- Internos

    // Se avanza el enumerador a mano (en vez de yield return StartCoroutine) para que
    // StopCoroutine corte tambien la animacion del arma.
    private IEnumerator Run(int handIndex, IEnumerator visual)
    {
        while (visual.MoveNext()) yield return visual.Current;

        ResetSocket(handIndex);
        _routines[handIndex] = null;
    }

    private void ResetSocket(int handIndex)
    {
        Transform socket = _equipment.GetSocket(handIndex);
        socket.localPosition = _restPosition[handIndex];
        socket.localRotation = _restRotation[handIndex];
    }
}

/// <summary>
/// Efectos simples generados por codigo (sin assets): abanico de espada, anillo de impacto y destello.
/// Los meshes se cachean; cada efecto usa una instancia propia del material para poder desvanecerse.
/// </summary>
public static class AttackEffects
{
    private static Material _defaultMaterial;
    private static readonly Dictionary<int, Mesh> _fanMeshes = new Dictionary<int, Mesh>();
    private static Mesh _ringMesh;
    private static Mesh _flashMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _defaultMaterial = null;
        _fanMeshes.Clear();
        _ringMesh = null;
        _flashMesh = null;
    }

    /// <summary>Material por defecto (Sprites/Default). Null si el shader no existe.</summary>
    public static Material DefaultMaterial
    {
        get
        {
            if (_defaultMaterial != null) return _defaultMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[AttackEffects] No se encontró el shader Sprites/Default. Asigná un Effect Material en PlayerAttackVisuals.");
                return null;
            }

            _defaultMaterial = new Material(shader);
            return _defaultMaterial;
        }
    }

    /// <summary>Abanico horizontal con el mismo angulo y alcance que el hitbox de la espada.</summary>
    public static void SpawnFan(MonoBehaviour runner, Material material, Vector3 origin, Vector3 direction,
                                float angle, float range, float life)
    {
        Spawn(runner, material, GetFanMesh(angle), "SlashFan",
              origin, Quaternion.LookRotation(direction, Vector3.up),
              new Vector3(range, 1f, range), new Color(1f, 1f, 1f, 0.45f), life, 0.85f, 1f);
    }

    /// <summary>Anillo en el suelo que se expande hasta el radio indicado y se desvanece.</summary>
    public static void SpawnRing(MonoBehaviour runner, Material material, Vector3 center, float radius, float life)
    {
        Spawn(runner, material, GetRingMesh(), "ImpactRing",
              center, Quaternion.identity,
              new Vector3(radius, 1f, radius), new Color(1f, 0.9f, 0.5f, 0.7f), life, 0.2f, 1f);
    }

    /// <summary>Destello corto, alargado en la direccion del disparo.</summary>
    public static void SpawnFlash(MonoBehaviour runner, Material material, Vector3 position, Vector3 direction,
                                  float size, float life)
    {
        Spawn(runner, material, GetFlashMesh(), "MuzzleFlash",
              position, Quaternion.LookRotation(direction, Vector3.up),
              new Vector3(size, size, size * 2f), new Color(1f, 0.95f, 0.6f, 0.9f), life, 0.6f, 1.3f);
    }

    // ---------------------------------------------------------------- Internos

    private static void Spawn(MonoBehaviour runner, Material baseMaterial, Mesh mesh, string objectName,
                              Vector3 position, Quaternion rotation, Vector3 scale, Color color,
                              float life, float startFactor, float endFactor)
    {
        if (runner == null || baseMaterial == null || mesh == null) return;

        var go = new GameObject(objectName);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale * startFactor;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var meshRenderer = go.AddComponent<MeshRenderer>();
        var material = new Material(baseMaterial) { color = color };
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        Object.Destroy(go, life + 0.1f);   // red de seguridad por si se corta la corrutina
        runner.StartCoroutine(Animate(go, material, color, scale, life, startFactor, endFactor));
    }

    private static IEnumerator Animate(GameObject go, Material material, Color color, Vector3 scale,
                                       float life, float startFactor, float endFactor)
    {
        float t = 0f;
        while (t < life && go != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);

            go.transform.localScale = scale * Mathf.Lerp(startFactor, endFactor, k);

            Color c = color;
            c.a = color.a * (1f - k);
            material.color = c;

            yield return null;
        }

        if (go != null) Object.Destroy(go);
        Object.Destroy(material);
    }

    // Sector de radio 1 en el plano XZ, centrado en +Z. Se escala al alcance real.
    private static Mesh GetFanMesh(float angle)
    {
        angle = Mathf.Clamp(angle, 1f, 360f);
        int key = Mathf.RoundToInt(angle);
        if (_fanMeshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        int segments = Mathf.Max(2, Mathf.CeilToInt(angle / 10f));
        var vertices = new Vector3[segments + 2];
        var triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;
        float half = angle * 0.5f;
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Lerp(-half, half, i / (float)segments) * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        var mesh = new Mesh { name = "SlashFan" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        _fanMeshes[key] = mesh;
        return mesh;
    }

    // Anillo plano de radio exterior 1 y radio interior 0.75.
    private static Mesh GetRingMesh()
    {
        if (_ringMesh != null) return _ringMesh;

        const int segments = 32;
        const float inner = 0.75f;
        var vertices = new Vector3[segments * 2];
        var triangles = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir;
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int b = ((i + 1) % segments) * 2;
            int t = i * 6;
            triangles[t] = a;
            triangles[t + 1] = a + 1;
            triangles[t + 2] = b + 1;
            triangles[t + 3] = a;
            triangles[t + 4] = b + 1;
            triangles[t + 5] = b;
        }

        _ringMesh = new Mesh { name = "ImpactRing" };
        _ringMesh.vertices = vertices;
        _ringMesh.triangles = triangles;
        _ringMesh.RecalculateBounds();
        return _ringMesh;
    }

    // Octaedro de radio 1 (un "diamante" 3D): se ve desde cualquier angulo de camara.
    private static Mesh GetFlashMesh()
    {
        if (_flashMesh != null) return _flashMesh;

        _flashMesh = new Mesh { name = "MuzzleFlash" };
        _flashMesh.vertices = new[]
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        _flashMesh.triangles = new[]
        {
            0, 2, 4,  4, 2, 1,  1, 2, 5,  5, 2, 0,
            0, 4, 3,  4, 1, 3,  1, 5, 3,  5, 0, 3
        };
        _flashMesh.RecalculateBounds();
        return _flashMesh;
    }
}

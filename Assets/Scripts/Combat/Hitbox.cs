using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Hitbox : MonoBehaviour
{
    [Tooltip("Reference id used by animation events (HB_On/HB_Off).")]
    public string Id = "Slash_R1";

    [Tooltip("Layers that this hitbox is allowed to damage.")]
    public LayerMask DamageMask;

    [Tooltip("Optional: minimum velocity dot (forward) required to register hits. Set 0 to ignore.")]
    public float forwardDotThreshold = 0f;

    public bool Active { get; private set; }

    public event Action<Hitbox, Collider> OnHit;

    private Collider _col;
    private Transform _owner;

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (!_col) Debug.LogError($"Hitbox '{name}' needs a Collider (IsTrigger = true).", this);
        if (_col) _col.isTrigger = true;
        _owner = transform.root;
    }

    public void SetActive(bool v)
    {
        Active = v;
        if (_col) _col.enabled = v;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Active || other.isTrigger) return;

        // Layer filter
        if ((DamageMask.value & (1 << other.gameObject.layer)) == 0) return;

        // Optional directional filter (useful for tight authoring)
        if (forwardDotThreshold > 0f)
        {
            var fwd = _owner ? _owner.forward : transform.forward;
            var toOther = (other.transform.position - transform.position).normalized;
            if (Vector3.Dot(fwd, toOther) < forwardDotThreshold) return;
        }

        OnHit?.Invoke(this, other);
    }

    // Editor gizmo helps place/size hitboxes
    void OnDrawGizmos()
    {
        Gizmos.color = Active ? new Color(0f, 1f, 0.3f, 0.35f) : new Color(1f, 0.2f, 0f, 0.15f);
        var c = GetComponent<Collider>();
        if (c is BoxCollider b)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.center, b.size);
        }
        else if (c is SphereCollider s)
        {
            Gizmos.DrawSphere(transform.TransformPoint(s.center), s.radius * Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));
        }
        else if (c is CapsuleCollider cap)
        {
            // simple capsule viz (approx)
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(cap.center, new Vector3(cap.radius * 2f, cap.height, cap.radius * 2f));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}

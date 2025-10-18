using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Hitbox))]
public class HitboxDebugProbe : MonoBehaviour
{
    private Hitbox _hb;
    private bool _lastActive;

    void Awake()
    {
        _hb = GetComponent<Hitbox>();
        _lastActive = _hb.Active;
        _hb.OnHit += OnHitContact;
    }

    void OnDestroy()
    {
        if (_hb != null) _hb.OnHit -= OnHitContact;
    }

    void Update()
    {
        if (_hb.Active != _lastActive)
        {
            _lastActive = _hb.Active;
            CombatDebugOverlay.ReportHitboxToggle(_hb, _hb.Active);
        }
    }

    private void OnHitContact(Hitbox hb, Collider other)
    {
        CombatDebugOverlay.ReportHitContact(hb, other);
    }

    void OnDrawGizmos()
    {
        // Color by active state (green when live, orange when off)
        var hb = GetComponent<Hitbox>();
        var active = hb != null && hb.Active;
        Gizmos.color = active ? new Color(0f, 1f, 0.3f, 0.25f) : new Color(1f, 0.6f, 0f, 0.15f);

        var c = GetComponent<Collider>();
        if (!c) return;

        // Draw quick shape
        if (c is BoxCollider b)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.center, b.size);
        }
        else if (c is SphereCollider s)
        {
            Gizmos.DrawSphere(transform.TransformPoint(s.center),
                s.radius * Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));
        }
        else if (c is CapsuleCollider cap)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(cap.center, new Vector3(cap.radius * 2f, cap.height, cap.radius * 2f));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}

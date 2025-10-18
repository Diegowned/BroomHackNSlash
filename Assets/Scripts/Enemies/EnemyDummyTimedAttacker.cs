using UnityEngine;

[DisallowMultipleComponent]
public class EnemyDummyTimedAttacker : MonoBehaviour
{
    [Header("Attack Timing")]
    public float windupTime = 0.5f;    // time before the hitbox turns on
    public float activeTime = 0.15f;   // hitbox on
    public float recoveryTime = 1.0f;  // after swing ends
    public bool autoStart = true;

    [Header("Damage")]
    public float damage = 8f;
    public float stun = 0.1f;
    public LayerMask hitLayers; // set to Player layer

    [Header("Hitbox Ref")]
    public Hitbox attackHitbox; // assign the child hitbox (with trigger collider)

    [Header("Debug")]
    public bool facePlayer = true;   // optional rotate to face player each swing
    public Transform player;         // optional; if empty, will try to find by tag "Player"

    private bool _running;

    void Awake()
    {
        if (!attackHitbox) attackHitbox = GetComponentInChildren<Hitbox>();
        if (!player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }

        if (attackHitbox)
        {
            attackHitbox.DamageMask = hitLayers;
            attackHitbox.SetActive(false);
            attackHitbox.OnHit += OnHitboxContact;
        }
    }

    void OnDestroy()
    {
        if (attackHitbox) attackHitbox.OnHit -= OnHitboxContact;
    }

    void Start()
    {
        if (autoStart) StartAttackLoop();
    }

    public void StartAttackLoop()
    {
        if (_running || !attackHitbox) return;
        _running = true;
        StartCoroutine(AttackLoop());
    }

    private System.Collections.IEnumerator AttackLoop()
    {
        var waitWindup = new WaitForSeconds(windupTime);
        var waitActive = new WaitForSeconds(activeTime);
        var waitRecover = new WaitForSeconds(recoveryTime);

        while (_running && attackHitbox)
        {
            // Face the player before swing (optional)
            if (facePlayer && player)
            {
                Vector3 dir = player.position - transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            // Windup
            CombatDebugOverlay.ReportHitboxToggle(attackHitbox, false);
            yield return waitWindup;

            // Active frames ON
            attackHitbox.SetActive(true);
            CombatDebugOverlay.ReportHitboxToggle(attackHitbox, true);
            yield return waitActive;

            // OFF
            attackHitbox.SetActive(false);
            CombatDebugOverlay.ReportHitboxToggle(attackHitbox, false);

            // Recovery
            yield return waitRecover;
        }
    }

    private void OnHitboxContact(Hitbox hb, Collider other)
    {
        // Apply damage to first-time contacts only (simple approach)
        if (other.TryGetComponent<IDamageable>(out var dmg))
        {
            var ctx = new DamageContext
            {
                amount   = damage,
                stunSeconds = stun,
                source   = transform,
                hitPoint = other.ClosestPoint(hb.transform.position),
                hitDirection = (other.transform.position - transform.position).normalized
            };
            dmg.TakeDamage(ctx);
            // Optional cooldown per target per swing could be added if you want only one hit per activation.
        }
    }
}

using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class DefensiveStance : MonoBehaviour
{
    [Header("Input")]
    public KeyCode holdKey = KeyCode.Mouse2; // hold to defend

    [Header("Animator")]
    public Animator animator;
    public string defendBool = "Defend";          // loop state bool
    public string defendHitTrigger = "DefendHit"; // optional visual tick on successful stance hit

    [Header("Slide (Around Attacker)")]
    [Tooltip("Angular sweep from your current side to the enemy's back (~180).")]
    public float arcDegrees = 160f;
    [Tooltip("Total time the arc movement takes.")]
    public float slideTime = 0.22f;
    [Tooltip("Keep distance from enemy along the arc.")]
    public float minDistance = 1.5f;
    public float maxDistance = 3.5f;

    [Header("Grounding")]
    [Tooltip("Radius used for ground snap & depenetration checks.")]
    public float groundProbeRadius = 0.3f;
    [Tooltip("How far down we search for ground while sliding.")]
    public float groundProbeDown = 2.5f;
    [Tooltip("Layers considered walkable ground.")]
    public LayerMask groundMask = ~0;

    [Header("Safety / i-frames")]
    public float stanceIFramesOnHit = 0.35f; // extra i-frames during slide
    public bool ignoreDamageInStance = true; // consume the hit (no HP loss)

    [Header("Facing")]
    public bool faceAttackerDuringSlide = true;

    private PlayerHealth _health;
    private bool _stanceHeld;
    private bool _sliding;
    private Coroutine _slideCo;

    // Optional helpers if present
    private CharacterController _controller;
    private CapsuleCollider _capsule; // for depenetration fallback

    void Awake()
    {
        _health = GetComponent<PlayerHealth>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        _controller = GetComponent<CharacterController>();
        _capsule = GetComponent<CapsuleCollider>();
    }

    void OnEnable()
    {
        if (_health != null) _health.BeforeDamage += OnBeforeDamage;
    }

    void OnDisable()
    {
        if (_health != null) _health.BeforeDamage -= OnBeforeDamage;
    }

    void Update()
    {
        bool wantStance = Input.GetKey(holdKey);

        if (wantStance != _stanceHeld)
        {
            _stanceHeld = wantStance;
            if (animator && !string.IsNullOrEmpty(defendBool))
                animator.SetBool(defendBool, _stanceHeld);
        }
    }

    /// <summary>
    /// Intercept incoming damage while stance is held. Return true to consume (no HP loss).
    /// </summary>
    private bool OnBeforeDamage(DamageContext ctx)
    {
        if (!_stanceHeld || _sliding) return false;

        // Visual tick
        if (animator && !string.IsNullOrEmpty(defendHitTrigger))
            animator.SetTrigger(defendHitTrigger);

        // i-frames + round the attacker
        if (_slideCo != null) StopCoroutine(_slideCo);
        _slideCo = StartCoroutine(SlideAroundAttacker(ctx));

        // Eat the hit or pass it through based on setting
        return ignoreDamageInStance;
    }

    private IEnumerator SlideAroundAttacker(DamageContext ctx)
    {
        _sliding = true;

        // Grant i-frames during the reposition
        if (_health) _health.GrantIFrames(stanceIFramesOnHit);

        Transform attacker = ctx.source ? ctx.source : null;
        if (!attacker)
        {
            // Fallback: short backstep if we don't know the attacker
            yield return StartCoroutine(ShortBackstep());
            _sliding = false;
            yield break;
        }

        Vector3 center = attacker.position;

        // Start direction from enemy -> player (on plane)
        Vector3 startFromEnemy = (transform.position - center);
        startFromEnemy.y = 0f;

        float dist = startFromEnemy.magnitude;
        if (dist < 0.001f) dist = minDistance;
        dist = Mathf.Clamp(dist, minDistance, maxDistance);

        Vector3 fromDir = (startFromEnemy.sqrMagnitude < 0.0001f) ? -attacker.forward : startFromEnemy.normalized;

        // We want to end behind the attacker (-forward)
        Vector3 toBehind = -attacker.forward;
        toBehind.y = 0f; toBehind.Normalize();

        // Choose arc sign so we rotate the short way
        float signed = Mathf.Sign(Vector3.SignedAngle(fromDir, toBehind, Vector3.up)); // +left, -right
        float totalDegrees = arcDegrees * (signed == 0 ? 1f : signed);

        float t = 0f;
        float dur = Mathf.Max(0.01f, slideTime);

        // Cache initial world pos for controller Move delta calc
        Vector3 prevPos = transform.position;

        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur);
            // gently ease
            float eased = 1f - (1f - a) * (1f - a);

            // Interpolated direction around arc
            Quaternion rot = Quaternion.AngleAxis(totalDegrees * eased, Vector3.up);
            Vector3 dir = (rot * fromDir).normalized;

            // Desired world position at this frame (horizontal)
            Vector3 flatTarget = center + dir * dist;

            // Snap to ground below that point
            Vector3 target = SnapToGround(flatTarget);

            // Move there respecting controller/physics
            MoveInstantOrController(prevPos, target);

            // Face attacker if desired
            if (faceAttackerDuringSlide)
            {
                Vector3 look = center - transform.position; look.y = 0f;
                if (look.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 0.8f);
            }

            prevPos = transform.position;
            yield return null;
        }

        // Final clamp straight behind
        Vector3 final = center + toBehind * dist;
        final = SnapToGround(final);
        MoveInstantOrController(prevPos, final);

        // Final depenetration safety if we have a collider
        ResolvePenetrationIfStuck();

        _sliding = false;
    }

    private Vector3 SnapToGround(Vector3 horizontalPos)
    {
        // Cast from a bit above desired point, straight down to find ground
        Vector3 origin = horizontalPos + Vector3.up * (groundProbeDown * 0.5f + 0.5f);
        float castDist = groundProbeDown + 1.0f;

        if (Physics.SphereCast(origin, groundProbeRadius, Vector3.down, out RaycastHit hit, castDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            return new Vector3(horizontalPos.x, hit.point.y, horizontalPos.z);
        }

        // If no ground found, keep current Y (prevents big pops if moving over pits)
        return new Vector3(horizontalPos.x, transform.position.y, horizontalPos.z);
    }

    private void MoveInstantOrController(Vector3 from, Vector3 to)
    {
        if (_controller)
        {
            // Use CharacterController to respect steps/skin & grounded logic
            Vector3 delta = to - from;
            // Small downward bias helps maintain contact on slopes
            delta += Vector3.down * 0.02f;
            _controller.Move(delta);
        }
        else
        {
            transform.position = to;
        }
    }

    private void ResolvePenetrationIfStuck()
    {
        // Try to gently push out of geometry if intersecting
        Vector3 pos = transform.position;

        // Prefer CharacterController shape if present
        if (_controller)
        {
            Vector3 center = pos + _controller.center;
            float radius = _controller.radius;
            float height = Mathf.Max(_controller.height, radius * 2f);

            Collider[] overlaps = Physics.OverlapCapsule(
                center + Vector3.up * (height * 0.5f - radius),
                center - Vector3.up * (height * 0.5f - radius),
                radius,
                ~0,
                QueryTriggerInteraction.Ignore);

            foreach (var col in overlaps)
            {
                if (!col || col.attachedRigidbody == GetComponent<Rigidbody>()) continue;

                if (Physics.ComputePenetration(
                    col, col.bounds.center, col.transform.rotation,
                    _controller, center, transform.rotation,
                    out Vector3 dir, out float dist))
                {
                    Vector3 depen = dir * dist;
                    _controller.Move(depen + Vector3.up * 0.01f);
                }
            }
        }
        else if (_capsule)
        {
            Vector3 worldCenter = pos + _capsule.center;
            float radius = _capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            float height = Mathf.Max(_capsule.height * transform.lossyScale.y, radius * 2f);

            Vector3 p1 = worldCenter + Vector3.up * (height * 0.5f - radius);
            Vector3 p2 = worldCenter - Vector3.up * (height * 0.5f - radius);

            Collider[] overlaps = Physics.OverlapCapsule(p1, p2, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in overlaps)
            {
                if (!col || col.attachedRigidbody == GetComponent<Rigidbody>()) continue;

                if (Physics.ComputePenetration(
                    col, col.bounds.center, col.transform.rotation,
                    _capsule, worldCenter, transform.rotation,
                    out Vector3 dir, out float dist))
                {
                    transform.position += dir * dist + Vector3.up * 0.005f;
                }
            }
        }
        // If neither controller nor capsule present, we skip (very rare in your setup).
    }

    private IEnumerator ShortBackstep()
    {
        float dur = Mathf.Max(0.01f, slideTime * 0.6f);
        Vector3 start = transform.position;
        Vector3 endFlat = start - transform.forward * 1.5f;
        Vector3 end = SnapToGround(endFlat);

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.SmoothStep(0f, 1f, t / dur);
            Vector3 pos = Vector3.Lerp(start, end, a);
            MoveInstantOrController(transform.position, pos);
            yield return null;
        }

        ResolvePenetrationIfStuck();
    }
}

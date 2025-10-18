using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerCombat : MonoBehaviour
{


    [Header("Input")]
    [Tooltip("Input button name for light attack (legacy Input Manager).")]
    public string lightAttackButton = "Fire1"; // LMB / Ctrl by default; change in Input settings

    [Header("Attack Data")]
    public float lightAttackDamage = 12f;
    public float lightAttackStun = 0.2f;
    public float lightAttackCooldown = 0.15f;
    public LayerMask damageToLayers; // e.g., Enemy layer

    [Header("References")]
    public List<Hitbox> hitboxes; // Assign child hitboxes in Inspector
    public Camera mainCamera;     // Optional, for FOV pulses etc.

    private Animator _anim;
    private bool _attackLocked;   // gate input during active attack/cancel windows
    private float _cooldownTimer;

    // Per-activation registry: cleared when we turn a hitbox on
    private readonly Dictionary<Hitbox, HashSet<Collider>> _alreadyHit = new();

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (!mainCamera) mainCamera = Camera.main;

        // Wire hitboxes to us
        foreach (var hb in hitboxes)
        {
            if (!hb) continue;
            hb.OnHit += OnHitboxContact;
            hb.SetActive(false);
            _alreadyHit[hb] = new HashSet<Collider>();
            hb.DamageMask = damageToLayers;
        }
    }

    void Update()
    {
        // simple cooldown
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        // Input → light attack trigger
        if (!_attackLocked && _cooldownTimer <= 0f && Input.GetButtonDown(lightAttackButton))
        {
            _anim.ResetTrigger("LightAttack"); // optional safety
            _anim.SetTrigger("LightAttack");
            _attackLocked = true;              // unlocked by Attack_End() event
        }
    }

    #region Animation Events (drop these on frames in your clip)
    // Called at the first frame the attack should begin (movement cancels, etc.)
    public void Attack_Begin()
    {
        // Tiny FOV pulse feedback (if you’re using my DmcCameraRig: call SetWideFOV or PulseFOV from there)
        // var rig = mainCamera ? mainCamera.GetComponentInChildren<BroomHackNSlash.CameraSystem.DmcCameraRig>() : null;
        // if (rig) rig.PulseFOV(6f, 0.18f);
    }

    // Called where the hitbox should be ON (you control timing per clip)
    // Example event: HB_On("Slash_R1")
    public void HB_On(string hitboxId)
    {
        var hb = FindHitbox(hitboxId);
        if (!hb) return;
        _alreadyHit[hb].Clear(); // ensure fresh hit registry for this activation
        hb.SetActive(true);
    }

    // Called when the hitbox should be OFF
    // Example event: HB_Off("Slash_R1")
    public void HB_Off(string hitboxId)
    {
        var hb = FindHitbox(hitboxId);
        if (!hb) return;
        hb.SetActive(false);
    }

    // Called on the final frame of the attack animation (or when you open a cancel window)
    public void Attack_End()
    {
        _attackLocked = false;
        _cooldownTimer = lightAttackCooldown;
        // Safety: ensure all hitboxes are off
        foreach (var hb in hitboxes) if (hb) hb.SetActive(false);
    }
    #endregion

    private Hitbox FindHitbox(string id)
    {
        foreach (var hb in hitboxes) if (hb && hb.Id == id) return hb;
        return null;
    }

    // Receives hit notifications from any active Hitbox
    private void OnHitboxContact(Hitbox hb, Collider other)
    {
        if (!_alreadyHit.TryGetValue(hb, out var set)) return;
        if (set.Contains(other)) return; // already hit this target during this activation

        set.Add(other);

        // Apply damage if target has IDamageable
        if (other.TryGetComponent<IDamageable>(out var dmg))
        {
            var ctx = new DamageContext
            {
                amount = lightAttackDamage,
                stunSeconds = lightAttackStun,
                source = transform,
                hitPoint = other.ClosestPoint(hb.transform.position),
                hitDirection = (other.transform.position - transform.position).normalized
            };
            dmg.TakeDamage(ctx);
            CombatDebugOverlay.ReportDamage(ctx, other);

        }
    }
}

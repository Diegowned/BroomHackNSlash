using UnityEngine;
using System;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHP = 100f;
    public float iFrameSeconds = 0.5f;

    [Header("Knockback (optional)")]
    public float knockbackForce = 6f;
    public float knockbackUp = 0.0f;

    [Header("Hooks")]
    public Animator animator; 
    public string hurtTriggerName = "Hurt";

    private float _hp;
    private float _iFrameTimer;

    // >>> NEW: let systems intercept/consume damage (return true to consume)
    public event Func<DamageContext, bool> BeforeDamage;

    void Awake()
    {
        _hp = maxHP;
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (_iFrameTimer > 0f) _iFrameTimer -= Time.deltaTime;
    }

    // >>> NEW: external i-frame grant (used by stance)
    public void GrantIFrames(float seconds)
    {
        _iFrameTimer = Mathf.Max(_iFrameTimer, seconds);
    }

    // >>> helper to invoke all pre-damage handlers
    private bool InvokeBeforeDamage(DamageContext ctx)
    {
        if (BeforeDamage == null) return false;
        foreach (Func<DamageContext, bool> h in BeforeDamage.GetInvocationList())
            if (h(ctx)) return true; // consumed
        return false;
    }

    public void TakeDamage(DamageContext ctx)
    {
        // >>> NEW: allow stances/parries to consume the hit
        if (InvokeBeforeDamage(ctx)) return;

        if (_iFrameTimer > 0f) return;

        _hp = Mathf.Max(0f, _hp - ctx.amount);
        _iFrameTimer = iFrameSeconds;

        var body = GetComponent<Rigidbody>();
        if (body)
        {
            var kb = ctx.hitDirection.normalized * knockbackForce + Vector3.up * knockbackUp;
            body.AddForce(kb, ForceMode.VelocityChange);
        }

        if (animator && !string.IsNullOrEmpty(hurtTriggerName))
            animator.SetTrigger(hurtTriggerName);

        CombatDebugOverlay.ReportDamage(ctx, this);

        if (_hp <= 0f) OnDeath();
    }

    private void OnDeath()
    {
        Debug.Log("Player died.");
        var combat = GetComponent<PlayerCombat>();
        if (combat) combat.enabled = false;
    }

    void OnGUI()
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(12, 12, 250, 22), $"Player HP: {_hp:0}/{maxHP:0}");
    }

    public void Heal(float amount)
    {
        _hp = Mathf.Min(maxHP, _hp + Mathf.Abs(amount));
    }
}

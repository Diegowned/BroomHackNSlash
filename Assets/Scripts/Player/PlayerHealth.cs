using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHP = 100f;
    public float iFrameSeconds = 0.5f;

    [Header("Knockback (optional)")]
    public float knockbackForce = 6f;
    public float knockbackUp = 0.0f;

    [Header("Hooks")]
    public Animator animator; // optional: set a "Hurt" trigger
    public string hurtTriggerName = "Hurt";

    private float _hp;
    private float _iFrameTimer;

    void Awake()
    {
        _hp = maxHP;
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (_iFrameTimer > 0f) _iFrameTimer -= Time.deltaTime;
    }

    public void TakeDamage(DamageContext ctx)
    {
        if (_iFrameTimer > 0f) return;

        _hp = Mathf.Max(0f, _hp - ctx.amount);
        _iFrameTimer = iFrameSeconds;

        // Knockback (very light; adapt to your controller if needed)
        var body = GetComponent<Rigidbody>();
        if (body)
        {
            var kb = ctx.hitDirection.normalized * knockbackForce + Vector3.up * knockbackUp;
            body.AddForce(kb, ForceMode.VelocityChange);
        }

        if (animator && !string.IsNullOrEmpty(hurtTriggerName))
            animator.SetTrigger(hurtTriggerName);

        // Debug overlay
        CombatDebugOverlay.ReportDamage(ctx, this);

        if (_hp <= 0f) OnDeath();
    }

    private void OnDeath()
    {
        Debug.Log("Player died.");
        // TODO: ragdoll/respawn. For now, just disable input/combat.
        var combat = GetComponent<PlayerCombat>();
        if (combat) combat.enabled = false;
        // (Optional) reload scene or respawn logic here.
    }

    // Tiny HUD for quick testing
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

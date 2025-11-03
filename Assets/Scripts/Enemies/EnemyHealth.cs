using System;
using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable, IHealthReadable
{
    [Header("Health")]
    public float maxHP = 100f;
    public bool destroyOnDeath = true;

    [Header("Feedback (optional)")]
    public Animator animator;
    public string hitTrigger = "Hit";
    public string deathTrigger = "Death";

    private float _hp;
    private Rigidbody _rb;

    public float CurrentHP => _hp;
    public float MaxHP => maxHP;
    public bool IsDead => _hp <= 0f;
    public event Action<float, float> OnHealthChanged;

    void Awake()
    {
        _hp = Mathf.Max(1f, maxHP);
        _rb = GetComponent<Rigidbody>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        OnHealthChanged?.Invoke(_hp, maxHP);
    }

    public void TakeDamage(DamageContext ctx)
    {
        if (IsDead) return;

        _hp = Mathf.Max(0f, _hp - Mathf.Max(0f, ctx.amount));
        OnHealthChanged?.Invoke(_hp, maxHP);

        if (ctx.launchForce > 0 && _rb != null)
        {
            _rb.velocity = Vector3.zero; // Reset velocity before applying new force
            _rb.AddForce(Vector3.up * ctx.launchForce, ForceMode.Impulse);
        }

        if (animator && !string.IsNullOrEmpty(hitTrigger)) animator.SetTrigger(hitTrigger);

        if (_hp <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        _hp = Mathf.Min(maxHP, _hp + Mathf.Abs(amount));
        OnHealthChanged?.Invoke(_hp, maxHP);
    }

    private void Die()
    {
        if (animator && !string.IsNullOrEmpty(deathTrigger)) animator.SetTrigger(deathTrigger);
        if (destroyOnDeath)
        {
            // small delay lets death anim fire; tweak or remove as you wish
            Destroy(gameObject, 0.2f);
        }
    }
}

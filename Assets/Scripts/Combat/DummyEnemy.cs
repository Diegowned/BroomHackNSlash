using UnityEngine;

public class DummyEnemy : MonoBehaviour, IDamageable
{
    public float maxHP = 60f;
    private float _hp;

    void Awake() => _hp = maxHP;

    public void TakeDamage(DamageContext ctx)
    {
        _hp -= ctx.amount;
        // TODO: play hit VFX/SFX, apply knockback, start stun timer, etc.
        Debug.Log($"{name} took {ctx.amount} dmg from {ctx.source?.name}. HP now {_hp:0.}.");
        if (_hp <= 0f) Die();
    }

    private void Die()
    {
        Debug.Log($"{name} defeated.");
        // Destroy or play death anim.
        Destroy(gameObject);
    }
}

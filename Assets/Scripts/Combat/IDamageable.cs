using UnityEngine;

public interface IDamageable
{
    void TakeDamage(DamageContext ctx);
}

public struct DamageContext
{
    public float amount;
    public float stunSeconds;
    public Transform source;
    public Vector3 hitPoint;
    public Vector3 hitDirection;
}

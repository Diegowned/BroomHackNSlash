using System;

public interface IHealthReadable
{
    float CurrentHP { get; }
    float MaxHP { get; }
    bool IsDead { get; }

    event Action<float, float> OnHealthChanged;
}

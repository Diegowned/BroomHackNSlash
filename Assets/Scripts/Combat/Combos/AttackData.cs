using System.Collections.Generic;
using UnityEngine;

namespace BroomHackNSlash.Combat
{
    public enum AttackDirection
    {
        Neutral,
        Forward,
        Backward
    }

    [CreateAssetMenu(fileName = "NewAttack", menuName = "Combat/Attack Data")]
    public class AttackData : ScriptableObject
    {
    [Header("Identity")]
    public string attackName;

    [Header("Animation")]
    public string animationTrigger;
    public AnimationClip clip;

    [Header("Combat Properties")]
    public float damage = 10f;
    public float stunSeconds = 0.2f;
    public float launchForce = 0f;

    [Header("Timings (Frames)")]
    [Tooltip("Frame where the combo window opens.")]
    public int comboWindowStartFrame;
    [Tooltip("Frame where the combo window closes.")]
    public int comboWindowEndFrame;
    [Tooltip("Total frames in the attack animation before returning to idle.")]
    public int recoveryFrames;

    [Header("Follow-Up Attacks")]
    public List<FollowUpAttack> followUps;
}

[System.Serializable]
public class FollowUpAttack
{
    public AttackDirection requiredDirection;
    public AttackData nextAttack;
}
}

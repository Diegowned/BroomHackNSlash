using UnityEngine;

[CreateAssetMenu(fileName = "AttackStep", menuName = "Combat/Attack Step", order = 0)]
public class AttackStepSO : ScriptableObject
{
    [Header("Identity")]
    public string stepId = "L1";          // e.g., "L1", "L2", "M1"
    public string animatorTrigger = "Light_L1"; // trigger name in Animator

    [Header("Optional docs (not enforced)")]
    public float startup;   // frames or seconds (for your notes)
    public float active;
    public float recovery;

    [Tooltip("If true, and no branch input arrives, this step can replay itself (hold to mash).")]
    public bool canLoop;

    [Header("Branches")]
    public AttackStepSO onLight;
    public AttackStepSO onMedium;

    [Header("Windows")]
    [Tooltip("How long we keep a pressed button buffered waiting for the cancel window (sec).")]
    public float inputBufferTime = 0.25f;

    [Tooltip("Max time after finishing an attack to continue the chain before reset (sec).")]
    public float chainResetTime = 0.9f;
}

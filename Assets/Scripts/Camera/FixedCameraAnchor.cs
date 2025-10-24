// =============================
// FixedCameraAnchor.cs
// Place multiple of these in your scene at designer-picked spots.
// The camera will snap/smooth to the active anchor (picked by zone or nearest).
// =============================
using UnityEngine;

[ExecuteAlways]
public class FixedCameraAnchor : MonoBehaviour
{
    [Header("Anchor Pose (used directly by the Camera)")]
    public Vector3 position;
    public Vector3 eulerRotation;

    [Header("Framing")]
    [Tooltip("How strongly this anchor tries to keep the player centered (0 = look straight ahead, 1 = always look at player)")]
    [Range(0,1)] public float lookAtPlayerWeight = 0.8f;

    [Tooltip("Optional tilt added when looking at target")] public float extraPitchTowardTarget = 0f;

    [Header("Priority (only used when multiple zones overlap)")]
    public int priority = 0;

    [Header("Debug")] public Color gizmoColor = new Color(0.2f,0.8f,1f,0.5f);

    public Vector3 WorldPosition => Application.isPlaying ? transform.position : transform.position = transform.TransformPoint(Vector3.zero);
    public Quaternion WorldRotation => Application.isPlaying ? transform.rotation : transform.rotation = Quaternion.Euler(eulerRotation);

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(eulerRotation);
        }
    }
#endif

    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, 0.25f);
        Gizmos.DrawRay(transform.position, transform.forward * 0.6f);
    }
}

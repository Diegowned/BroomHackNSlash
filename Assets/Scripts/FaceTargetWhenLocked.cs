using UnityEngine;
using BroomHackNSlash.CameraSystem; // for DmcCameraRig

[DisallowMultipleComponent]
public class FaceTargetWhenLocked : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your DmcCameraRig (usually on Main Camera). If left empty, will try Camera.main.")]
    public DmcCameraRig cameraRig;

    [Header("Facing")]
    [Tooltip("Degrees per second to rotate toward the target while locked.")]
    public float facingTurnSpeed = 900f;

    [Tooltip("Ignore micro-rotations when very close to perfect facing.")]
    public float yawEpsilon = 0.5f;

    [Tooltip("If the target is closer than this, still face it but clamp twitch by smoothing.")]
    public float minFacingDistance = 0.2f;

    void Reset()
    {
        // Convenience auto-hook
        if (!cameraRig && Camera.main)
            cameraRig = Camera.main.GetComponent<DmcCameraRig>();
    }

    void LateUpdate()
    {
        if (!cameraRig || !cameraRig.IsLocked || !cameraRig.CurrentLockTarget) return;

        var target = cameraRig.CurrentLockTarget;
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float sqrMag = to.sqrMagnitude;
        if (sqrMag < 0.0001f) return;

        // Normalize horizontally; tiny distances get normalized too but we keep smoothing to avoid jitter
        Vector3 dir = to.normalized;

        // Desired yaw-only rotation
        Quaternion desired = Quaternion.LookRotation(dir, Vector3.up);

        // Smoothly rotate toward target
        float maxStep = facingTurnSpeed * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, maxStep);

        // Snap if we’re basically there to prevent micro jitter
        float yawDelta = Quaternion.Angle(transform.rotation, desired);
        if (yawDelta <= yawEpsilon) transform.rotation = desired;
    }
}

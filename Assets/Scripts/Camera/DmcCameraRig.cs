
using System.Collections.Generic;
using UnityEngine;

namespace BroomHackNSlash.CameraSystem
{
    [RequireComponent(typeof(Camera))]
    public class DmcCameraRig : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Usually a child on the player at chest/head height")] public Transform followTarget;
        [Tooltip("All fixed anchors in the scene; auto-discovers if left empty")] public List<FixedCameraAnchor> anchors = new List<FixedCameraAnchor>();
        [Tooltip("Optional: zones to drive anchor choice; if empty, nearest anchor is used")] public List<FixedCameraZone> zones = new List<FixedCameraZone>();

        [Header("Smoothing")]
        public float positionSmoothTime = 0.08f;
        public float rotationLerp = 14f;

        [Header("Collision")]
        public LayerMask collisionMask = ~0;
        public float collisionSphereRadius = 0.25f;
        public float collisionBuffer = 0.1f;

        [Header("Lock-On (optional)")]
        public string lockOnButton = "LockOn";
        public string enemyTag = "Enemy";
        public float lockOnRadius = 18f;
        public float lockOnFOV = 80f;

        [Header("Target Switching")]
        public string switchLeftButton = "SwitchLeft";
        public string switchRightButton = "SwitchRight";
        [Tooltip("Seconds between switch inputs")] public float switchCooldown = 0.25f;

        [Header("Framing")]
        [Tooltip("Vertical offset applied to followTarget when framing")] public float followHeightOffset = 0.9f;
        [Tooltip("How strongly to look at the midpoint between player and lock target when locked")] [Range(0,1)] public float lockMidWeight = 0.55f;

        private Camera _cam;
        private Vector3 _posVel;
        private FixedCameraAnchor _activeAnchor;

        // --- keep public API for FaceTargetWhenLocked ---
        private Transform _currentLockTarget;
        private bool _isLocked;
        public bool IsLocked => _isLocked;
        public Transform CurrentLockTarget => _currentLockTarget;
        public System.Action<Transform> OnLockTargetChanged;

        // switching cooldown
        private float _nextSwitchTime = 0f;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (anchors == null || anchors.Count == 0)
                anchors = new List<FixedCameraAnchor>(FindObjectsOfType<FixedCameraAnchor>());
            if (zones == null || zones.Count == 0)
                zones = new List<FixedCameraZone>(FindObjectsOfType<FixedCameraZone>());
            PickInitialAnchor();
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (!followTarget) return;

            // Hold-to-lock
            if (Input.GetButtonDown(lockOnButton))
            {
                AcquireLockTarget();
                _isLocked = _currentLockTarget != null;
            }
            else if (Input.GetButtonUp(lockOnButton))
            {
                _isLocked = false;
                SetCurrentTarget(null);
            }

            // Switch targets while locked
            if (_isLocked && Time.time >= _nextSwitchTime)
            {
                if (Input.GetButtonDown(switchRightButton)) { if (SwitchTarget(true)) _nextSwitchTime = Time.time + switchCooldown; }
                else if (Input.GetButtonDown(switchLeftButton)) { if (SwitchTarget(false)) _nextSwitchTime = Time.time + switchCooldown; }
            }

            // Anchor selection
            SelectAnchor();

            // Camera placement
            UpdateCameraPose();
        }

        void PickInitialAnchor()
        {
            _activeAnchor = GetNearestAnchor(followTarget ? followTarget.position : transform.position);
        }

        FixedCameraAnchor GetNearestAnchor(Vector3 point)
        {
            float best = float.PositiveInfinity; FixedCameraAnchor bestA = null;
            foreach (var a in anchors)
            {
                if (!a) continue;
                float d = (a.transform.position - point).sqrMagnitude;
                if (d < best) { best = d; bestA = a; }
            }
            return bestA;
        }

        void SelectAnchor()
        {
            FixedCameraAnchor chosen = _activeAnchor;
            int bestPriority = int.MinValue;

            if (zones != null && zones.Count > 0)
            {
                foreach (var z in zones)
                {
                    if (!z || !z.anchor) continue;
                    if (z.ContainsPoint(followTarget.position))
                    {
                        int pri = Mathf.Max(z.priority, z.anchor.priority);
                        if (pri > bestPriority)
                        {
                            bestPriority = pri;
                            chosen = z.anchor;
                        }
                    }
                }
            }

            // Fallback: nearest
            if (!chosen)
                chosen = GetNearestAnchor(followTarget.position);

            if (chosen && chosen != _activeAnchor)
                _activeAnchor = chosen;
        }

        void UpdateCameraPose()
        {
            if (!_activeAnchor) return;

            // Desired position/rotation come from anchor
            Vector3 desiredPos = _activeAnchor.transform.position;
            Quaternion desiredRot = _activeAnchor.transform.rotation;

            // Collision push-in: sphere cast from anchor back toward player framing point
            Vector3 framePoint = followTarget.position + Vector3.up * followHeightOffset;

            Vector3 camToFrame = (framePoint - desiredPos);
            float maxDist = camToFrame.magnitude;
            Vector3 dir = camToFrame.normalized;

            if (Physics.SphereCast(framePoint, collisionSphereRadius, -dir, out RaycastHit hit, maxDist, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float clipped = Mathf.Max(0.0f, maxDist - hit.distance + collisionBuffer);
                desiredPos += dir * clipped; // push camera toward frame point to avoid clipping into walls
            }

            // Smooth position
            Vector3 newPos = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, positionSmoothTime);
            transform.position = newPos;

            // Compute look target: blend between anchor forward and dynamic look-at
            Vector3 lookTarget = framePoint;
            if (_isLocked && _currentLockTarget)
            {
                Vector3 enemyHead = _currentLockTarget.position + Vector3.up * 0.9f;
                lookTarget = Vector3.Lerp(framePoint, (framePoint + enemyHead) * 0.5f, lockMidWeight);
            }

            // Desired rotation from anchor forward toward lookTarget by weight
            Vector3 anchorFwd = _activeAnchor.transform.forward;
            Vector3 toLook = (lookTarget - newPos).normalized;

            Quaternion rotToLook = Quaternion.LookRotation(toLook, Vector3.up);
            Quaternion rotAnchor = _activeAnchor.transform.rotation;

            float w = _activeAnchor.lookAtPlayerWeight;
            Quaternion targetRot = Quaternion.Slerp(rotAnchor, rotToLook, w);

            // Extra pitch tilt if desired
            if (_activeAnchor.extraPitchTowardTarget != 0f)
            {
                Vector3 e = targetRot.eulerAngles;
                e.x += _activeAnchor.extraPitchTowardTarget;
                targetRot = Quaternion.Euler(e);
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationLerp * Time.deltaTime);
        }

        // -------- Lock-on (simplified for fixed camera) --------
        void AcquireLockTarget()
        {
            Transform best = null;
            float bestScore = float.PositiveInfinity;
            Vector3 origin = transform.position;

            foreach (var col in Physics.OverlapSphere(followTarget.position, lockOnRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!col || !col.CompareTag(enemyTag)) continue;
                Transform t = col.transform;
                if (!HasLineOfSight(origin, t)) continue;
                Vector3 to = (t.position - origin);
                float ang = Vector3.Angle(transform.forward, to);
                if (ang > lockOnFOV) continue;

                float dist = to.magnitude;
                float score = ang * 1.5f + dist * 0.1f; // prefer centered and closer
                if (score < bestScore) { bestScore = score; best = t; }
            }
            SetCurrentTarget(best);
        }

        bool SwitchTarget(bool toRight)
        {
            if (!_currentLockTarget) { AcquireLockTarget(); return _currentLockTarget != null; }

            var candidates = new List<Transform>();
            foreach (var col in Physics.OverlapSphere(followTarget.position, lockOnRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!col || !col.CompareTag(enemyTag)) continue;
                Transform t = col.transform;
                if (t == _currentLockTarget) continue;
                if (!WithinFOV(t)) continue;
                if (!HasLineOfSight(transform.position, t)) continue;
                candidates.Add(t);
            }
            if (candidates.Count == 0) return false;

            // screen-space (viewport X) ordering, relative to current target
            Vector3 curVp = _cam.WorldToViewportPoint(_currentLockTarget.position + Vector3.up * 0.9f);
            float bestDelta = toRight ? float.PositiveInfinity : float.NegativeInfinity;
            Transform pick = null;

            foreach (var t in candidates)
            {
                Vector3 vp = _cam.WorldToViewportPoint(t.position + Vector3.up * 0.9f);
                float dx = vp.x - curVp.x;
                if (toRight)
                {
                    if (dx > 0f && dx < bestDelta) { bestDelta = dx; pick = t; }
                }
                else
                {
                    if (dx < 0f && dx > bestDelta) { bestDelta = dx; pick = t; }
                }
            }

            // wrap-around if none on that side
            if (!pick)
            {
                foreach (var t in candidates)
                {
                    Vector3 vp = _cam.WorldToViewportPoint(t.position + Vector3.up * 0.9f);
                    float dx = vp.x - curVp.x;
                    if (toRight)
                    {
                        if (dx <= 0f && (pick == null || dx > bestDelta)) { bestDelta = dx; pick = t; }
                    }
                    else
                    {
                        if (dx >= 0f && (pick == null || dx < bestDelta)) { bestDelta = dx; pick = t; }
                    }
                }
            }

            if (pick)
            {
                SetCurrentTarget(pick);
                return true;
            }
            return false;
        }

        bool WithinFOV(Transform t)
        {
            Vector3 to = t.position - transform.position;
            return Vector3.Angle(transform.forward, to) <= lockOnFOV;
        }

        bool HasLineOfSight(Vector3 origin, Transform t)
        {
            Vector3 head = t.position + Vector3.up * 1.0f;
            if (Physics.Linecast(origin, head, out RaycastHit hit, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(t) && hit.transform != t) return false;
            }
            return true;
        }

        void SetCurrentTarget(Transform t)
        {
            if (_currentLockTarget == t) return;
            _currentLockTarget = t;
            OnLockTargetChanged?.Invoke(_currentLockTarget);
        }

        // Debug
        void OnDrawGizmosSelected()
        {
            if (followTarget)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(followTarget.position, lockOnRadius);
            }
        }
    }
}

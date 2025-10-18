using System;
using System.Collections.Generic;
using UnityEngine;

namespace BroomHackNSlash.CameraSystem
{
    /// <summary>
    /// Devil May Cry-style third-person camera:
    /// - Free-look orbit (mouse/controller via legacy axes)
    /// - Soft lock-on with target cycling (Q/E or stick flick)
    /// - Fixed shoulder bias (no swapping)
    /// - No auto-recenter when idle
    /// - Camera collision via sphere cast
    /// - FOV pulse hooks
    /// - Cursor lock/hide
    /// Expose IsLocked/CurrentLockTarget and OnLockTargetChanged for UI (e.g., reticle).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DmcCameraRig : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Usually a child on the player (e.g., CameraFollowTarget at ~1.6m).")]
        public Transform followTarget;

        [Header("Orbit")]
        public float distance = 5.5f;
        public float minDistance = 1.0f;
        public float maxDistance = 7.5f;
        public float mouseSensitivity = 140f;      // deg/sec per mouse unit
        public float controllerSensitivity = 220f; // deg/sec per stick unit
        public float pitchMin = -20f;
        public float pitchMax = 70f;

        [Tooltip("Disable any auto recentering when there is no look input. (Kept for clarity; no recenter code is active.)")]
        public bool disableIdleRecenter = true;

        [Header("Lock-On")]
        public KeyCode lockOnKey = KeyCode.Tab;
        public string enemyTag = "Enemy";
        public float lockOnRadius = 18f;
        public float lockOnFOV = 65f;
        [Tooltip("Horizontal offset (meters) to bias the camera to a shoulder when locked.")]
        public float shoulderOffset = 1.1f;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.06f;
        public float rotationLerp = 20f;

        [Header("Collision")]
        public LayerMask collisionMask = ~0;
        public float collisionSphereRadius = 0.25f;
        public float collisionBuffer = 0.1f;

        [Header("FOV")]
        public float defaultFOV = 60f;
        public float sprintOrAttackFOV = 68f;
        public float fovLerp = 6f;

        [Header("Input Axes (Legacy)")]
        public string lookX = "Mouse X";
        public string lookY = "Mouse Y";

        [Header("Lock-On: Cycling")]
        public KeyCode prevTargetKey = KeyCode.Q;   // cycle left
        public KeyCode nextTargetKey = KeyCode.E;   // cycle right

        [Tooltip("Optional axis for cycling (e.g., map Right Stick X). Leave empty to ignore.")]
        public string cycleAxis = "";               // e.g., "LockCycle"
        public float cycleFlickThreshold = 0.6f;
        public float cycleAxisDebounce = 0.25f;

        // --- Public accessors / event for UI ---
        public bool IsLocked => _isLocked;
        public Transform CurrentLockTarget => _currentLockTarget;
        public Action<Transform> OnLockTargetChanged;

        // --- State ---
        private Transform _currentLockTarget;
        private bool _isLocked;
        private int _shoulderSign = 1; // fixed to right shoulder (no swapping)
        private float _yaw;
        private float _pitch;
        private float _desiredDistance;
        private Vector3 _posVel;
        private Camera _cam;
        private float _targetFOV;
        private float _cycleAxisCooldown;

        // ---------- Lifecycle ----------
        void Awake()
        {
            _cam = GetComponent<Camera>();
            _targetFOV = defaultFOV;
            _cam.fieldOfView = defaultFOV;
            _desiredDistance = Mathf.Clamp(distance, minDistance, maxDistance);

            if (followTarget != null)
            {
                Vector3 toCam = (transform.position - followTarget.position).normalized;
                _yaw = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
                _pitch = Mathf.Asin(toCam.y) * Mathf.Rad2Deg;
            }
        }

        void Start()
        {
            // Lock & hide cursor while playing (Esc to free if you add your own pause/menu logic)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (!followTarget) return;

            HandleInput();
            HandleLockOnTargeting();
            UpdateCameraTransform();
            UpdateFOV();
        }

        // ---------- Input & Lock ----------
        private void HandleInput()
        {
            // Toggle lock-on
            if (Input.GetKeyDown(lockOnKey))
            {
                if (_isLocked && _currentLockTarget)
                {
                    _isLocked = false;
                    SetCurrentTarget(null);
                }
                else
                {
                    AcquireLockTarget();
                    _isLocked = _currentLockTarget != null;
                }
            }

            // Look input
            float lx = Input.GetAxisRaw(lookX);
            float ly = Input.GetAxisRaw(lookY);
            bool hasLookInput = Mathf.Abs(lx) > 0.001f || Mathf.Abs(ly) > 0.001f;

            if (!_isLocked)
            {
                // Free-look only (no recentering while idle)
                float sens = IsMouseMoving() ? mouseSensitivity : controllerSensitivity;
                _yaw += lx * sens * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch - ly * sens * Time.deltaTime, pitchMin, pitchMax);
            }
            else
            {
                // In lock-on, steer camera to frame player + target; allow gentle manual nudge
                if (_currentLockTarget)
                {
                    Vector3 dirToTarget = _currentLockTarget.position - followTarget.position;
                    dirToTarget.y = 0f;
                    if (dirToTarget.sqrMagnitude < 0.01f) dirToTarget = followTarget.forward;

                    float desiredYaw = Mathf.Atan2(dirToTarget.x, dirToTarget.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, desiredYaw, rotationLerp * Time.deltaTime);

                    Vector3 mid = Vector3.Lerp(followTarget.position, _currentLockTarget.position, 0.45f);
                    Vector3 lookDir = (mid - transform.position).normalized;
                    float desiredPitch = Mathf.Asin(lookDir.y) * Mathf.Rad2Deg;
                    _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(desiredPitch, pitchMin, pitchMax), rotationLerp * Time.deltaTime);

                    // Small manual nudges
                    _yaw += lx * 40f * Time.deltaTime;
                    _pitch = Mathf.Clamp(_pitch - ly * 40f * Time.deltaTime, pitchMin, pitchMax);
                }
            }

            // Scroll wheel zoom
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                _desiredDistance = Mathf.Clamp(_desiredDistance - scroll * 3.0f, minDistance, maxDistance);

            // --- Target cycling while locked ---
            if (_isLocked && _currentLockTarget)
            {
                // Keyboard
                if (Input.GetKeyDown(prevTargetKey)) CycleLockTarget(-1);
                else if (Input.GetKeyDown(nextTargetKey)) CycleLockTarget(+1);

                // Optional stick flick
                if (!string.IsNullOrEmpty(cycleAxis))
                {
                    if (_cycleAxisCooldown > 0f) _cycleAxisCooldown -= Time.deltaTime;
                    float ax = Input.GetAxisRaw(cycleAxis);
                    if (_cycleAxisCooldown <= 0f && Mathf.Abs(ax) >= cycleFlickThreshold)
                    {
                        CycleLockTarget(Mathf.Sign(ax) > 0 ? +1 : -1);
                        _cycleAxisCooldown = cycleAxisDebounce;
                    }
                }
            }
        }

        private bool IsMouseMoving()
        {
            return Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.0001f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.0001f;
        }

        private void HandleLockOnTargeting()
        {
            if (!_isLocked) return;

            // If current target is invalid, try to reacquire; otherwise drop lock.
            if (!_currentLockTarget || !IsTargetValid(_currentLockTarget))
            {
                AcquireLockTarget();
                if (_currentLockTarget == null) _isLocked = false;
            }
        }

        private void AcquireLockTarget()
        {
            Transform best = null;
            float bestScore = float.PositiveInfinity;

            Collider[] hits = Physics.OverlapSphere(followTarget.position, lockOnRadius, ~0, QueryTriggerInteraction.Ignore);
            if (hits != null)
            {
                foreach (var h in hits)
                {
                    if (!h || !h.CompareTag(enemyTag)) continue;
                    Transform t = h.transform;
                    if (!IsTargetValid(t)) continue;

                    // Prefer near screen center, then closer distance
                    Vector3 vp = _cam.WorldToViewportPoint(t.position);
                    if (vp.z <= 0f) continue;

                    float dx = (vp.x - 0.5f);
                    float dy = (vp.y - 0.5f);
                    float angleCost = Mathf.Sqrt(dx * dx + dy * dy);
                    float dist = Vector3.Distance(followTarget.position, t.position);
                    float score = angleCost * 100f + dist * 0.1f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = t;
                    }
                }
            }

            SetCurrentTarget(best);
        }

        private bool IsTargetValid(Transform t)
        {
            if (!t) return false;

            // FOV cone
            Vector3 to = (t.position - transform.position).normalized;
            float ang = Vector3.Angle(transform.forward, to);
            if (ang > lockOnFOV) return false;

            // Line of sight
            Vector3 head = t.position + Vector3.up * 1.2f;
            Vector3 origin = followTarget.position + Vector3.up * 0.3f;
            if (Physics.Linecast(origin, head, out RaycastHit hit, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(t) && hit.transform != t) return false;
            }

            // Range
            float d = Vector3.Distance(followTarget.position, t.position);
            if (d > lockOnRadius + 0.5f) return false;

            return true;
        }

        private bool IsTargetCandidateVisible(Transform t)
        {
            // Looser check used for cycling
            Vector3 vp = _cam.WorldToViewportPoint(t.position + Vector3.up * 1.0f);
            if (vp.z <= 0f) return false;
            if (vp.x < -0.15f || vp.x > 1.15f || vp.y < -0.15f || vp.y > 1.15f) return false;

            Vector3 origin = followTarget.position + Vector3.up * 0.3f;
            Vector3 head = t.position + Vector3.up * 1.0f;
            if (Physics.Linecast(origin, head, out RaycastHit hit, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(t) && hit.transform != t) return false;
            }
            return true;
        }

        private void CycleLockTarget(int dir) // dir: +1 right, -1 left
        {
            Collider[] hits = Physics.OverlapSphere(followTarget.position, lockOnRadius, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return;

            List<Transform> candidates = new List<Transform>(16);
            foreach (var h in hits)
            {
                if (!h || !h.CompareTag(enemyTag)) continue;
                var t = h.transform;
                if (!IsTargetCandidateVisible(t)) continue;
                candidates.Add(t);
            }
            if (candidates.Count == 0) return;

            // Current viewport position
            Vector3 curVp = _cam.WorldToViewportPoint(_currentLockTarget.position);
            float curX = curVp.x;

            Transform best = null;
            float bestScore = float.PositiveInfinity;

            // Primary pass: look to requested side
            foreach (var t in candidates)
            {
                if (t == _currentLockTarget) continue;
                Vector3 vp = _cam.WorldToViewportPoint(t.position);
                if (vp.z <= 0f) continue;
                float dx = vp.x - curX;
                if (dir > 0 && dx <= 0f) continue; // want right
                if (dir < 0 && dx >= 0f) continue; // want left
                float score = Mathf.Abs(dx) * 100f + Mathf.Abs(vp.y - curVp.y) * 10f;
                if (score < bestScore) { bestScore = score; best = t; }
            }

            // Wrap-around if none on that side
            if (!best)
            {
                foreach (var t in candidates)
                {
                    if (t == _currentLockTarget) continue;
                    Vector3 vp = _cam.WorldToViewportPoint(t.position);
                    if (vp.z <= 0f) continue;
                    float score = Mathf.Abs(vp.x - curX) * 100f + Mathf.Abs(vp.y - curVp.y) * 10f;
                    if (score < bestScore) { bestScore = score; best = t; }
                }
            }

            if (best)
            {
                SetCurrentTarget(best);
                // Optional gentle yaw nudge toward new target
                Vector3 flat = (best.position - followTarget.position); flat.y = 0f;
                if (flat.sqrMagnitude > 0.001f)
                {
                    float desiredYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, desiredYaw, rotationLerp * 2f * Time.deltaTime);
                }
            }
        }

        private void SetCurrentTarget(Transform t)
        {
            if (_currentLockTarget == t) return;
            _currentLockTarget = t;
            OnLockTargetChanged?.Invoke(_currentLockTarget);
        }

        // ---------- Camera Transform ----------
        private void UpdateCameraTransform()
        {
            // 1) Desired rotation from yaw/pitch
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

            // 2) Shoulder bias (fixed side; stronger while locked)
            float shoulder = _shoulderSign * shoulderOffset * (_isLocked ? 1.0f : 0.5f);
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            Vector3 localShoulder = right * shoulder;

            // 3) Raw desired position before collision
            Vector3 pivot = followTarget.position + localShoulder;
            Vector3 desiredPos = pivot - (rot * Vector3.forward * _desiredDistance);

            // 4) Collision: sphere cast from pivot toward desired
            Vector3 dir = (desiredPos - pivot).normalized;
            float maxDist = Vector3.Distance(pivot, desiredPos);
            if (Physics.SphereCast(pivot, collisionSphereRadius, dir, out RaycastHit hit, maxDist, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float clippedDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);
                desiredPos = pivot + dir * clippedDist;
            }

            // 5) Smooth position
            Vector3 newPos = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, positionSmoothTime);

            // 6) Look target: followTarget (free) or midpoint (locked)
            Vector3 lookPoint = followTarget.position;
            if (_isLocked && _currentLockTarget)
                lookPoint = Vector3.Lerp(followTarget.position, _currentLockTarget.position + Vector3.up * 0.9f, 0.45f);

            // 7) Apply
            transform.position = newPos;
            Quaternion lookRot = Quaternion.LookRotation((lookPoint - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotationLerp * Time.deltaTime);
        }

        private void UpdateFOV()
        {
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _targetFOV, Time.deltaTime * fovLerp);
        }

        /// <summary>Pulse the FOV briefly (e.g., on dash/attack): PulseFOV(8f, 0.2f)</summary>
        public void PulseFOV(float extra, float duration)
        {
            StopAllCoroutines();
            StartCoroutine(PulseFOVRoutine(extra, duration));
        }

        private System.Collections.IEnumerator PulseFOVRoutine(float extra, float duration)
        {
            float t = 0f;
            float start = _cam.fieldOfView;
            float peak = Mathf.Clamp(defaultFOV + extra, 10f, 120f);

            // Ease to peak
            float up = duration * 0.4f;
            while (t < up)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / up);
                _targetFOV = Mathf.Lerp(start, peak, a);
                yield return null;
            }

            // Ease back
            t = 0f;
            float dn = duration * 0.6f;
            while (t < dn)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / dn);
                _targetFOV = Mathf.Lerp(peak, defaultFOV, a);
                yield return null;
            }
            _targetFOV = defaultFOV;
        }

        /// <summary>Set a wider FOV while true (e.g., sprint). Resets to default when false.</summary>
        public void SetWideFOV(bool wide)
        {
            _targetFOV = wide ? sprintOrAttackFOV : defaultFOV;
        }

        // ---------- Debug ----------
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

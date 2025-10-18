using System.Collections.Generic;
using UnityEngine;

namespace BroomHackNSlash.CameraSystem
{
    /// <summary>
    /// Devil May Cry-style third-person camera:
    /// - Free-look orbit (legacy Input axes)
    /// - Soft lock-on to nearest visible enemy (toggle)
    /// - Target cycling (Q/E or stick flick)
    /// - Lock-hold API to prevent auto-reacquire/cycling during player moves (e.g., stance slide)
    /// - Camera collision (sphere cast)
    /// - Smooth damping + optional FOV pulses
    /// Drop on Main Camera and assign a "followTarget" (child on player at head/chest height).
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

        [Tooltip("Disable any auto recentering when there is no look input (keeps last view).")]
        public bool disableIdleRecenter = true;   // kept for inspector clarity (no code uses recenter)

        [Header("Lock-On")]
        public KeyCode lockOnKey = KeyCode.Tab;
        public string enemyTag = "Enemy";
        public float lockOnRadius = 18f;
        public float lockOnFOV = 65f;
        [Tooltip("Fixed horizontal offset (meters) to keep player slightly to one side in lock-on.")]
        public float shoulderOffset = 1.1f;

        [Header("Lock-On: Cycling")]
        public KeyCode prevTargetKey = KeyCode.Q;   // cycle left
        public KeyCode nextTargetKey = KeyCode.E;   // cycle right
        [Tooltip("Optional axis for cycling (e.g., RightStick X) with flick detection.")]
        public string cycleAxis = "LockCycle";      // set up in Input Manager if desired
        public float cycleFlickThreshold = 0.6f;
        public float cycleAxisDebounce = 0.25f;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.06f;
        public float rotationLerp = 20f;

        [Header("Collision")]
        public LayerMask collisionMask = ~0; // everything
        public float collisionSphereRadius = 0.25f;
        public float collisionBuffer = 0.1f;

        [Header("FOV")]
        public float defaultFOV = 60f;
        public float sprintOrAttackFOV = 68f;
        public float fovLerp = 6f;

        [Header("Input Axes (Legacy)")]
        public string lookX = "Mouse X";
        public string lookY = "Mouse Y";

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

        // Cycling state
        private float _cycleAxisCooldown = 0f;

        // --- Target hold state (prevents cycling/reacquire while active) ---
        private int _lockHoldDepth = 0;
        private Transform _lockHoldTarget = null;

        // --- Public accessors & events ---
        public bool IsLocked => _isLocked;
        public Transform CurrentLockTarget => _currentLockTarget;
        public System.Action<Transform> OnLockTargetChanged;

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
            // Lock & hide cursor for play
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

        private void HandleInput()
        {
            // Unlock cursor for menus
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

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

            if (!_isLocked)
            {
                // Free-look only; no idle recentering
                float sens = IsMouseMoving() ? mouseSensitivity : controllerSensitivity;
                _yaw += lx * sens * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch - ly * sens * Time.deltaTime, pitchMin, pitchMax);
            }
            else
            {
                // Lock-on: steer to frame player + target; allow small manual nudges
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

                    // Manual nudges
                    _yaw += lx * 40f * Time.deltaTime;
                    _pitch = Mathf.Clamp(_pitch - ly * 40f * Time.deltaTime, pitchMin, pitchMax);
                }
            }

            // Scroll zoom
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                _desiredDistance = Mathf.Clamp(_desiredDistance - scroll * 3.0f, minDistance, maxDistance);
            }

            // --- Target cycling while locked (blocked during lock-hold) ---
            if (_isLocked && _currentLockTarget && _lockHoldDepth == 0)
            {
                if (Input.GetKeyDown(prevTargetKey)) CycleLockTarget(-1);
                else if (Input.GetKeyDown(nextTargetKey)) CycleLockTarget(+1);

                float ax = 0f;
                if (!string.IsNullOrEmpty(cycleAxis)) ax = Input.GetAxisRaw(cycleAxis);
                if (_cycleAxisCooldown > 0f) _cycleAxisCooldown -= Time.deltaTime;

                if (_cycleAxisCooldown <= 0f && Mathf.Abs(ax) >= cycleFlickThreshold)
                {
                    CycleLockTarget(Mathf.Sign(ax) > 0 ? +1 : -1);
                    _cycleAxisCooldown = cycleAxisDebounce;
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

            // If held (e.g., during stance slide), do not drop or reacquire
            if (_lockHoldDepth > 0) return;

            if (!_currentLockTarget || !IsTargetValid(_currentLockTarget))
            {
                AcquireLockTarget();
                if (_currentLockTarget == null) _isLocked = false;
            }
        }

        private void AcquireLockTarget()
        {
            Transform best = null;
            Collider[] hits = Physics.OverlapSphere(followTarget.position, lockOnRadius, ~0, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                float bestScore = float.PositiveInfinity;
                foreach (var h in hits)
                {
                    if (!h || !h.CompareTag(enemyTag)) continue;
                    Transform t = h.transform;
                    if (!IsTargetValid(t)) continue;

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

            // FOV
            Vector3 to = (t.position - transform.position).normalized;
            float ang = Vector3.Angle(transform.forward, to);
            if (ang > lockOnFOV) return false;

            // LOS
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

            Vector3 curVp3 = _cam.WorldToViewportPoint(_currentLockTarget.position);
            float curX = curVp3.x;

            Transform best = null;
            float bestScore = float.PositiveInfinity;

            // Primary: look on desired side horizontally
            foreach (var t in candidates)
            {
                if (t == _currentLockTarget) continue;
                Vector3 vp = _cam.WorldToViewportPoint(t.position);
                if (vp.z <= 0f) continue;

                float dx = vp.x - curX;
                if (dir > 0 && dx <= 0f) continue;
                if (dir < 0 && dx >= 0f) continue;

                float score = Mathf.Abs(dx) * 100f + Mathf.Abs(vp.y - curVp3.y) * 10f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }

            // Wrap-around if none on that side
            if (!best)
            {
                foreach (var t in candidates)
                {
                    if (t == _currentLockTarget) continue;
                    Vector3 vp = _cam.WorldToViewportPoint(t.position);
                    if (vp.z <= 0f) continue;
                    float dx = vp.x - curX;
                    float score = Mathf.Abs(dx) * 100f + Mathf.Abs(vp.y - curVp3.y) * 10f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = t;
                    }
                }
            }

            if (best)
            {
                SetCurrentTarget(best);
                // Optional: nudge yaw toward the new target
                Vector3 flat = (best.position - followTarget.position); flat.y = 0f;
                if (flat.sqrMagnitude > 0.001f)
                {
                    float desiredYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, desiredYaw, rotationLerp * 2f * Time.deltaTime);
                }
            }
        }

        private bool IsTargetCandidateVisible(Transform t)
        {
            Vector3 vp = _cam.WorldToViewportPoint(t.position + Vector3.up * 0.9f);
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

        private void UpdateCameraTransform()
        {
            // 1) Compute desired rotation
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

            // 2) Fixed shoulder bias (smaller when free-look)
            float shoulder = _shoulderSign * shoulderOffset * (_isLocked ? 1.0f : 0.5f);
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            Vector3 localShoulder = right * shoulder;

            // 3) Desired camera position before collision
            Vector3 pivot = followTarget.position + localShoulder;
            Vector3 desiredPos = pivot - (rot * Vector3.forward * _desiredDistance);

            // 4) Collision: sphere cast from pivot to desiredPos
            Vector3 dir = (desiredPos - pivot).normalized;
            float maxDist = Vector3.Distance(pivot, desiredPos);
            if (Physics.SphereCast(pivot, collisionSphereRadius, dir, out RaycastHit hit, maxDist, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float clippedDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);
                desiredPos = pivot + dir * clippedDist;
            }

            // 5) Smooth move
            Vector3 newPos = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, positionSmoothTime);

            // 6) Look target
            Vector3 lookPoint = followTarget.position;
            if (_isLocked && _currentLockTarget)
            {
                lookPoint = Vector3.Lerp(followTarget.position, _currentLockTarget.position + Vector3.up * 0.9f, 0.45f);
            }

            // 7) Apply
            transform.position = newPos;

            Quaternion lookRot = Quaternion.LookRotation((lookPoint - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotationLerp * Time.deltaTime);
        }

        private void UpdateFOV()
        {
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _targetFOV, Time.deltaTime * fovLerp);
        }

        /// <summary>Kick the FOV briefly (e.g., on dash or big hit).</summary>
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

            // Ease out to peak
            while (t < duration * 0.4f)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / (duration * 0.4f));
                _targetFOV = Mathf.Lerp(start, peak, a);
                yield return null;
            }

            // Ease back
            t = 0f;
            while (t < duration * 0.6f)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / (duration * 0.6f));
                _targetFOV = Mathf.Lerp(peak, defaultFOV, a);
                yield return null;
            }
            _targetFOV = defaultFOV;
        }

        /// <summary>Set a wider FOV while true; returns to default when false.</summary>
        public void SetWideFOV(bool wide)
        {
            _targetFOV = wide ? sprintOrAttackFOV : defaultFOV;
        }

        // --- Lock-hold API (for stance/parry/etc.) ---
        public void PushLockHold(Transform t)
        {
            _lockHoldDepth++;
            _lockHoldTarget = t ? t : _currentLockTarget;
            if (_lockHoldTarget) { _isLocked = true; SetCurrentTarget(_lockHoldTarget); }
        }

        public void PopLockHold()
        {
            if (_lockHoldDepth > 0) _lockHoldDepth--;
            if (_lockHoldDepth == 0) _lockHoldTarget = null;
        }

        // --- Helpers ---
        private void SetCurrentTarget(Transform t)
        {
            if (_currentLockTarget == t) return;
            _currentLockTarget = t;
            OnLockTargetChanged?.Invoke(_currentLockTarget);
        }

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

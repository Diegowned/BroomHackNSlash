using System.Collections.Generic;
using UnityEngine;

namespace BroomHackNSlash.CameraSystem
{
    [RequireComponent(typeof(Camera))]
    public class DmcCameraRig : MonoBehaviour
    {


        public bool IsLocked => _isLocked;
        public Transform CurrentLockTarget => _currentLockTarget;   
        
        [Header("References")]
        public Transform followTarget;

        [Header("Orbit")]
        public float distance = 5.5f;
        public float minDistance = 1.0f;
        public float maxDistance = 7.5f;
        public float mouseSensitivity = 140f;
        public float controllerSensitivity = 220f;
        public float pitchMin = -20f;
        public float pitchMax = 70f;

        [Tooltip("Disable any auto recentering when there is no look input.")]
        public bool disableIdleRecenter = true;   // <= default true

        [Header("Lock-On")]
        public KeyCode lockOnKey = KeyCode.Tab;
        public string enemyTag = "Enemy";
        public float lockOnRadius = 18f;
        public float lockOnFOV = 65f;
        [Tooltip("Fixed horizontal offset (meters) to keep player slightly to one side in lock-on.")]
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

        // State
        private Transform _currentLockTarget;
        private bool _isLocked;
        private int _shoulderSign = 1; // fixed to right shoulder (no swapping)
        private float _yaw;
        private float _pitch;
        private float _desiredDistance;
        private Vector3 _posVel;
        private Camera _cam;

        private float _targetFOV;

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
    // Lock and hide the mouse cursor while playing
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

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                      Cursor.lockState = CursorLockMode.None;
                      Cursor.visible = true;
                 }
        }

        private void HandleInput()
        {
            // Toggle lock-on
            if (Input.GetKeyDown(lockOnKey))
            {
                if (_isLocked && _currentLockTarget)
                {
                    _isLocked = false;
                    _currentLockTarget = null;
                }
                else
                {
                    AcquireLockTarget();
                    _isLocked = _currentLockTarget != null;
                }
            }

            // No shoulder swapping (feature removed)

            // Look input
            float lx = Input.GetAxisRaw(lookX);
            float ly = Input.GetAxisRaw(lookY);

            if (!_isLocked)
            {
                // Free-look only; no smart/idle recentering at all
                float sens = IsMouseMoving() ? mouseSensitivity : controllerSensitivity;
                _yaw += lx * sens * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch - ly * sens * Time.deltaTime, pitchMin, pitchMax);

                // If you ever want optional recentering toward move direction, gate it behind disableIdleRecenter == false.
                // (Left intentionally removed as requested)
            }
            else
            {
                // In lock-on, steer camera to frame player + target with gentle manual nudges
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

                    // small nudges while locked
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
        }

        private bool IsMouseMoving()
        {
            return Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.0001f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.0001f;
        }

        private void HandleLockOnTargeting()
        {
            if (!_isLocked) return;
            if (!_currentLockTarget || !IsTargetValid(_currentLockTarget))
            {
                AcquireLockTarget();
                if (_currentLockTarget == null) _isLocked = false;
            }
        }

        private void AcquireLockTarget()
        {
            _currentLockTarget = null;
            Collider[] hits = Physics.OverlapSphere(followTarget.position, lockOnRadius, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return;

            Transform best = null;
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

            _currentLockTarget = best;
        }

        private bool IsTargetValid(Transform t)
        {
            if (!t) return false;
            Vector3 to = (t.position - transform.position).normalized;
            float ang = Vector3.Angle(transform.forward, to);
            if (ang > lockOnFOV) return false;

            Vector3 head = t.position + Vector3.up * 1.2f;
            Vector3 origin = followTarget.position + Vector3.up * 0.3f;
            if (Physics.Linecast(origin, head, out RaycastHit hit, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(t) && hit.transform != t) return false;
            }

            float d = Vector3.Distance(followTarget.position, t.position);
            if (d > lockOnRadius + 0.5f) return false;

            return true;
        }

        private void UpdateCameraTransform()
        {
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

            // Fixed shoulder (no swapping)
            float shoulder = _shoulderSign * shoulderOffset * (_isLocked ? 1.0f : 0.5f);
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            Vector3 localShoulder = right * shoulder;

            Vector3 pivot = followTarget.position + localShoulder;
            Vector3 desiredPos = pivot - (rot * Vector3.forward * _desiredDistance);

            Vector3 dir = (desiredPos - pivot).normalized;
            float maxDist = Vector3.Distance(pivot, desiredPos);
            if (Physics.SphereCast(pivot, collisionSphereRadius, dir, out RaycastHit hit, maxDist, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float clippedDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);
                desiredPos = pivot + dir * clippedDist;
            }

            Vector3 newPos = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, positionSmoothTime);

            Vector3 lookPoint = followTarget.position;
            if (_isLocked && _currentLockTarget)
                lookPoint = Vector3.Lerp(followTarget.position, _currentLockTarget.position + Vector3.up * 0.9f, 0.45f);

            transform.position = newPos;
            Quaternion lookRot = Quaternion.LookRotation((lookPoint - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotationLerp * Time.deltaTime);
        }

        private void UpdateFOV()
        {
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _targetFOV, Time.deltaTime * fovLerp);
        }

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
            while (t < duration * 0.4f)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / (duration * 0.4f));
                _targetFOV = Mathf.Lerp(start, peak, a);
                yield return null;
            }

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

        public void SetWideFOV(bool wide)
        {
            _targetFOV = wide ? sprintOrAttackFOV : defaultFOV;
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

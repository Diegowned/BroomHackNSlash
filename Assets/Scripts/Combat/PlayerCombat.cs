using System.Collections.Generic;
using UnityEngine;
using BroomHackNSlash.Character;

namespace BroomHackNSlash.Combat
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SimplePlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        public enum CombatState
        {
            Idle,
            Attacking,
            Recovery,
            Stunned
        }

        [Header("Input")]
        [Tooltip("Input button name for light attack (legacy Input Manager).")]
        public string lightAttackButton = "Fire1";

        [Header("Attacks")]
        public AttackData neutralStartingAttack;
        public AttackData forwardStartingAttack;
        public AttackData backwardStartingAttack;
        public LayerMask damageToLayers;

        [Header("References")]
        public List<Hitbox> hitboxes;
        private Transform cameraTransform;

        private Animator _anim;
        private SimplePlayerController _playerController;
        private BroomHackNSlash.CameraSystem.DmcCameraRig _dmcCameraRig;
        private CombatState _currentState;
        private AttackData _currentAttack;
        private bool _comboWindowIsOpen;

        // Input Buffering
        private const float _inputBufferTime = 0.2f; // seconds
        private float _inputBufferTimer;
        private bool _attackBuffered;

        private readonly Dictionary<Hitbox, HashSet<Collider>> _alreadyHit = new();

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _playerController = GetComponent<SimplePlayerController>();
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
                _dmcCameraRig = mainCamera.GetComponent<BroomHackNSlash.CameraSystem.DmcCameraRig>();
            }

            foreach (var hb in hitboxes)
            {
                if (!hb) continue;
                hb.OnHit += OnHitboxContact;
                hb.SetActive(false);
                _alreadyHit[hb] = new HashSet<Collider>();
                hb.DamageMask = damageToLayers;
            }
        }

    void Update()
    {
        HandleInputBuffering();
        ProcessCombatState();
    }

        private void HandleInputBuffering()
        {
            if (Input.GetButtonDown(lightAttackButton))
            {
                _attackBuffered = true;
                _inputBufferTimer = _inputBufferTime;
            }
            else if (_inputBufferTimer > 0)
            {
                _inputBufferTimer -= Time.deltaTime;
                if (_inputBufferTimer <= 0)
                {
                    _attackBuffered = false;
                }
            }
        }

        private void ProcessCombatState()
        {
            switch (_currentState)
            {
                case CombatState.Idle:
                    if (_attackBuffered)
                    {
                        _attackBuffered = false;
                        var direction = GetAttackDirection();
                        AttackData attackToExecute = direction switch
                        {
                            AttackDirection.Forward => forwardStartingAttack,
                            AttackDirection.Backward => backwardStartingAttack,
                            _ => neutralStartingAttack
                        };
                        ExecuteAttack(attackToExecute);
                    }
                    break;
                case CombatState.Attacking:
                    if (_attackBuffered && _comboWindowIsOpen)
                    {
                        _attackBuffered = false;
                        TryCombo();
                    }
                    break;
            }
        }

    private void ExecuteAttack(AttackData attack)
    {
        if (attack == null) return;
        _currentAttack = attack;
        _anim.SetTrigger(attack.animationTrigger);
        _currentState = CombatState.Attacking;
        _comboWindowIsOpen = false;
    }

    private void TryCombo()
    {
        if (_currentAttack == null || _currentAttack.followUps.Count == 0) return;

        AttackDirection direction = GetAttackDirection();

        foreach (var followUp in _currentAttack.followUps)
        {
            if (followUp.requiredDirection == direction)
            {
                ExecuteAttack(followUp.nextAttack);
                return;
            }
        }
    }

        private AttackDirection GetAttackDirection()
        {
            if (_dmcCameraRig == null || !_dmcCameraRig.IsLocked || _dmcCameraRig.CurrentLockTarget == null)
            {
                return AttackDirection.Neutral;
            }

            Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            if (input.sqrMagnitude < 0.1f)
            {
                return AttackDirection.Neutral;
            }

            // Get the camera-relative input direction
            Vector3 camForward = cameraTransform.forward;
            camForward.y = 0;
            camForward.Normalize();
            Vector3 camRight = cameraTransform.right;
            camRight.y = 0;
            camRight.Normalize();
            Vector3 inputDir = (camForward * input.y + camRight * input.x).normalized;

            // Get the direction from player to enemy
            Vector3 toEnemyDir = _dmcCameraRig.CurrentLockTarget.position - transform.position;
            toEnemyDir.y = 0;
            toEnemyDir.Normalize();

            // Compare the angle between the player's input and the direction to the enemy
            float angle = Vector3.SignedAngle(inputDir, toEnemyDir, Vector3.up);

            // Check if input is generally towards or away from the enemy
            if (Mathf.Abs(angle) < 45.0f)
            {
                return AttackDirection.Forward;
            }
            if (Mathf.Abs(angle) > 135.0f)
            {
                return AttackDirection.Backward;
            }

            return AttackDirection.Neutral;
        }

        #region Animation Events
    public void OpenComboWindow()
    {
        _comboWindowIsOpen = true;
    }

    public void CloseComboWindow()
    {
        _comboWindowIsOpen = false;
    }

        public void HB_On(string hitboxId)
        {
            var hb = FindHitbox(hitboxId);
            if (!hb) return;
            _alreadyHit[hb].Clear();
            hb.SetActive(true);
        }

        public void HB_Off(string hitboxId)
        {
            var hb = FindHitbox(hitboxId);
            if (hb) hb.SetActive(false);
        }

        public void Attack_End()
        {
            Debug.Log("Attack_End called. Resetting state to Idle.");
            _currentState = CombatState.Idle;
            _currentAttack = null;
            foreach (var hb in hitboxes) if (hb) hb.SetActive(false);
        }
        #endregion

        private Hitbox FindHitbox(string id)
        {
            foreach (var hb in hitboxes) if (hb && hb.Id == id) return hb;
            return null;
        }

        private void OnHitboxContact(Hitbox hb, Collider other)
        {
            if (!_alreadyHit.TryGetValue(hb, out var set) || set.Contains(other)) return;
            set.Add(other);

            if (other.TryGetComponent<IDamageable>(out var dmg))
            {
            var ctx = new DamageContext
            {
                amount = _currentAttack.damage,
                stunSeconds = _currentAttack.stunSeconds,
                launchForce = _currentAttack.launchForce,
                source = transform,
                hitPoint = other.ClosestPoint(hb.transform.position),
                hitDirection = (other.transform.position - transform.position).normalized
            };
                dmg.TakeDamage(ctx);
                CombatDebugOverlay.ReportDamage(ctx, other);
            }
        }

        void OnDrawGizmos()
        {
            if (_dmcCameraRig != null && _dmcCameraRig.IsLocked && _dmcCameraRig.CurrentLockTarget != null)
            {
                Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
                if (input.sqrMagnitude > 0.1f)
                {
                    // Get the camera-relative input direction
                    Vector3 camForward = cameraTransform.forward;
                    camForward.y = 0;
                    camForward.Normalize();
                    Vector3 camRight = cameraTransform.right;
                    camRight.y = 0;
                    camRight.Normalize();
                    Vector3 inputDir = (camForward * input.y + camRight * input.x).normalized;

                    // Get the direction from player to enemy
                    Vector3 toEnemyDir = _dmcCameraRig.CurrentLockTarget.position - transform.position;
                    toEnemyDir.y = 0;
                    toEnemyDir.Normalize();

                    // Draw the player's input direction (relative to camera)
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(transform.position, transform.position + inputDir * 2);

                    // Draw the direction to the enemy
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(transform.position, transform.position + toEnemyDir * 2);
                }
            }
        }
    }
}

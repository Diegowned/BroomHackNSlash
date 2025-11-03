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
        public AttackData startingAttack;
        public LayerMask damageToLayers;

        [Header("References")]
        public List<Hitbox> hitboxes;
        private Transform cameraTransform;

        private Animator _anim;
        private SimplePlayerController _playerController;
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
            cameraTransform = Camera.main != null ? Camera.main.transform : null;

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
                        ExecuteAttack(startingAttack);
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
            Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            if (input.sqrMagnitude < 0.1f)
            {
                return AttackDirection.Neutral;
            }

            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;
            camForward.y = 0;
            camRight.y = 0;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDirection = (camForward * input.y + camRight * input.x).normalized;
            float dot = Vector3.Dot(transform.forward, moveDirection);

            if (dot > 0.7f)
            {
                return AttackDirection.Forward;
            }
            if (dot < -0.7f)
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
                    source = transform,
                    hitPoint = other.ClosestPoint(hb.transform.position),
                    hitDirection = (other.transform.position - transform.position).normalized
                };
                dmg.TakeDamage(ctx);
                CombatDebugOverlay.ReportDamage(ctx, other);
            }
        }
    }
}

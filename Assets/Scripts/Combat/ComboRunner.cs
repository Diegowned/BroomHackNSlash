using UnityEngine;

public enum AttackButton { None, Light, Medium }

[RequireComponent(typeof(Animator))]
public class ComboRunner : MonoBehaviour
{
    [Header("Data")]
    public ComboSetSO comboSet;

    [Header("Input (legacy)")]
    public string lightButton = "Fire1";   // LMB / Ctrl by default
    public string mediumButton = "Fire2";  // RMB by default

    [Header("References")]
    [Tooltip("Optional. If assigned, we’ll set this to lock input during active attacks.")]
    public PlayerCombat playerCombat; // your existing script (optional)

    private Animator _anim;
    private AttackStepSO _current;
    private bool _inAttack;          // true between Attack_Begin and Attack_End
    private bool _cancelOpen;        // true between Combo_CancelOpen and Combo_CancelClose
    private float _timeSinceEnd;     // time since last Attack_End (for chain reset)

    // Simple input buffer
    private AttackButton _bufferedBtn = AttackButton.None;
    private float _bufferExpire;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (!playerCombat) playerCombat = GetComponent<PlayerCombat>();
    }

    void OnEnable()
    {
        ResetComboState();
    }

    void Update()
    {
        ReadInput();

        // Allow chain reset if we sit too long after the last attack
        if (!_inAttack && _current != null)
        {
            _timeSinceEnd += Time.deltaTime;
            if (_timeSinceEnd > _current.chainResetTime)
                ResetComboState();
        }

        // If we’re idle (no current step) and have buffered input, start from matching starter
        if (!_inAttack && _current == null && _bufferedBtn != AttackButton.None)
        {
            TryStartFromIdle(_bufferedBtn);
        }

        // If we’re within cancel window, try to consume buffered input and branch
        if (_inAttack && _cancelOpen && _bufferedBtn != AttackButton.None)
        {
            TryBranch(_bufferedBtn);
        }
    }

    private void ReadInput()
    {
        // Note: if PlayerCombat also reads input, turn that off (see tiny patch below).
        if (Input.GetButtonDown(lightButton))
            Buffer(AttackButton.Light, _current ? _current.inputBufferTime : (comboSet ? comboSet.lightStarter.inputBufferTime : 0.25f));

        if (Input.GetButtonDown(mediumButton))
            Buffer(AttackButton.Medium, _current ? _current.inputBufferTime : (comboSet ? comboSet.mediumStarter.inputBufferTime : 0.25f));
    }

    private void Buffer(AttackButton btn, float ttl)
    {
        _bufferedBtn = btn;
        _bufferExpire = Time.time + Mathf.Max(0.05f, ttl);
    }

    private void ClearBuffer()
    {
        _bufferedBtn = AttackButton.None;
        _bufferExpire = 0f;
    }

    private bool BufferValid => _bufferedBtn != AttackButton.None && Time.time <= _bufferExpire;

    private void TryStartFromIdle(AttackButton btn)
    {
        if (!comboSet) return;
        var start = (btn == AttackButton.Medium) ? comboSet.mediumStarter : comboSet.lightStarter;
        if (!start) return;

        PlayStep(start);
        // We intentionally keep the buffer until Attack_Begin (so you can pre-press slightly early)
    }

    private void TryBranch(AttackButton btn)
    {
        if (_current == null) return;

        AttackStepSO next = null;
        if (btn == AttackButton.Light)   next = _current.onLight;
        if (btn == AttackButton.Medium)  next = _current.onMedium;

        if (!next)
        {
            if (_current.canLoop && btn == AttackButton.Light) next = _current; // optional mash loop
            else return;
        }

        // Consume buffer and schedule transition when we hit Attack_End (animation event flow)
        ClearBuffer();
        // Queue immediately by swapping current and triggering next; animation will blend based on Animator transitions.
        PlayStep(next);
    }

    private void PlayStep(AttackStepSO step)
    {
        _current = step;
        _timeSinceEnd = 0f;

        if (playerCombat) playerCombat.enabled = true; // ensure active
        if (_anim && !string.IsNullOrEmpty(step.animatorTrigger))
        {
            _anim.ResetTrigger(step.animatorTrigger); // safety
            _anim.SetTrigger(step.animatorTrigger);
        }
    }

    private void ResetComboState()
    {
        _current = null;
        _inAttack = false;
        _cancelOpen = false;
        _timeSinceEnd = 0f;
        ClearBuffer();
    }

    // ---------------- Animation Events ----------------
    // Hook these by name in your attack clips.

    // Called at first actionable frame (you already use this in PlayerCombat)
    public void Attack_Begin()
    {
        _inAttack = true;
        if (playerCombat) { /* optional: playerCombat can lock movement here if you like */ }
    }

    // Your existing events HB_On(string) / HB_Off(string) continue to drive hitboxes.

    // Open a cancel window (place this before active frames end)
    public void Combo_CancelOpen() => _cancelOpen = true;

    // Close the cancel window
    public void Combo_CancelClose() => _cancelOpen = false;

    // Called on the final frame or at your cancel transition point
    public void Attack_End()
    {
        _inAttack = false;
        _cancelOpen = false;
        _timeSinceEnd = 0f;

        // If a buffer exists and is still valid, decide what to do:
        if (BufferValid)
        {
            // If we have a current step (we will, unless we were interrupted), branch now.
            if (_current) TryBranch(_bufferedBtn);
            else TryStartFromIdle(_bufferedBtn);
        }
        else
        {
            // No buffered input → we keep _current set so chainResetTime can expire; you can also null it here if preferred:
            // _current = null;
        }
    }
}

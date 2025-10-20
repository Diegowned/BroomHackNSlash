using UnityEngine;
using UnityEngine.UI;
using BroomHackNSlash.CameraSystem; // DmcCameraRig

/// Attach this to your existing World-Space Canvas object (do NOT disable this GO at runtime).
public class LockOnBillboardRadial : MonoBehaviour
{
    [Header("References")]
    public DmcCameraRig cameraRig;     // auto-found if empty
    public Camera worldCamera;         // usually Camera.main
    public RectTransform canvasRoot;   // your world-space canvas root
    public Image fillImage;            // radial HP fill (Image Type = Filled, Radial360)
    public Image backImage;            // ring background

    [Header("Placement")]
    public Vector3 targetOffset = new Vector3(0f, 1.1f, 0f);
    public float followLerp = 18f;

    [Header("Scaling")]
    public float minScale = 0.7f;
    public float maxScale = 1.35f;
    public float nearDistance = 2.0f;
    public float farDistance  = 22.0f;
    public float scaleLerp = 14f;

    [Header("Occlusion")]
    public bool hideWhenOccluded = true;
    public LayerMask occlusionMask = ~0;

    [Header("Colors")]
    public Gradient hpColor;
    public Color backColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Target-switch UX")]
    [Tooltip("Prevents the ring from hiding for a brief moment after changing targets, avoiding flicker.")]
    public float switchGraceSeconds = 0.15f;

    [Header("Pulse on change")]
    public bool pulseOnChange = true;
    public float pulseAmount = 0.35f;
    public float pulseTime = 0.18f;

    // ---- internals ----
    private Transform _currentTarget;
    private IHealthReadable _currentHealth;
    private float _curHP = 1f, _maxHP = 1f;
    private float _baseScale = 1f;
    private float _pulseT;
    private float _switchGraceT;
    private bool _visible; // desired visual state (images on/off)
    private CanvasGroup _group; // optional fade (kept on same GO; DO NOT disable the GO)

    void Awake()
    {
        if (!worldCamera) worldCamera = Camera.main;
        if (!cameraRig && worldCamera) cameraRig = worldCamera.GetComponent<DmcCameraRig>();
        if (!canvasRoot) canvasRoot = GetComponent<RectTransform>();
        _group = GetComponent<CanvasGroup>(); // optional

        if (hpColor == null || hpColor.colorKeys.Length == 0)
        {
            hpColor = new Gradient()
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.green, 0f),
                    new GradientColorKey(Color.yellow, 0.5f),
                    new GradientColorKey(Color.red, 1f)
                },
                alphaKeys = new[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(1f,1f) }
            };
        }

        if (backImage) backImage.color = backColor;
        SetVisible(false); // start hidden but keep GO active
    }

    void OnEnable()
    {
        if (cameraRig) cameraRig.OnLockTargetChanged += HandleTargetChanged;
        if (cameraRig && cameraRig.IsLocked) HandleTargetChanged(cameraRig.CurrentLockTarget);
    }

    void OnDisable()
    {
        if (cameraRig) cameraRig.OnLockTargetChanged -= HandleTargetChanged;
        UnbindHealth();
        // Leave the GO active; only images get toggled.
    }

    private void HandleTargetChanged(Transform t)
    {
        _currentTarget = t;
        BindHealth(t);
        _pulseT = (pulseOnChange && t) ? pulseTime : 0f;
        _switchGraceT = switchGraceSeconds; // start grace to avoid flicker
        SetVisible(t != null);
        if (t) SnapNowAndUpdate();
    }

    void LateUpdate()
    {
        if (_switchGraceT > 0f) _switchGraceT -= Time.deltaTime;

        if (!cameraRig || !cameraRig.IsLocked || !_currentTarget)
        {
            SetVisible(false);
            return;
        }

        Vector3 desired = _currentTarget.position + targetOffset;

        // Occlusion check, with grace period so we don't hide during the instant of switching
        bool occluded = false;
        if (hideWhenOccluded && _switchGraceT <= 0f)
        {
            if (Physics.Linecast(worldCamera.transform.position, desired, out RaycastHit hit, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(_currentTarget) && hit.transform != _currentTarget)
                    occluded = true;
            }
        }

        // Follow
        canvasRoot.position = Vector3.Lerp(canvasRoot.position, desired, Time.deltaTime * followLerp);

        // Billboard
        Vector3 fwd = (canvasRoot.position - worldCamera.transform.position).normalized;
        canvasRoot.forward = fwd;

        // Scale by distance + optional pulse
        float d = Vector3.Distance(worldCamera.transform.position, desired);
        float t01 = Mathf.InverseLerp(farDistance, nearDistance, d);
        float targetScale = Mathf.Lerp(minScale, maxScale, t01);

        if (_pulseT > 0f)
        {
            _pulseT -= Time.deltaTime;
            float a = Mathf.Clamp01(_pulseT / pulseTime);
            targetScale *= 1f + pulseAmount * a * (2f - a);
        }

        _baseScale = Mathf.Lerp(_baseScale, targetScale, Time.deltaTime * scaleLerp);
        canvasRoot.localScale = Vector3.one * _baseScale;

        SetVisible(!occluded);
        UpdateFillAndColor();
    }

    // ----- Health binding -----

    private void BindHealth(Transform t)
    {
        UnbindHealth();
        if (!t) return;

        _currentHealth = t.GetComponentInParent<IHealthReadable>();
        if (_currentHealth != null)
        {
            _curHP = _currentHealth.CurrentHP;
            _maxHP = Mathf.Max(1f, _currentHealth.MaxHP);
            _currentHealth.OnHealthChanged += OnHealthChanged;
        }
        else
        {
            _curHP = 1f; _maxHP = 1f;
        }
    }

    private void UnbindHealth()
    {
        if (_currentHealth != null)
        {
            _currentHealth.OnHealthChanged -= OnHealthChanged;
            _currentHealth = null;
        }
    }

    private void OnHealthChanged(float cur, float max)
    {
        _curHP = cur;
        _maxHP = Mathf.Max(1f, max);
        UpdateFillAndColor();
        if (_currentHealth != null && _currentHealth.IsDead)
            SetVisible(false);
    }

    // ----- Visual helpers -----

    private void UpdateFillAndColor()
    {
        float frac = Mathf.Clamp01(_curHP / Mathf.Max(1f, _maxHP));
        if (fillImage)
        {
            fillImage.fillAmount = frac;
            fillImage.color = hpColor.Evaluate(frac);
        }
        if (backImage) backImage.color = backColor;
    }

    private void SetVisible(bool v)
    {
        if (_visible == v) return;
        _visible = v;

        // DO NOT deactivate the GameObject that holds this script.
        if (fillImage) fillImage.enabled = v;
        if (backImage) backImage.enabled = v;

        // Optional: fade using CanvasGroup (keeps GO active)
        if (_group)
            _group.alpha = v ? 1f : 0f;
    }

    private void SnapNowAndUpdate()
    {
        canvasRoot.position = _currentTarget.position + targetOffset;
        canvasRoot.localScale = Vector3.one * maxScale;
        _baseScale = maxScale;
        UpdateFillAndColor();
    }
}

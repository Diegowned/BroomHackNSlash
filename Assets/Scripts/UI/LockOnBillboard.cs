using UnityEngine;
using BroomHackNSlash.CameraSystem; // your DmcCameraRig namespace

/// <summary>
/// World-space billboard reticle for the current lock target.
/// - Follows the target at an offset (head/chest)
/// - Always faces the camera
/// - Scales with distance
/// - Optional pulse on target change
/// </summary>
public class LockOnBillboard : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your DmcCameraRig (usually on Main Camera). If empty, found via Camera.main.")]
    public DmcCameraRig cameraRig;
    [Tooltip("Camera used to face the billboard. If empty, Camera.main.")]
    public Camera worldCamera;

    [Header("Reticle Source (pick one)")]
    [Tooltip("Prefab to instantiate for the reticle (e.g., a simple quad with a transparent material). Leave null if using Sprite below.")]
    public GameObject reticlePrefab;
    [Tooltip("If no prefab is provided, a quad will be created and this sprite will be used to make a transparent material.")]
    public Sprite reticleSprite;

    [Header("Placement")]
    [Tooltip("Offset from the target position (in world space). 1.0 on Y puts it roughly around head height.")]
    public Vector3 targetOffset = new Vector3(0f, 1.0f, 0f);

    [Header("Scaling")]
    public float minScale = 0.6f;
    public float maxScale = 1.4f;
    [Tooltip("Distance at which the reticle is at maxScale.")]
    public float nearDistance = 2.0f;
    [Tooltip("Distance at which the reticle is at minScale.")]
    public float farDistance = 22.0f;
    public float followLerp = 18f;
    public float scaleLerp = 14f;

    [Header("Visibility & Style")]
    [Tooltip("Hide when occluded by level geo.")]
    public bool hideWhenOccluded = true;
    [Tooltip("Layers considered solid for occlusion check.")]
    public LayerMask occlusionMask = ~0;
    [Tooltip("Base color tint for the material (alpha controls intensity).")]
    public Color tint = Color.white;
    [Tooltip("Color when occluded or target lost.")]
    public Color occludedTint = new Color(1f, 0.5f, 0.5f, 0.6f);

    [Header("Pulse On Target Change")]
    public bool pulseOnChange = true;
    public float pulseAmount = 0.35f;
    public float pulseTime = 0.18f;

    // ---- internals ----
    private Transform _currentTarget;
    private Transform _reticle;
    private Material _matInstance;
    private Vector3 _vel;
    private float _pulseT;
    private float _baseScale = 1f;
    private bool _visible;

    void Awake()
    {
        if (!worldCamera) worldCamera = Camera.main;
        if (!cameraRig && worldCamera) cameraRig = worldCamera.GetComponent<DmcCameraRig>();

        BuildReticleIfNeeded();
        SetVisible(false);

        // Subscribe to target changes if the rig exposes the event
        if (cameraRig != null) cameraRig.OnLockTargetChanged += HandleTargetChanged;
    }

    void OnDestroy()
    {
        if (cameraRig != null) cameraRig.OnLockTargetChanged -= HandleTargetChanged;
        if (_matInstance)
        {
            if (Application.isPlaying) Destroy(_matInstance);
            else DestroyImmediate(_matInstance);
        }
        if (_reticle && !_reticle.GetComponentInParent<LockOnBillboard>()) // safety
        {
            if (Application.isPlaying) Destroy(_reticle.gameObject);
            else DestroyImmediate(_reticle.gameObject);
        }
    }

    private void HandleTargetChanged(Transform t)
    {
        _currentTarget = t;
        _pulseT = pulseOnChange && t ? pulseTime : 0f;
        SetVisible(t != null);
        ApplyTint(t ? tint : occludedTint);
        // snap position immediately on change
        if (t && _reticle) _reticle.position = t.position + targetOffset;
    }

    void LateUpdate()
    {
        if (!worldCamera) worldCamera = Camera.main;
        if (!cameraRig) { SetVisible(false); return; }

        // If your rig doesn't fire OnLockTargetChanged, poll here:
        if (cameraRig.IsLocked && cameraRig.CurrentLockTarget != _currentTarget)
            HandleTargetChanged(cameraRig.CurrentLockTarget);

        if (!cameraRig.IsLocked || _currentTarget == null) { SetVisible(false); return; }

        // Desired world position
        Vector3 desired = _currentTarget.position + targetOffset;

        // Occlusion check (optional)
        bool occluded = false;
        if (hideWhenOccluded)
        {
            Vector3 origin = worldCamera.transform.position;
            if (Physics.Linecast(origin, desired, out RaycastHit hit, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                // allow target's own colliders
                if (!hit.transform.IsChildOf(_currentTarget) && hit.transform != _currentTarget)
                    occluded = true;
            }
        }

        // Smooth follow
        if (_reticle)
        {
            _reticle.position = Vector3.Lerp(_reticle.position, desired, Time.deltaTime * followLerp);

            // Face camera (billboard)
            _reticle.forward = ( _reticle.position - worldCamera.transform.position ).normalized;

            // Scale by distance + pulse
            float d = Vector3.Distance(worldCamera.transform.position, desired);
            float t01 = Mathf.InverseLerp(farDistance, nearDistance, d);
            float targetScale = Mathf.Lerp(minScale, maxScale, t01);

            if (_pulseT > 0f)
            {
                _pulseT -= Time.deltaTime;
                float a = Mathf.Clamp01(_pulseT / pulseTime);
                // quick ease-out pulse
                targetScale *= 1f + pulseAmount * a * (2f - a);
            }

            _baseScale = Mathf.Lerp(_baseScale, targetScale, Time.deltaTime * scaleLerp);
            _reticle.localScale = Vector3.one * _baseScale;

            // Visibility / tint
            SetVisible(!occluded);
            ApplyTint(occluded ? occludedTint : tint);
        }
        else
        {
            BuildReticleIfNeeded();
        }
    }

    private void BuildReticleIfNeeded()
    {
        if (_reticle) return;

        if (reticlePrefab)
        {
            var go = Instantiate(reticlePrefab, transform);
            _reticle = go.transform;
        }
        else
        {
            // Make a unit quad and give it a transparent material from the sprite (if provided)
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "LockOn_Quad";
            quad.transform.SetParent(transform, false);
            _reticle = quad.transform;

            var mr = quad.GetComponent<MeshRenderer>();
            _matInstance = CreateTransparentMaterial(reticleSprite, tint);
            mr.sharedMaterial = _matInstance;

            // Match quad aspect to sprite if any
            if (reticleSprite)
            {
                float w = reticleSprite.rect.width;
                float h = reticleSprite.rect.height;
                float aspect = (h <= 0.001f) ? 1f : (w / h);
                _reticle.localScale = new Vector3(aspect, 1f, 1f); // base; we further scale uniformly each frame
            }
        }
    }

    private Material CreateTransparentMaterial(Sprite sprite, Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (!sh) sh = Shader.Find("Sprites/Default");
        if (!sh) sh = Shader.Find("Unlit/Transparent");
        if (!sh) sh = Shader.Find("Unlit/Texture"); // last resort

        var mat = new Material(sh);
        if (sprite)
        {
            // Use the sprite texture
            mat.mainTexture = sprite.texture;
        }

        // Try common color properties
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);

        // Make sure it renders transparent and on top of most geometry (optional slight queue bump)
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
        return mat;
    }

    private void SetVisible(bool v)
    {
        _visible = v;
        if (_reticle)
        {
            var r = _reticle.GetComponent<Renderer>();
            if (r) r.enabled = v;
        }
    }

    private void ApplyTint(Color c)
    {
        if (_matInstance)
        {
            if (_matInstance.HasProperty("_BaseColor")) _matInstance.SetColor("_BaseColor", c);
            if (_matInstance.HasProperty("_Color"))     _matInstance.SetColor("_Color", c);
        }
        else if (_reticle)
        {
            var r = _reticle.GetComponent<Renderer>();
            if (r && r.sharedMaterial)
            {
                var m = r.sharedMaterial;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
            }
        }
    }
}

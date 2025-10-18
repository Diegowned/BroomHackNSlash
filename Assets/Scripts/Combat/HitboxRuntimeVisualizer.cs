using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Hitbox))]
public class HitboxRuntimeVisualizer : MonoBehaviour
{
    [Header("Visibility")]
    [Tooltip("If true, the visual is always shown. If false, it only shows while Hitbox.Active is true.")]
    public bool alwaysShow = false;

    [Header("Appearance")]
    public Color activeColor = new Color(0f, 1f, 0.3f, 0.25f);
    public Color inactiveColor = new Color(1f, 0.6f, 0f, 0.15f);
    [Tooltip("Optional: assign a material; if empty, a simple transparent one will be created at runtime.")]
    public Material overrideMaterial;

    // Internals
    private Hitbox _hb;
    private Collider _col;
    private GameObject _vizGO;
    private Renderer _renderer;
    private Material _matInstance;

    void Awake()
    {
        _hb = GetComponent<Hitbox>();
        _col = GetComponent<Collider>();
        if (!_col)
        {
            Debug.LogWarning("HitboxRuntimeVisualizer needs a Collider on the same object.", this);
            enabled = false;
            return;
        }

        BuildVisualizer();
        ApplyTransformFromCollider();
        UpdateVisualState(force:true);
    }

    void LateUpdate()
    {
        // Keep transform synced if authoring/animating centers/scale
        ApplyTransformFromCollider();
        UpdateVisualState();
    }

    private void BuildVisualizer()
    {
        if (_vizGO) return;

        // Create a child to carry the mesh so it can have its own local offset/rotation
        _vizGO = new GameObject($"{name}_HB_Viz");
        _vizGO.transform.SetParent(transform, false);

        Mesh sourceMesh = null;
        PrimitiveType primitive = PrimitiveType.Cube;

        if (_col is BoxCollider)
            primitive = PrimitiveType.Cube;
        else if (_col is SphereCollider)
            primitive = PrimitiveType.Sphere;
        else if (_col is CapsuleCollider)
            primitive = PrimitiveType.Capsule;
        else
            Debug.LogWarning("Unsupported collider type for runtime viz; supports Box/Sphere/Capsule.", this);

        // Use Unity primitive mesh (then remove the auto-added collider)
        var temp = GameObject.CreatePrimitive(primitive);
        sourceMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp.GetComponent<Collider>()); // not needed

        var mf = _vizGO.AddComponent<MeshFilter>();
        var mr = _vizGO.AddComponent<MeshRenderer>();
        mf.sharedMesh = sourceMesh;

        // Material
        if (overrideMaterial)
        {
            _matInstance = Instantiate(overrideMaterial);
        }
        else
        {
            // Works in Built-in/URP/HDRP: a simple transparent material
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            _matInstance = new Material(shader);
            // Turn on transparency
            if (shader && shader.name.Contains("Universal"))
            {
                // URP Lit setup
                _matInstance.SetFloat("_Surface", 1f);  // Transparent
                _matInstance.SetFloat("_Blend", 0f);
                _matInstance.SetFloat("_ZWrite", 0f);
                _matInstance.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _matInstance.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                _matInstance.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                // Standard shader setup
                _matInstance.SetFloat("_Mode", 3f); // Transparent
                _matInstance.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _matInstance.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _matInstance.SetInt("_ZWrite", 0);
                _matInstance.DisableKeyword("_ALPHATEST_ON");
                _matInstance.EnableKeyword("_ALPHABLEND_ON");
                _matInstance.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _matInstance.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        mr.sharedMaterial = _matInstance;
        _renderer = mr;
    }

    private void ApplyTransformFromCollider()
    {
        if (_col is BoxCollider b)
        {
            _vizGO.transform.localPosition = b.center;
            _vizGO.transform.localRotation = Quaternion.identity;
            _vizGO.transform.localScale = b.size;
        }
        else if (_col is SphereCollider s)
        {
            _vizGO.transform.localPosition = s.center;
            _vizGO.transform.localRotation = Quaternion.identity;
            float r = s.radius;
            // account for lossy scale by baking into local scale:
            _vizGO.transform.localScale = Vector3.one * (r * 2f);
        }
        else if (_col is CapsuleCollider cap)
        {
            _vizGO.transform.localPosition = cap.center;

            // Orient the capsule visual to match collider direction (0=X,1=Y,2=Z)
            if (cap.direction == 0)       // X
                _vizGO.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (cap.direction == 1)  // Y
                _vizGO.transform.localRotation = Quaternion.identity;
            else                          // Z
                _vizGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Capsule scale: X/Z based on radius, Y based on height
            float dia = cap.radius * 2f;
            _vizGO.transform.localScale = new Vector3(dia, cap.height, dia);
        }
    }

    private void UpdateVisualState(bool force = false)
    {
        if (!_renderer) return;

        bool shouldShow = alwaysShow || (_hb != null && _hb.Active);
        if (_renderer.enabled != shouldShow || force)
            _renderer.enabled = shouldShow;

        // Color by active state (fades handled by material alpha)
        var col = (_hb != null && _hb.Active) ? activeColor : inactiveColor;
        // Standard & URP Lit both respect _BaseColor; Standard also respects _Color
        if (_matInstance.HasProperty("_BaseColor")) _matInstance.SetColor("_BaseColor", col);
        if (_matInstance.HasProperty("_Color"))     _matInstance.SetColor("_Color", col);
    }

    void OnDisable()
    {
        if (_renderer) _renderer.enabled = false;
    }

    void OnDestroy()
    {
        if (_matInstance && !overrideMaterial)
        {
            if (Application.isPlaying) Destroy(_matInstance);
            else DestroyImmediate(_matInstance);
        }
        if (_vizGO)
        {
            if (Application.isPlaying) Destroy(_vizGO);
            else DestroyImmediate(_vizGO);
        }
    }
}

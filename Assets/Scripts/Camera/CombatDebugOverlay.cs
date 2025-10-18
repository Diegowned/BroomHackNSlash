using System.Collections.Generic;
using UnityEngine;

public class CombatDebugOverlay : MonoBehaviour
{
    private class LogItem
    {
        public string text;
        public float ttl;
        public Color color;
    }

    private static CombatDebugOverlay _instance;

    [Header("On-Screen Log")]
    public int maxLines = 12;
    public float messageTTL = 4f;
    public Vector2 screenPadding = new Vector2(12, 12);

    [Header("World Markers")]
    public float worldMarkerTTL = 0.6f;
    public float worldMarkerSize = 0.15f;

    private readonly List<LogItem> _lines = new();
    private readonly List<(Vector3 pos, float ttl, Color col)> _markers = new();

    void Awake()
    {
        if (_instance && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            _lines[i].ttl -= dt;
            if (_lines[i].ttl <= 0f) _lines.RemoveAt(i);
        }

        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            var t = _markers[i];
            t.ttl -= dt;
            if (t.ttl <= 0f) _markers.RemoveAt(i);
            else _markers[i] = t;
        }
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white },
            richText = true
        };

        float x = screenPadding.x;
        float y = screenPadding.y;

        for (int i = 0; i < _lines.Count; i++)
        {
            var li = _lines[i];
            float a = Mathf.Clamp01(li.ttl / messageTTL);
            var c = li.color; c.a = a;
            var prev = GUI.color;
            GUI.color = c;
            GUI.Label(new Rect(x, y + i * 18f, 900f, 20f), li.text, style);
            GUI.color = prev;
        }
    }

    void OnDrawGizmos()
    {
        if (_markers == null) return;
        foreach (var m in _markers)
        {
            Gizmos.color = new Color(m.col.r, m.col.g, m.col.b, Mathf.Clamp01(m.ttl / worldMarkerTTL));
            Gizmos.DrawSphere(m.pos, worldMarkerSize);
        }
    }

    // -------- Static reporting API --------

    public static void ReportHitboxToggle(Hitbox hb, bool active)
    {
        if (!_instance) return;
        _instance.PushLine($"<b>HB</b> [{hb.Id}] {(active ? "<color=#00FF88>ON</color>" : "<color=#FFAA00>OFF</color>")}",
            active ? new Color(0f, 1f, 0.6f, 1f) : new Color(1f, 0.7f, 0.1f, 1f));
    }

    public static void ReportHitContact(Hitbox hb, Collider other)
    {
        if (!_instance) return;
        var pos = other.ClosestPoint(hb.transform.position);
        _instance.PushLine($"<b>HIT</b> [{hb.Id}] → {other.name}", Color.cyan);
        _instance.PushMarker(pos, _instance.worldMarkerTTL, Color.cyan);
    }

    public static void ReportDamage(DamageContext ctx, Object targetObj)
    {
        if (!_instance) return;
        _instance.PushLine($"<b>DMG</b> {ctx.amount:0.#} to {targetObj.name}", new Color(1f, 0.4f, 0.4f, 1f));
        _instance.PushMarker(ctx.hitPoint, _instance.worldMarkerTTL, new Color(1f, 0.3f, 0.3f, 1f));
    }

    // ------------ helpers ------------
    private void PushLine(string text, Color col)
    {
        _lines.Add(new LogItem { text = text, color = col, ttl = messageTTL });
        while (_lines.Count > maxLines) _lines.RemoveAt(0);
    }

    private void PushMarker(Vector3 pos, float ttl, Color col)
    {
        _markers.Add((pos, ttl, col));
        if (_markers.Count > 64) _markers.RemoveAt(0);
    }
}

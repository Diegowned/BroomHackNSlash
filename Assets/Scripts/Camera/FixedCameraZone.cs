using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class FixedCameraZone : MonoBehaviour
{
    public FixedCameraAnchor anchor;
    [Tooltip("Higher wins when overlapping zones affect the player")] public int priority = 0;

    void Reset()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    public bool ContainsPoint(Vector3 worldPoint)
    {
        var box = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        Vector3 half = box.size * 0.5f;
        Vector3 c = box.center;
        return Mathf.Abs(local.x - c.x) <= half.x && Mathf.Abs(local.y - c.y) <= half.y && Mathf.Abs(local.z - c.z) <= half.z;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (!box) return;
        Gizmos.color = Color.yellow;
        Matrix4x4 m = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.matrix = m;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
        if (anchor)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, anchor.transform.position);
        }
    }
#endif
}

using UnityEngine;

public class FixedTrackingCamera : MonoBehaviour
{
    [Header("Player")]
    public Transform target; // Sybau >/3

    [Header("Camera Settings")]
    [Tooltip("Camera Speed")]
    public float rotationSpeed = 5f;

    void Update()
    {
        if (target == null) return;

        Vector3 direction = target.position - transform.position;

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}

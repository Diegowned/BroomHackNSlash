using UnityEngine;

namespace BroomHackNSlash.Character
{
    /// <summary>
    /// Minimal third-person style character controller that reads Unity's legacy input axes.
    /// The goal is to provide a solid baseline that we can iterate on with combo attacks,
    /// air juggling, and other Devil May Cry inspired mechanics later.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class SimplePlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Units per second movement speed when walking on the ground.")]
        [SerializeField]
        private float moveSpeed = 6f;

        [Tooltip("Degrees per second the character rotates to face the move direction.")]
        [SerializeField]
        private float rotationSpeed = 720f;

        [Header("Jumping & Gravity")]
        [Tooltip("Height in meters for a single jump.")]
        [SerializeField]
        private float jumpHeight = 1.5f;

        [Tooltip("Gravity strength applied while airborne. Negative values pull the player down.")]
        [SerializeField]
        private float gravity = -20f;

        private CharacterController characterController;
        private Transform cameraTransform;
        private float verticalVelocity;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            cameraTransform = Camera.main != null ? Camera.main.transform : null;
        }

        private void Update()
        {
            MovePlayer();
            HandleJumpAndGravity();
        }

        private void MovePlayer()
        {
            Vector2 input = ReadMovementInput();
            Vector3 movement = Vector3.zero;

            if (input.sqrMagnitude > 0.0001f)
            {
                // Align movement with the camera's orientation when available.
                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;

                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();

                movement = forward * input.y + right * input.x;
                movement.Normalize();
                movement *= moveSpeed;

                RotateTowards(movement);
            }

            Vector3 velocity = movement + Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }

        private void RotateTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        private void HandleJumpAndGravity()
        {
            if (characterController.isGrounded)
            {
                if (verticalVelocity < 0f)
                {
                    // Small downward force keeps the character snapped to the ground.
                    verticalVelocity = -2f;
                }

                if (Input.GetButtonDown("Jump"))
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }

        private static Vector2 ReadMovementInput()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector2 input = new Vector2(horizontal, vertical);
            return input.sqrMagnitude > 1f ? input.normalized : input;
        }
    }
}
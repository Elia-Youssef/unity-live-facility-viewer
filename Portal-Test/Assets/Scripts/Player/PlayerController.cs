using UnityEngine;

namespace FacilityViewer.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputRouter))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float InputDeadZone = 0.0001f;

        [SerializeField] private PlayerSettings playerSettings;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;

        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float pitch;
        private bool isInitialized;

        public bool IsGrounded { get; private set; }
        public float CurrentHorizontalSpeed { get; private set; }
        public float VerticalVelocity => verticalVelocity;
        public float Pitch => pitch;

        private void Reset()
        {
            characterController = GetComponent<CharacterController>();
            inputRouter = GetComponent<PlayerInputRouter>();
            playerCamera = GetComponentInChildren<Camera>(true);
            cameraPivot = playerCamera != null ? playerCamera.transform.parent : null;
        }

        private void Awake()
        {
            characterController ??= GetComponent<CharacterController>();
            inputRouter ??= GetComponent<PlayerInputRouter>();
            playerCamera ??= GetComponentInChildren<Camera>(true);
            cameraPivot ??= playerCamera != null ? playerCamera.transform.parent : null;

            if (!ValidateConfiguration())
            {
                enabled = false;
                return;
            }

            characterController.slopeLimit = playerSettings.SlopeLimit;
            characterController.stepOffset = playerSettings.StepOffset;
            pitch = NormalizeSignedAngle(cameraPivot.localEulerAngles.x);
            isInitialized = true;
        }

        private void OnEnable()
        {
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            CurrentHorizontalSpeed = 0f;
            IsGrounded = false;
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            UpdateLook();
            UpdateMovement(Time.deltaTime);
        }

        private void OnDisable()
        {
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            CurrentHorizontalSpeed = 0f;
            IsGrounded = false;
        }

        private bool ValidateConfiguration()
        {
            if (playerSettings == null)
            {
                Debug.LogError("PlayerController requires a PlayerSettings asset.", this);
                return false;
            }

            if (characterController == null || inputRouter == null || cameraPivot == null || playerCamera == null)
            {
                Debug.LogError("PlayerController is missing one or more required prefab references.", this);
                return false;
            }

            return true;
        }

        private void UpdateLook()
        {
            Vector2 lookDelta = inputRouter.LookInput * playerSettings.LookSensitivity;

            transform.Rotate(0f, lookDelta.x, 0f, Space.Self);
            pitch = Mathf.Clamp(
                pitch - lookDelta.y,
                playerSettings.MinimumPitch,
                playerSettings.MaximumPitch);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void UpdateMovement(float deltaTime)
        {
            Vector2 moveInput = Vector2.ClampMagnitude(inputRouter.MoveInput, 1f);
            Vector3 localDirection = new(moveInput.x, 0f, moveInput.y);
            Vector3 worldDirection = transform.TransformDirection(localDirection);
            float targetSpeed = inputRouter.IsSprinting
                ? playerSettings.SprintSpeed
                : playerSettings.WalkSpeed;
            Vector3 targetHorizontalVelocity = worldDirection * targetSpeed;
            float velocityChangeRate = moveInput.sqrMagnitude > InputDeadZone
                ? playerSettings.Acceleration
                : playerSettings.Deceleration;

            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                targetHorizontalVelocity,
                velocityChangeRate * deltaTime);

            if (horizontalVelocity.sqrMagnitude < InputDeadZone)
            {
                horizontalVelocity = Vector3.zero;
            }

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = playerSettings.GroundedVerticalSpeed;
            }
            else
            {
                verticalVelocity += playerSettings.Gravity * deltaTime;
            }

            Vector3 frameVelocity = horizontalVelocity + Vector3.up * verticalVelocity;
            CollisionFlags collisionFlags = characterController.Move(frameVelocity * deltaTime);

            IsGrounded = (collisionFlags & CollisionFlags.Below) != 0 || characterController.isGrounded;

            if (IsGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = playerSettings.GroundedVerticalSpeed;
            }

            Vector3 actualVelocity = characterController.velocity;
            CurrentHorizontalSpeed = new Vector2(actualVelocity.x, actualVelocity.z).magnitude;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}

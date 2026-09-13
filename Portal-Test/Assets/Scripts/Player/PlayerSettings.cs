using UnityEngine;

namespace FacilityViewer.Player
{
    public sealed class PlayerSettings : ScriptableObject
    {
        [Header("Ground Movement")]
        [Tooltip("Maximum walking speed in metres per second.")]
        [SerializeField, Min(0f)] private float walkSpeed = 4f;

        [Tooltip("Maximum sprinting speed in metres per second.")]
        [SerializeField, Min(0f)] private float sprintSpeed = 6.5f;

        [Tooltip("How quickly horizontal movement reaches its target speed, in metres per second squared.")]
        [SerializeField, Min(0f)] private float acceleration = 18f;

        [Tooltip("How quickly horizontal movement stops when there is no movement input, in metres per second squared.")]
        [SerializeField, Min(0f)] private float deceleration = 22f;

        [Header("Vertical Motion")]
        [Tooltip("Downward acceleration in metres per second squared.")]
        [SerializeField, Range(-50f, -0.01f)] private float gravity = -20f;

        [Tooltip("Small downward speed retained while grounded to keep the controller attached to slopes.")]
        [SerializeField, Range(-10f, -0.01f)] private float groundedVerticalSpeed = -2f;

        [Header("Look")]
        [Tooltip("Degrees of camera rotation applied per mouse-delta unit.")]
        [SerializeField, Range(0.01f, 1f)] private float lookSensitivity = 0.1f;

        [Tooltip("Lowest permitted vertical camera angle in degrees.")]
        [SerializeField, Range(-89f, 0f)] private float minimumPitch = -85f;

        [Tooltip("Highest permitted vertical camera angle in degrees.")]
        [SerializeField, Range(0f, 89f)] private float maximumPitch = 85f;

        [Header("Character Controller")]
        [Tooltip("Steepest walkable surface angle in degrees.")]
        [SerializeField, Range(0f, 90f)] private float slopeLimit = 45f;

        [Tooltip("Maximum obstacle height the CharacterController can step over, in metres.")]
        [SerializeField, Range(0f, 0.5f)] private float stepOffset = 0.3f;

        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        public float Gravity => gravity;
        public float GroundedVerticalSpeed => groundedVerticalSpeed;
        public float LookSensitivity => lookSensitivity;
        public float MinimumPitch => minimumPitch;
        public float MaximumPitch => maximumPitch;
        public float SlopeLimit => slopeLimit;
        public float StepOffset => stepOffset;

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            acceleration = Mathf.Max(0f, acceleration);
            deceleration = Mathf.Max(0f, deceleration);
            gravity = Mathf.Clamp(gravity, -50f, -0.01f);
            groundedVerticalSpeed = Mathf.Clamp(groundedVerticalSpeed, -10f, -0.01f);
            lookSensitivity = Mathf.Clamp(lookSensitivity, 0.01f, 1f);
            minimumPitch = Mathf.Clamp(minimumPitch, -89f, 0f);
            maximumPitch = Mathf.Clamp(maximumPitch, 0f, 89f);
            slopeLimit = Mathf.Clamp(slopeLimit, 0f, 90f);
            stepOffset = Mathf.Clamp(stepOffset, 0f, 0.5f);
        }
    }
}

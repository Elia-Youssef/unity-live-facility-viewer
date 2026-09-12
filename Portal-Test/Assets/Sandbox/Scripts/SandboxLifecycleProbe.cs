using UnityEngine;

namespace FacilityViewer.Sandbox
{
    public sealed class SandboxLifecycleProbe : MonoBehaviour
    {
        [SerializeField] private SandboxSettings settings;
        [SerializeField] private Transform referencedTarget;
        [SerializeField] private bool rotateInPlayMode = true;

        public SandboxSettings Settings => settings;
        public Transform ReferencedTarget => referencedTarget;
        public bool RotateInPlayMode => rotateInPlayMode;

        private void Awake()
        {
            Debug.Log($"[Sandbox] {name}: Awake", this);
        }

        private void OnEnable()
        {
            Debug.Log($"[Sandbox] {name}: OnEnable", this);
        }

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogError($"[Sandbox] {name}: Settings reference is missing.", this);
                enabled = false;
                return;
            }

            string targetName = referencedTarget == null ? "none" : referencedTarget.name;
            Debug.Log(
                $"[Sandbox] {name}: Start — label '{settings.DisplayLabel}', target '{targetName}'",
                this);
        }

        private void Update()
        {
            if (!rotateInPlayMode || settings == null)
            {
                return;
            }

            transform.Rotate(Vector3.up, settings.RotationSpeed * Time.deltaTime, Space.World);
        }

        private void OnDisable()
        {
            Debug.Log($"[Sandbox] {name}: OnDisable", this);
        }

        private void OnDestroy()
        {
            Debug.Log($"[Sandbox] {name}: OnDestroy", this);
        }
    }
}

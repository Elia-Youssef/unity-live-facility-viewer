using UnityEngine;

namespace FacilityViewer.Sandbox
{
    [CreateAssetMenu(
        fileName = "SandboxSettings",
        menuName = "Facility Viewer/Sandbox Settings")]
    public sealed class SandboxSettings : ScriptableObject
    {
        [SerializeField] private string displayLabel = "Configured Crate";
        [SerializeField, Min(0f)] private float rotationSpeed = 45f;
        [SerializeField] private Color accentColor = new(0.1f, 0.65f, 1f, 1f);

        public string DisplayLabel => displayLabel;
        public float RotationSpeed => rotationSpeed;
        public Color AccentColor => accentColor;
    }
}

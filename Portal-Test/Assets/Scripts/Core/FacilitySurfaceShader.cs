using System;
using System.Collections.Generic;

namespace FacilityViewer.Core
{
    /// <summary>
    /// Stable identifiers for the authored FacilitySurface Shader Graph contract.
    /// Material themes validate against this contract instead of duplicating shader strings.
    /// </summary>
    public static class FacilitySurfaceShader
    {
        public const string ShaderName = "Shader Graphs/FacilitySurface";

        public const string BaseColor = "_BaseColor";
        public const string Metallic = "_Metallic";
        public const string Smoothness = "_Smoothness";
        public const string NormalMap = "_NormalMap";
        public const string NormalStrength = "_NormalStrength";
        public const string MaintenanceTint = "_MaintenanceTint";
        public const string MaintenanceBlend = "_MaintenanceBlend";
        public const string EmissionColor = "_EmissionColor";
        public const string EmissionIntensity = "_EmissionIntensity";
        public const string EmergencyPulseSpeed = "_EmergencyPulseSpeed";
        public const string EmergencyPulseAmount = "_EmergencyPulseAmount";

        private static readonly IReadOnlyList<string> RequiredProperties = Array.AsReadOnly(new[]
        {
            BaseColor,
            Metallic,
            Smoothness,
            NormalMap,
            NormalStrength,
            MaintenanceTint,
            MaintenanceBlend,
            EmissionColor,
            EmissionIntensity,
            EmergencyPulseSpeed,
            EmergencyPulseAmount
        });

        public static IReadOnlyList<string> RequiredPropertyNames => RequiredProperties;
    }
}

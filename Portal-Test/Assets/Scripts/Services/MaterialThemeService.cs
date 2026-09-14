using System;
using System.Collections.Generic;
using FacilityViewer.Core;
using FacilityViewer.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FacilityViewer.Services
{
    /// <summary>
    /// Persistent owner of the selected facility material theme. Scene-local ThemeTargets
    /// declare renderer slots; this service validates a catalog and applies saved shared
    /// materials without owning facility renderers or presentation controls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaterialThemeService : MonoBehaviour
    {
        [SerializeField] private AppState appState;
        [SerializeField] private MaterialThemeDefinition[] themeCatalog = Array.Empty<MaterialThemeDefinition>();

        private readonly Dictionary<string, MaterialThemeDefinition> definitionsById =
            new(StringComparer.Ordinal);
        private readonly HashSet<ThemeTarget> registeredTargets = new();
        private readonly HashSet<RendererSlotKey> registeredRendererSlots = new();
        private readonly Dictionary<ThemeTarget, RejectionState> rejectedTargets = new();
        private readonly List<ThemeTarget> staleTargetBuffer = new();

        public bool IsReady { get; private set; }
        public AppState State => appState;
        public MaterialThemeDefinition CurrentThemeDefinition { get; private set; }
        public int RegisteredTargetCount
        {
            get
            {
                RemoveStaleTargets();
                return registeredTargets.Count;
            }
        }

        public IReadOnlyList<MaterialThemeDefinition> ThemeCatalog =>
            Array.AsReadOnly(themeCatalog ?? Array.Empty<MaterialThemeDefinition>());

        private void Reset()
        {
            appState = GetComponent<AppState>();
        }

        private void OnEnable()
        {
            ThemeTarget.Enabled += OnTargetEnabled;
            ThemeTarget.Disabled += OnTargetDisabled;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            if (IsReady)
            {
                RefreshRegisteredTargets();
            }
        }

        private void OnDisable()
        {
            ThemeTarget.Enabled -= OnTargetEnabled;
            ThemeTarget.Disabled -= OnTargetDisabled;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            registeredTargets.Clear();
            rejectedTargets.Clear();
            staleTargetBuffer.Clear();
        }

        /// <summary>
        /// Validates the complete serialized catalog before making the service ready.
        /// </summary>
        public bool Initialize()
        {
            IsReady = false;
            CurrentThemeDefinition = null;
            definitionsById.Clear();
            registeredTargets.Clear();
            rejectedTargets.Clear();
            staleTargetBuffer.Clear();
            appState ??= GetComponent<AppState>();

            if (appState == null)
            {
                Debug.LogError("MaterialThemeService requires an AppState reference.", this);
                return false;
            }

            // NullGfx player runs do not expose Shader Graph property reflection. Keep every
            // structural and shader-identity check, and relax only HasProperty validation there.
            bool validateRequiredShaderProperties =
                MaterialThemeRuntimeValidationPolicy.ShouldValidateRequiredShaderProperties(
                    SystemInfo.graphicsDeviceType);
            if (!MaterialThemeDefinitionValidator.TryValidateCatalog(
                    themeCatalog,
                    validateRequiredShaderProperties,
                    out IReadOnlyList<string> errors))
            {
                Debug.LogError(
                    $"MaterialThemeService theme catalog is invalid: {string.Join(" ", errors)}",
                    this);
                return false;
            }

            for (int index = 0; index < themeCatalog.Length; index++)
            {
                MaterialThemeDefinition definition = themeCatalog[index];
                definitionsById.Add(definition.ThemeId, definition);
            }

            if (!definitionsById.TryGetValue(appState.SelectedThemeId, out MaterialThemeDefinition selectedTheme))
            {
                Debug.LogError(
                    $"MaterialThemeService cannot resolve AppState selected theme ID '{appState.SelectedThemeId}'.",
                    this);
                definitionsById.Clear();
                return false;
            }

            CurrentThemeDefinition = selectedTheme;
            IsReady = true;
            RefreshRegisteredTargets();
            return true;
        }

        /// <summary>
        /// Requests a theme by its stable data ID. Invalid requests do not change AppState or
        /// renderer assignments; all registered targets are validated before the first write.
        /// </summary>
        public bool RequestTheme(string themeId)
        {
            if (!IsReady)
            {
                Debug.LogWarning("[Material Theme] Request rejected because the service is not ready.", this);
                return false;
            }

            string normalizedThemeId = themeId?.Trim() ?? string.Empty;
            if (!definitionsById.TryGetValue(normalizedThemeId, out MaterialThemeDefinition requestedTheme))
            {
                Debug.LogWarning($"[Material Theme] Unknown theme ID '{normalizedThemeId}'.", this);
                return false;
            }

            RemoveStaleTargets();

            if (!TryValidateRegisteredTargets(requestedTheme, out string validationError))
            {
                Debug.LogError($"[Material Theme] Theme request rejected: {validationError}", this);
                return false;
            }

            if (!ApplyThemeToRegisteredTargets(requestedTheme, out string applicationError))
            {
                Debug.LogError($"[Material Theme] Theme request could not be applied: {applicationError}", this);
                return false;
            }

            CurrentThemeDefinition = requestedTheme;
            appState.SetSelectedTheme(requestedTheme.ThemeId);
            appState.SetStatusMessage($"{requestedTheme.DisplayName} theme active");
            return true;
        }

        public void RefreshRegisteredTargets()
        {
            RemoveStaleTargets();

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                RegisterScene(SceneManager.GetSceneAt(index));
            }
        }

        private void OnTargetEnabled(ThemeTarget target)
        {
            RegisterTarget(target);
        }

        private void OnTargetDisabled(ThemeTarget target)
        {
            if (target != null)
            {
                registeredTargets.Remove(target);
                rejectedTargets.Remove(target);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            RegisterScene(scene);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            registeredTargets.RemoveWhere(target =>
                target == null || target.gameObject.scene.handle == scene.handle);
            RemoveRejectedTargetsForScene(scene);
        }

        private void RegisterScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                ThemeTarget[] targets = roots[rootIndex].GetComponentsInChildren<ThemeTarget>(true);
                for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    RegisterTarget(targets[targetIndex]);
                }
            }
        }

        private void RegisterTarget(ThemeTarget target)
        {
            if (!IsReady || target == null || !target.isActiveAndEnabled)
            {
                return;
            }

            if (!target.gameObject.scene.IsValid() || !target.gameObject.scene.isLoaded)
            {
                return;
            }

            RemoveStaleTargets();

            if (registeredTargets.Contains(target))
            {
                return;
            }

            if (!TryValidateTargetSet(CurrentThemeDefinition, target, out string validationError))
            {
                ReportTargetRejection(target, "rejected", validationError);
                return;
            }

            if (!target.TryApplyTheme(CurrentThemeDefinition, out string applicationError))
            {
                ReportTargetRejection(target, "could not be applied", applicationError);
                return;
            }

            rejectedTargets.Remove(target);
            registeredTargets.Add(target);
        }

        private bool TryValidateRegisteredTargets(MaterialThemeDefinition theme, out string error)
        {
            return TryValidateTargetSet(theme, null, out error);
        }

        private bool TryValidateTargetSet(
            MaterialThemeDefinition theme,
            ThemeTarget candidateTarget,
            out string error)
        {
            registeredRendererSlots.Clear();
            try
            {
                foreach (ThemeTarget target in registeredTargets)
                {
                    if (!target.TryValidateTheme(theme, out error))
                    {
                        return false;
                    }

                    if (!TryClaimRendererSlots(target, out error))
                    {
                        return false;
                    }
                }

                if (candidateTarget != null)
                {
                    if (!candidateTarget.TryValidateTheme(theme, out error))
                    {
                        return false;
                    }

                    if (!TryClaimRendererSlots(candidateTarget, out error))
                    {
                        return false;
                    }
                }

                error = string.Empty;
                return true;
            }
            finally
            {
                registeredRendererSlots.Clear();
            }
        }

        private bool TryClaimRendererSlots(ThemeTarget target, out string error)
        {
            IReadOnlyList<ThemeTargetMaterialSlot> materialSlots = target.MaterialSlots;
            for (int index = 0; index < materialSlots.Count; index++)
            {
                ThemeTargetMaterialSlot mapping = materialSlots[index];
                RendererSlotKey key = new(mapping.Renderer, mapping.MaterialSlotIndex);
                if (!registeredRendererSlots.Add(key))
                {
                    error = $"Theme target '{target.name}' maps renderer '{mapping.Renderer.name}' " +
                        $"material slot {mapping.MaterialSlotIndex}, which is already claimed by another " +
                        "registered ThemeTarget.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private bool ApplyThemeToRegisteredTargets(MaterialThemeDefinition theme, out string error)
        {
            foreach (ThemeTarget target in registeredTargets)
            {
                if (!target.TryApplyTheme(theme, out error))
                {
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void RemoveStaleTargets()
        {
            registeredTargets.RemoveWhere(IsStaleTarget);
            RemoveStaleRejectedTargets();
        }

        private void ReportTargetRejection(ThemeTarget target, string operation, string error)
        {
            RejectionState currentState = new(GetMappingFingerprint(target), operation, error);
            if (rejectedTargets.TryGetValue(target, out RejectionState previousState)
                && previousState.Equals(currentState))
            {
                return;
            }

            rejectedTargets[target] = currentState;
            Debug.LogError($"[Material Theme] Target registration {operation}: {error}", target);
        }

        private void RemoveStaleRejectedTargets()
        {
            staleTargetBuffer.Clear();
            foreach (KeyValuePair<ThemeTarget, RejectionState> entry in rejectedTargets)
            {
                if (IsStaleTarget(entry.Key))
                {
                    staleTargetBuffer.Add(entry.Key);
                }
            }

            for (int index = 0; index < staleTargetBuffer.Count; index++)
            {
                rejectedTargets.Remove(staleTargetBuffer[index]);
            }

            staleTargetBuffer.Clear();
        }

        private void RemoveRejectedTargetsForScene(Scene scene)
        {
            staleTargetBuffer.Clear();
            foreach (KeyValuePair<ThemeTarget, RejectionState> entry in rejectedTargets)
            {
                ThemeTarget target = entry.Key;
                if (target == null || target.gameObject.scene.handle == scene.handle)
                {
                    staleTargetBuffer.Add(target);
                }
            }

            for (int index = 0; index < staleTargetBuffer.Count; index++)
            {
                rejectedTargets.Remove(staleTargetBuffer[index]);
            }

            staleTargetBuffer.Clear();
        }

        private static bool IsStaleTarget(ThemeTarget target)
        {
            return target == null
                || !target.isActiveAndEnabled
                || !target.gameObject.scene.IsValid()
                || !target.gameObject.scene.isLoaded;
        }

        private static int GetMappingFingerprint(ThemeTarget target)
        {
            unchecked
            {
                int fingerprint = 17;
                IReadOnlyList<ThemeTargetMaterialSlot> materialSlots = target.MaterialSlots;
                fingerprint = (fingerprint * 31) + materialSlots.Count;

                for (int index = 0; index < materialSlots.Count; index++)
                {
                    ThemeTargetMaterialSlot mapping = materialSlots[index];
                    if (mapping == null)
                    {
                        fingerprint *= 31;
                        continue;
                    }

                    Renderer renderer = mapping.Renderer;
                    fingerprint = (fingerprint * 31) + (renderer != null ? renderer.GetHashCode() : 0);
                    fingerprint = (fingerprint * 31) + mapping.MaterialSlotIndex;
                    fingerprint = (fingerprint * 31) + StringComparer.Ordinal.GetHashCode(
                        mapping.GroupId ?? string.Empty);
                }

                return fingerprint;
            }
        }

        private readonly struct RendererSlotKey : IEquatable<RendererSlotKey>
        {
            private readonly Renderer renderer;
            private readonly int materialSlotIndex;

            public RendererSlotKey(Renderer renderer, int materialSlotIndex)
            {
                this.renderer = renderer;
                this.materialSlotIndex = materialSlotIndex;
            }

            public bool Equals(RendererSlotKey other)
            {
                return renderer == other.renderer
                    && materialSlotIndex == other.materialSlotIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RendererSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((renderer != null ? renderer.GetHashCode() : 0) * 397) ^ materialSlotIndex;
                }
            }
        }

        private readonly struct RejectionState : IEquatable<RejectionState>
        {
            private readonly int mappingFingerprint;
            private readonly string operation;
            private readonly string error;

            public RejectionState(int mappingFingerprint, string operation, string error)
            {
                this.mappingFingerprint = mappingFingerprint;
                this.operation = operation;
                this.error = error;
            }

            public bool Equals(RejectionState other)
            {
                return mappingFingerprint == other.mappingFingerprint
                    && string.Equals(operation, other.operation, StringComparison.Ordinal)
                    && string.Equals(error, other.error, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RejectionState other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = mappingFingerprint;
                    hashCode = (hashCode * 397) ^ (operation != null ? operation.GetHashCode() : 0);
                    return (hashCode * 397) ^ (error != null ? error.GetHashCode() : 0);
                }
            }
        }
    }

    internal static class MaterialThemeRuntimeValidationPolicy
    {
        internal static bool ShouldValidateRequiredShaderProperties(
            GraphicsDeviceType graphicsDeviceType)
        {
            return graphicsDeviceType != GraphicsDeviceType.Null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using FacilityViewer.World;
using Unity.Profiling;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace FacilityViewer.Editor
{
    /// <summary>
    /// Editor-only capture for the Lobby material ownership comparison. It does not add runtime
    /// components or persist scene changes, and writes generated artifacts only to the local
    /// temporary directory.
    /// </summary>
    [InitializeOnLoad]
    public static class MaterialOwnershipProfilingCapture
    {
        public const int EvidenceSchemaVersion = 1;
        public const string CaptureFolderName = "facility-viewer-m5-captures";
        public const string SourceMaterialName = "FacilityStructure";
        public const string SnapshotFileName = "material-ownership-comparison.snap";
        public const string ReportFileName = "material-ownership-comparison.json";

        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string GroupName = "Material Ownership Comparison";
        private const int SamplingFrameCount = 4;
        private const double CaptureTimeoutSeconds = 120d;
        private const string SessionPrefix = "FacilityViewer.MaterialOwnershipProfilingCapture.";
        private const string SessionActiveKey = SessionPrefix + "active";
        private const string SessionStageKey = SessionPrefix + "stage";
        private const string SessionDirectoryKey = SessionPrefix + "directory";
        private const string SessionExitWhenCompleteKey = SessionPrefix + "exitWhenComplete";
        private const string SessionSucceededKey = SessionPrefix + "succeeded";
        private const string SessionDeadlineTicksKey = SessionPrefix + "deadlineTicks";
        private const string SessionSampledFramesKey = SessionPrefix + "sampledFrames";
        private const string SessionSceneSetupKey = SessionPrefix + "sceneSetup";
        private const string SessionRestoreSceneSetupKey = SessionPrefix + "restoreSceneSetup";
        private const string SessionReportJsonKey = SessionPrefix + "reportJson";

        private static readonly CounterDefinition[] CounterDefinitions =
        {
            new("drawCalls", ProfilerCategory.Render, "Draw Calls Count"),
            new("setPassCalls", ProfilerCategory.Render, "SetPass Calls Count"),
            new("triangles", ProfilerCategory.Render, "Triangles Count"),
            new("vertices", ProfilerCategory.Render, "Vertices Count"),
            new("gcAllocatedInFrame", ProfilerCategory.Memory, "GC Allocated In Frame")
        };

        private static readonly List<CounterRecorder> CounterRecorders = new();
        private static readonly List<Material> MaterialBuffer = new();
        private static readonly HashSet<Material> DistinctMaterials = new();

        private static CaptureStage stage;
        private static bool exitEditorWhenComplete;
        private static bool snapshotComplete;
        private static bool snapshotSucceeded;
        private static bool captureSucceeded;
        private static int sampledFrames;
        private static long captureDeadlineTicks;
        private static string captureDirectory;
        private static string snapshotPath;
        private static string completedSnapshotPath;
        private static MaterialOwnershipProfileReport report;
        private static bool isRestoringSceneSetup;

        static MaterialOwnershipProfilingCapture()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode
                && stage != CaptureStage.Idle
                && stage != CaptureStage.Completing
                && stage != CaptureStage.RestorationFailed)
            {
                Complete(false, "Play Mode ended before the material ownership capture completed.");
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            if (stage == CaptureStage.Idle
                || stage == CaptureStage.Completing
                || stage == CaptureStage.RestorationFailed)
            {
                return;
            }

            if (stage == CaptureStage.WaitForSnapshot)
            {
                Complete(false, "Domain reload interrupted an in-flight Memory Profiler snapshot.");
                return;
            }

            DisposeCounters();
            SaveCaptureProgress(stage == CaptureStage.Sampling ? "sampling" : "waitForComparison");
        }

        [MenuItem("Tools/Facility Viewer/Profiling/Capture Material Ownership Evidence")]
        private static void CaptureFromMenu()
        {
            BeginCapture(false);
        }

        /// <summary>
        /// Command-line entry point. It exits the Editor after the snapshot callback writes the
        /// local report, so callers must not also pass Unity's -quit option.
        /// </summary>
        public static void CaptureFromBatch()
        {
            BeginCapture(true);
        }

        public static string CreateSourceEvidenceJson()
        {
            return JsonUtility.ToJson(new SourceEvidence
            {
                schemaVersion = EvidenceSchemaVersion,
                scenario = "lobby-material-ownership-comparison",
                unityVersion = Application.unityVersion,
                rendererCount = 3,
                savedSharedMaterialReferenceCount = 2,
                propertyBlockOverrideRendererCount = 1,
                ownedRuntimeMaterialReferenceCount = 1,
                distinctComparisonMaterialObjectCount = 2,
                frameDebuggerStatus = "manual-native-window-gate",
                runtimeCountersStatus = "local-capture-only",
                memorySnapshotStatus = "local-temp-snapshot-only"
            }, true);
        }

        public static string SerializeSceneSetupForTesting(SceneSetup[] sceneSetup)
        {
            return SerializeSceneSetup(sceneSetup);
        }

        public static SceneSetup[] DeserializeSceneSetupForTesting(string serializedSceneSetup)
        {
            return DeserializeSceneSetup(serializedSceneSetup);
        }

        private static void BeginCapture(bool exitWhenFinished)
        {
            if (stage != CaptureStage.Idle || isRestoringSceneSetup
                || SessionState.GetBool(SessionActiveKey, false))
            {
                throw new InvalidOperationException(
                    "A material ownership profiling capture is active or awaiting scene restoration.");
            }

            exitEditorWhenComplete = exitWhenFinished;
            SceneSetup[] previousSceneSetup = null;
            if (!exitWhenFinished && !TryPrepareInteractiveCapture(out previousSceneSetup))
            {
                return;
            }

            try
            {
                captureDirectory = Path.Combine(
                    Path.GetTempPath(),
                    CaptureFolderName,
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                snapshotPath = Path.Combine(captureDirectory, SnapshotFileName);
                completedSnapshotPath = string.Empty;
                snapshotComplete = false;
                snapshotSucceeded = false;
                captureSucceeded = false;
                sampledFrames = 0;
                report = null;
                Directory.CreateDirectory(captureDirectory);

                captureDeadlineTicks = DateTime.UtcNow.AddSeconds(CaptureTimeoutSeconds).Ticks;
                stage = CaptureStage.WaitForComparison;
                SaveRunningSession(exitWhenFinished ? null : previousSceneSetup);
                EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Complete(false, exception.Message);
            }
        }

        private static void Tick()
        {
            try
            {
                RestoreCaptureAfterDomainReload();
                if (stage == CaptureStage.Idle)
                {
                    return;
                }

                if (DateTime.UtcNow.Ticks > captureDeadlineTicks
                    && stage != CaptureStage.Completing
                    && stage != CaptureStage.RestorationFailed)
                {
                    Complete(false, "Capture timed out before the Memory Profiler callback completed.");
                    return;
                }

                switch (stage)
                {
                    case CaptureStage.WaitForComparison:
                        TryStartSampling();
                        break;
                    case CaptureStage.Sampling:
                        CollectSamplingFrame();
                        break;
                    case CaptureStage.WaitForSnapshot:
                        if (snapshotComplete)
                        {
                            Complete(snapshotSucceeded, snapshotSucceeded
                                ? string.Empty
                                : "The Memory Profiler reported that snapshot creation failed.");
                        }

                        break;
                    case CaptureStage.Completing:
                        FinishAfterPlayModeStops();
                        break;
                    case CaptureStage.RestorationFailed:
                        break;
                }
            }
            catch (Exception exception)
            {
                Complete(false, exception.Message);
            }
        }

        private static void RestoreCaptureAfterDomainReload()
        {
            if (stage != CaptureStage.Idle || !SessionState.GetBool(SessionActiveKey, false))
            {
                return;
            }

            captureDirectory = SessionState.GetString(SessionDirectoryKey, string.Empty);
            exitEditorWhenComplete = SessionState.GetBool(SessionExitWhenCompleteKey, false);
            captureSucceeded = SessionState.GetBool(SessionSucceededKey, false);
            sampledFrames = SessionState.GetInt(SessionSampledFramesKey, 0);
            string serializedReport = SessionState.GetString(SessionReportJsonKey, string.Empty);
            if (!string.IsNullOrEmpty(serializedReport))
            {
                report = JsonUtility.FromJson<MaterialOwnershipProfileReport>(serializedReport);
            }
            captureDeadlineTicks = long.TryParse(
                SessionState.GetString(SessionDeadlineTicksKey, string.Empty),
                out long restoredDeadlineTicks)
                ? restoredDeadlineTicks
                : DateTime.UtcNow.AddSeconds(CaptureTimeoutSeconds).Ticks;
            snapshotPath = Path.Combine(captureDirectory, SnapshotFileName);
            string storedStage = SessionState.GetString(SessionStageKey, string.Empty);
            switch (storedStage)
            {
                case "completing":
                    stage = CaptureStage.Completing;
                    break;
                case "restorationFailed":
                    stage = CaptureStage.RestorationFailed;
                    break;
                case "waitForSnapshot":
                    stage = CaptureStage.WaitForSnapshot;
                    Complete(false, "Domain reload interrupted an in-flight Memory Profiler snapshot.");
                    break;
                default:
                    sampledFrames = 0;
                    stage = CaptureStage.WaitForComparison;
                    SaveCaptureProgress("waitForComparison");
                    break;
            }
        }

        private static void TryStartSampling()
        {
            Scene lobbyScene = SceneManager.GetSceneByPath(LobbyScenePath);
            if (!EditorApplication.isPlaying || !lobbyScene.isLoaded)
            {
                return;
            }

            Transform comparison = FindComparison(lobbyScene);
            if (comparison == null)
            {
                return;
            }

            StartCounters();
            stage = CaptureStage.Sampling;
            SaveCaptureProgress("sampling");
        }

        private static void CollectSamplingFrame()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.Render();
            }

            sampledFrames++;
            SaveCaptureProgress("sampling");
            if (sampledFrames < SamplingFrameCount)
            {
                return;
            }

            report = CreateRuntimeReport(FindComparison(SceneManager.GetSceneByPath(LobbyScenePath)));
            stage = CaptureStage.WaitForSnapshot;
            SaveCaptureProgress("waitForSnapshot");
            MemoryProfiler.TakeSnapshot(
                snapshotPath,
                OnSnapshotCompleted,
                CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects);
        }

        private static void OnSnapshotCompleted(string path, bool success)
        {
            completedSnapshotPath = path ?? string.Empty;
            snapshotSucceeded = success && File.Exists(completedSnapshotPath);
            snapshotComplete = true;
        }

        private static MaterialOwnershipProfileReport CreateRuntimeReport(Transform comparison)
        {
            if (comparison == null)
            {
                throw new InvalidOperationException("The material ownership comparison is not loaded.");
            }

            Renderer sharedRenderer = FindRenderer(comparison, "Shared Saved Material");
            Renderer propertyBlockRenderer = FindRenderer(comparison, "Property Block Override");
            Renderer ownedRenderer = FindRenderer(comparison, "Owned Runtime Instance");
            ShaderParameterController propertyBlockController =
                propertyBlockRenderer.GetComponent<ShaderParameterController>();
            OwnedRuntimeMaterialInstance instanceOwner =
                ownedRenderer.GetComponent<OwnedRuntimeMaterialInstance>();
            Material sourceMaterial = GetSingleSharedMaterial(sharedRenderer);
            Material propertyBlockMaterial = GetSingleSharedMaterial(propertyBlockRenderer);
            Material ownedMaterial = GetSingleSharedMaterial(ownedRenderer);

            if (sourceMaterial == null || sourceMaterial.name != SourceMaterialName)
            {
                throw new InvalidOperationException("The shared comparison material is not the saved Standard FacilityStructure material.");
            }

            if (propertyBlockController == null || !propertyBlockController.IsApplied)
            {
                throw new InvalidOperationException("The property-block comparison controller is not applied.");
            }

            if (instanceOwner == null || instanceOwner.RuntimeMaterial == null
                || instanceOwner.CreatedInstanceCount != 1)
            {
                throw new InvalidOperationException("The owned runtime material comparison has not created exactly one instance.");
            }

            DistinctMaterials.Clear();
            DistinctMaterials.Add(sourceMaterial);
            DistinctMaterials.Add(propertyBlockMaterial);
            DistinctMaterials.Add(ownedMaterial);
            int savedSourceReferenceCount = 1;
            savedSourceReferenceCount += propertyBlockMaterial == sourceMaterial ? 1 : 0;
            savedSourceReferenceCount += ownedMaterial == sourceMaterial ? 1 : 0;

            var evidence = new MaterialOwnershipProfileReport
            {
                schemaVersion = EvidenceSchemaVersion,
                scenario = "lobby-material-ownership-comparison",
                unityVersion = Application.unityVersion,
                sampledFrameCount = sampledFrames,
                comparisonRendererCount = 3,
                savedSharedMaterialReferenceCount = savedSourceReferenceCount,
                propertyBlockOverrideRendererCount = 1,
                ownedRuntimeMaterialReferenceCount = ownedMaterial == instanceOwner.RuntimeMaterial ? 1 : 0,
                distinctComparisonMaterialObjectCount = DistinctMaterials.Count,
                loadedMaterialObjectCount = Resources.FindObjectsOfTypeAll<Material>().Length,
                totalAllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong(),
                totalReservedMemoryBytes = Profiler.GetTotalReservedMemoryLong(),
                monoUsedMemoryBytes = Profiler.GetMonoUsedSizeLong(),
                frameDebuggerStatus = Application.isBatchMode
                    ? "manual-native-window-gate-batch-mode"
                    : "manual-native-window-gate",
                counters = ReadCounters()
            };

            return evidence;
        }

        private static void Complete(bool success, string error)
        {
            if (stage == CaptureStage.Completing)
            {
                return;
            }

            captureSucceeded = success;
            if (report == null)
            {
                report = new MaterialOwnershipProfileReport
                {
                    schemaVersion = EvidenceSchemaVersion,
                    scenario = "lobby-material-ownership-comparison",
                    unityVersion = Application.unityVersion,
                    frameDebuggerStatus = Application.isBatchMode
                        ? "manual-native-window-gate-batch-mode"
                        : "manual-native-window-gate"
                };
            }

            report.snapshotCaptureSucceeded = success;
            report.snapshotFileName = success ? Path.GetFileName(completedSnapshotPath) : string.Empty;
            report.snapshotByteCount = success ? new FileInfo(completedSnapshotPath).Length : 0L;
            report.error = error ?? string.Empty;
            WriteLocalReport(report);
            SessionState.SetString(SessionReportJsonKey, JsonUtility.ToJson(report));
            DisposeCounters();
            MaterialBuffer.Clear();
            DistinctMaterials.Clear();
            stage = CaptureStage.Completing;
            SaveCaptureProgress("completing");
        }

        private static void FinishAfterPlayModeStops()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }

                return;
            }

            bool sceneSetupRestored = exitEditorWhenComplete || RestoreInteractiveSceneSetup();
            if (!sceneSetupRestored)
            {
                captureSucceeded = false;
                if (report != null)
                {
                    report.error = "The capture completed, but the prior scene setup could not be restored.";
                    WriteLocalReport(report);
                    SessionState.SetString(SessionReportJsonKey, JsonUtility.ToJson(report));
                }

                stage = CaptureStage.RestorationFailed;
                SaveCaptureProgress("restorationFailed");
                Debug.LogError("Material ownership profiling could not restore the prior scene setup.");
                return;
            }

            stage = CaptureStage.Idle;
            ClearSession();
            string reportPath = Path.Combine(captureDirectory, ReportFileName);
            if (captureSucceeded && sceneSetupRestored)
            {
                Debug.Log($"Material ownership profiling evidence saved to '{reportPath}'.");
            }
            else
            {
                Debug.LogError($"Material ownership profiling capture did not complete cleanly. See '{reportPath}'.");
            }

            if (exitEditorWhenComplete)
            {
                EditorApplication.Exit(captureSucceeded ? 0 : 1);
            }
        }

        private static bool TryPrepareInteractiveCapture(out SceneSetup[] previousSceneSetup)
        {
            previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Material Ownership Profiling",
                    "Wait for compilation and Play Mode transitions to finish before starting a capture.",
                    "OK");
                return false;
            }

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && string.IsNullOrEmpty(scene.path))
                {
                    EditorUtility.DisplayDialog(
                        "Material Ownership Profiling",
                        "Save or close the untitled scene before starting a capture. The capture will not discard it.",
                        "OK");
                    return false;
                }
            }

            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        private static bool RestoreInteractiveSceneSetup()
        {
            if (!SessionState.GetBool(SessionRestoreSceneSetupKey, false))
            {
                return true;
            }

            try
            {
                isRestoringSceneSetup = true;
                EditorSceneManager.RestoreSceneManagerSetup(DeserializeSceneSetup(
                    SessionState.GetString(SessionSceneSetupKey, string.Empty)));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Material ownership profiling could not restore the prior scene setup: {exception.Message}");
                return false;
            }
            finally
            {
                isRestoringSceneSetup = false;
            }
        }

        private static void SaveRunningSession(SceneSetup[] previousSceneSetup)
        {
            SessionState.SetBool(SessionActiveKey, true);
            SessionState.SetString(SessionDirectoryKey, captureDirectory);
            SessionState.SetBool(SessionExitWhenCompleteKey, exitEditorWhenComplete);
            SessionState.SetString(SessionDeadlineTicksKey, captureDeadlineTicks.ToString());
            SessionState.SetBool(SessionRestoreSceneSetupKey, previousSceneSetup != null);
            if (previousSceneSetup != null)
            {
                SessionState.SetString(SessionSceneSetupKey, SerializeSceneSetup(previousSceneSetup));
            }

            SaveCaptureProgress("waitForComparison");
        }

        private static void SaveCaptureProgress(string stageName)
        {
            SessionState.SetString(SessionStageKey, stageName);
            SessionState.SetBool(SessionSucceededKey, captureSucceeded);
            SessionState.SetInt(SessionSampledFramesKey, sampledFrames);
        }

        private static void ClearSession()
        {
            SessionState.EraseBool(SessionActiveKey);
            SessionState.EraseString(SessionStageKey);
            SessionState.EraseString(SessionDirectoryKey);
            SessionState.EraseBool(SessionExitWhenCompleteKey);
            SessionState.EraseBool(SessionSucceededKey);
            SessionState.EraseString(SessionDeadlineTicksKey);
            SessionState.EraseInt(SessionSampledFramesKey);
            SessionState.EraseString(SessionSceneSetupKey);
            SessionState.EraseBool(SessionRestoreSceneSetupKey);
            SessionState.EraseString(SessionReportJsonKey);
        }

        private static string SerializeSceneSetup(SceneSetup[] sceneSetup)
        {
            if (sceneSetup == null)
            {
                return string.Empty;
            }

            var storedSetup = new StoredSceneSetup
            {
                scenes = new StoredScene[sceneSetup.Length]
            };
            for (int index = 0; index < sceneSetup.Length; index++)
            {
                storedSetup.scenes[index] = new StoredScene
                {
                    path = sceneSetup[index].path,
                    isLoaded = sceneSetup[index].isLoaded,
                    isActive = sceneSetup[index].isActive
                };
            }

            return JsonUtility.ToJson(storedSetup);
        }

        private static SceneSetup[] DeserializeSceneSetup(string serializedSceneSetup)
        {
            StoredSceneSetup storedSetup = JsonUtility.FromJson<StoredSceneSetup>(serializedSceneSetup);
            if (storedSetup?.scenes == null || storedSetup.scenes.Length == 0)
            {
                throw new InvalidOperationException("The prior scene setup is missing or empty.");
            }

            SceneSetup[] sceneSetup = new SceneSetup[storedSetup.scenes.Length];
            for (int index = 0; index < storedSetup.scenes.Length; index++)
            {
                StoredScene storedScene = storedSetup.scenes[index];
                if (string.IsNullOrEmpty(storedScene.path))
                {
                    throw new InvalidOperationException("The prior scene setup contains an untitled scene.");
                }

                sceneSetup[index] = new SceneSetup
                {
                    path = storedScene.path,
                    isLoaded = storedScene.isLoaded,
                    isActive = storedScene.isActive
                };
            }

            return sceneSetup;
        }

        private static void StartCounters()
        {
            DisposeCounters();
            for (int index = 0; index < CounterDefinitions.Length; index++)
            {
                CounterDefinition definition = CounterDefinitions[index];
                CounterRecorders.Add(new CounterRecorder
                {
                    id = definition.id,
                    recorder = ProfilerRecorder.StartNew(definition.category, definition.markerName)
                });
            }
        }

        private static CounterReport[] ReadCounters()
        {
            CounterReport[] counters = new CounterReport[CounterRecorders.Count];
            for (int index = 0; index < CounterRecorders.Count; index++)
            {
                CounterRecorder counter = CounterRecorders[index];
                counters[index] = new CounterReport
                {
                    id = counter.id,
                    supported = counter.recorder.Valid,
                    lastValue = counter.recorder.Valid ? counter.recorder.LastValue : 0L
                };
            }

            return counters;
        }

        private static void DisposeCounters()
        {
            for (int index = 0; index < CounterRecorders.Count; index++)
            {
                if (CounterRecorders[index].recorder.Valid)
                {
                    CounterRecorders[index].recorder.Dispose();
                }
            }

            CounterRecorders.Clear();
        }

        private static Transform FindComparison(Scene scene)
        {
            if (!scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length != 1)
            {
                return null;
            }

            return roots[0].transform.Find(GroupName);
        }

        private static Renderer FindRenderer(Transform comparison, string name)
        {
            Transform child = comparison.Find(name);
            if (child == null)
            {
                throw new InvalidOperationException($"The comparison is missing '{name}'.");
            }

            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException($"The comparison object '{name}' has no renderer.");
            }

            return renderer;
        }

        private static Material GetSingleSharedMaterial(Renderer renderer)
        {
            MaterialBuffer.Clear();
            renderer.GetSharedMaterials(MaterialBuffer);
            if (MaterialBuffer.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' must have exactly one material slot for profiling.");
            }

            return MaterialBuffer[0];
        }

        private static void WriteLocalReport(MaterialOwnershipProfileReport profileReport)
        {
            string reportPath = Path.Combine(captureDirectory, ReportFileName);
            File.WriteAllText(reportPath, JsonUtility.ToJson(profileReport, true));
        }

        private enum CaptureStage
        {
            Idle,
            WaitForComparison,
            Sampling,
            WaitForSnapshot,
            Completing,
            RestorationFailed
        }

        private readonly struct CounterDefinition
        {
            public CounterDefinition(string id, ProfilerCategory category, string markerName)
            {
                this.id = id;
                this.category = category;
                this.markerName = markerName;
            }

            public readonly string id;
            public readonly ProfilerCategory category;
            public readonly string markerName;
        }

        private struct CounterRecorder
        {
            public string id;
            public ProfilerRecorder recorder;
        }

        [Serializable]
        private sealed class StoredSceneSetup
        {
            public StoredScene[] scenes;
        }

        [Serializable]
        private sealed class StoredScene
        {
            public string path;
            public bool isLoaded;
            public bool isActive;
        }

        [Serializable]
        private sealed class SourceEvidence
        {
            public int schemaVersion;
            public string scenario;
            public string unityVersion;
            public int rendererCount;
            public int savedSharedMaterialReferenceCount;
            public int propertyBlockOverrideRendererCount;
            public int ownedRuntimeMaterialReferenceCount;
            public int distinctComparisonMaterialObjectCount;
            public string frameDebuggerStatus;
            public string runtimeCountersStatus;
            public string memorySnapshotStatus;
        }

        [Serializable]
        private sealed class MaterialOwnershipProfileReport
        {
            public int schemaVersion;
            public string scenario;
            public string unityVersion;
            public int sampledFrameCount;
            public int comparisonRendererCount;
            public int savedSharedMaterialReferenceCount;
            public int propertyBlockOverrideRendererCount;
            public int ownedRuntimeMaterialReferenceCount;
            public int distinctComparisonMaterialObjectCount;
            public int loadedMaterialObjectCount;
            public long totalAllocatedMemoryBytes;
            public long totalReservedMemoryBytes;
            public long monoUsedMemoryBytes;
            public string frameDebuggerStatus;
            public CounterReport[] counters;
            public bool snapshotCaptureSucceeded;
            public string snapshotFileName;
            public long snapshotByteCount;
            public string error;
        }

        [Serializable]
        private sealed class CounterReport
        {
            public string id;
            public bool supported;
            public long lastValue;
        }
    }
}

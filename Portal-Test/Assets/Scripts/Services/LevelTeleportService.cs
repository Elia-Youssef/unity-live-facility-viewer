using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using FacilityViewer.Core;
using FacilityViewer.Player;
using FacilityViewer.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FacilityViewer.Services
{
    [DisallowMultipleComponent]
    public sealed class LevelTeleportService : MonoBehaviour
    {
        [SerializeField] private AppState appState;
        [SerializeField] private GameObject playerRoot;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private LevelDefinition initialLevel;
        [SerializeField] private LevelDefinition[] availableLevels = { };

        private Coroutine transitionRoutine;
        private Scene activeFacilityScene;
        private int requestSequence;
        private Func<Scene, AsyncOperation> unloadSceneOperation = SceneManager.UnloadSceneAsync;

        public bool IsReady { get; private set; }
        public AppState State => appState;
        public GameObject PlayerRoot => playerRoot;
        public CharacterController CharacterController => characterController;
        public LevelDefinition InitialLevel => initialLevel;
        public IReadOnlyList<LevelDefinition> OrderedLevelCatalog =>
            System.Array.AsReadOnly(availableLevels ?? System.Array.Empty<LevelDefinition>());
        public bool IsTransitionActive => transitionRoutine != null;

        private void Reset()
        {
            appState = GetComponent<AppState>();
        }

        public bool Initialize()
        {
            IsReady = false;

            appState ??= GetComponent<AppState>();

            if (appState == null)
            {
                Debug.LogError("LevelTeleportService requires an AppState reference.", this);
                return false;
            }

            if (playerRoot == null)
            {
                Debug.LogError("LevelTeleportService requires the persistent Player root.", this);
                return false;
            }

            characterController ??= playerRoot.GetComponent<CharacterController>();

            if (characterController == null || playerRoot.GetComponent<PlayerController>() == null)
            {
                Debug.LogError(
                    "LevelTeleportService requires a Player root containing CharacterController and PlayerController.",
                    this);
                return false;
            }

            if (!ValidateLevelCatalog(out string catalogError))
            {
                Debug.LogError($"LevelTeleportService level catalog is invalid: {catalogError}", this);
                return false;
            }

            IsReady = true;
            return true;
        }

        public bool RequestInitialLevel()
        {
            return RequestTransition(initialLevel);
        }

        public bool RequestTransition(string levelId)
        {
            LevelDefinition destination = FindLevel(levelId);

            if (destination == null)
            {
                string message = $"Unknown facility level ID: '{levelId}'";
                appState?.SetFailure(message);
                Debug.LogError($"[Transition] {message}", this);
                return false;
            }

            return RequestTransition(destination);
        }

        public bool RequestTransition(LevelDefinition destination)
        {
            string spawnId = destination != null ? destination.SpawnPointId : string.Empty;
            return RequestTransition(destination, spawnId);
        }

        public bool RequestTransition(LevelDefinition destination, string spawnPointId)
        {
            if (!IsReady)
            {
                appState?.SetFailure("Facility transition service is not ready");
                Debug.LogError("[Transition] Request rejected because the service is not ready.", this);
                return false;
            }

            if (transitionRoutine != null || appState.IsTransitioning)
            {
                Debug.LogWarning("[Transition] Request rejected because another transition is active.", this);
                return false;
            }

            string normalizedSpawnId = spawnPointId?.Trim() ?? string.Empty;

            if (destination != null && string.IsNullOrWhiteSpace(normalizedSpawnId))
            {
                string message = $"No destination spawn ID was provided for {destination.DisplayName}.";
                appState.SetFailure(message);
                Debug.LogError($"[Transition] {message}", this);
                return false;
            }

            transitionRoutine = StartCoroutine(TransitionTo(
                destination,
                normalizedSpawnId,
                ++requestSequence));
            return true;
        }

        private IEnumerator TransitionTo(
            LevelDefinition destination,
            string requestedSpawnId,
            int requestId)
        {
            float startedAt = Time.realtimeSinceStartup;
            string oldSceneName = activeFacilityScene.IsValid() ? activeFacilityScene.name : "None";
            string destinationName = destination != null ? destination.DisplayName : "Missing";
            string spawnId = destination != null ? requestedSpawnId : "Missing";

            SetPhase(
                FacilityTransitionPhase.Validating,
                $"Validating {destinationName}",
                requestId,
                oldSceneName,
                destinationName,
                spawnId,
                startedAt);

            // Guarantees the active routine is observable before any validation can finish.
            yield return null;

            if (destination == null)
            {
                yield return FailTransition(
                    "Destination definition is missing.",
                    default,
                    false,
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);
                yield break;
            }

            if (!destination.TryValidate(out string definitionError))
            {
                yield return FailTransition(
                    definitionError,
                    default,
                    false,
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);
                yield break;
            }

            if (!Application.CanStreamedLevelBeLoaded(destination.ScenePath))
            {
                yield return FailTransition(
                    $"Scene is not enabled in the build: {destination.ScenePath}",
                    default,
                    false,
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);
                yield break;
            }

            Scene destinationScene = SceneManager.GetSceneByPath(destination.ScenePath);

            bool loadedByRequest = !destinationScene.IsValid() || !destinationScene.isLoaded;

            if (loadedByRequest)
            {
                SetPhase(
                    FacilityTransitionPhase.Loading,
                    $"Loading {destination.DisplayName}",
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);

                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
                    destination.ScenePath,
                    LoadSceneMode.Additive);

                if (loadOperation == null)
                {
                    yield return FailTransition(
                        $"Unity could not start loading {destination.DisplayName}.",
                        default,
                        false,
                        requestId,
                        oldSceneName,
                        destinationName,
                        spawnId,
                        startedAt);
                    yield break;
                }

                while (!loadOperation.isDone)
                {
                    yield return null;
                }

                destinationScene = SceneManager.GetSceneByPath(destination.ScenePath);
            }

            if (!destinationScene.IsValid() || !destinationScene.isLoaded)
            {
                yield return FailTransition(
                    $"Loaded scene could not be resolved: {destination.ScenePath}",
                    destinationScene,
                    loadedByRequest,
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);
                yield break;
            }

            if (!TryFindSpawnPoint(destinationScene, spawnId, out SpawnPoint spawnPoint, out string spawnError))
            {
                yield return FailTransition(
                    spawnError,
                    destinationScene,
                    loadedByRequest,
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);
                yield break;
            }

            SetPhase(
                FacilityTransitionPhase.Teleporting,
                $"Entering {destination.DisplayName}",
                requestId,
                oldSceneName,
                destinationName,
                spawnId,
                startedAt);

            Vector3 previousPlayerPosition = playerRoot.transform.position;
            Quaternion previousPlayerRotation = playerRoot.transform.rotation;
            TeleportPlayer(spawnPoint.transform);
            SceneManager.SetActiveScene(destinationScene);

            if (activeFacilityScene.IsValid()
                && activeFacilityScene.isLoaded
                && activeFacilityScene.handle != destinationScene.handle)
            {
                SetPhase(
                    FacilityTransitionPhase.Unloading,
                    $"Finishing transition to {destination.DisplayName}",
                    requestId,
                    oldSceneName,
                    destinationName,
                    spawnId,
                    startedAt);

                AsyncOperation unloadOperation = BeginSceneUnload(activeFacilityScene);

                if (unloadOperation == null)
                {
                    // Keep the service, app state, and player with the existing facility before
                    // rolling back the destination loaded for this request.
                    SceneManager.SetActiveScene(activeFacilityScene);
                    TeleportPlayer(previousPlayerPosition, previousPlayerRotation);
                    yield return FailTransition(
                        $"Unity could not start unloading the previous facility: {oldSceneName}.",
                        destinationScene,
                        loadedByRequest,
                        requestId,
                        oldSceneName,
                        destinationName,
                        spawnId,
                        startedAt);
                    yield break;
                }

                while (!unloadOperation.isDone)
                {
                    yield return null;
                }
            }

            activeFacilityScene = destinationScene;
            appState.SetCurrentLevel(destination.LevelId, destination.DisplayName);
            appState.SetTransitionPhase(
                FacilityTransitionPhase.Complete,
                $"{destination.DisplayName} ready");
            LogPhase(
                requestId,
                oldSceneName,
                destinationName,
                spawnId,
                FacilityTransitionPhase.Complete,
                startedAt,
                "success");
            transitionRoutine = null;
        }

        private IEnumerator FailTransition(
            string message,
            Scene sceneToCleanUp,
            bool unloadScene,
            int requestId,
            string oldSceneName,
            string destinationName,
            string spawnId,
            float startedAt)
        {
            string failureMessage = message;

            if (unloadScene && sceneToCleanUp.IsValid() && sceneToCleanUp.isLoaded)
            {
                AsyncOperation unloadOperation = BeginSceneUnload(sceneToCleanUp);

                if (unloadOperation == null)
                {
                    Debug.LogWarning(
                        $"[Transition] Cleanup unload did not start for failed destination {destinationName}; retrying with Unity scene manager.",
                        this);
                    unloadOperation = SceneManager.UnloadSceneAsync(sceneToCleanUp);
                }

                if (unloadOperation != null)
                {
                    while (!unloadOperation.isDone)
                    {
                        yield return null;
                    }
                }

                if (sceneToCleanUp.IsValid() && sceneToCleanUp.isLoaded)
                {
                    failureMessage =
                        $"{message} Failed to unload the failed destination scene: {sceneToCleanUp.name}.";
                }
            }

            appState.SetFailure(failureMessage);
            LogFailure(requestId, oldSceneName, destinationName, spawnId, startedAt, failureMessage);
            transitionRoutine = null;
        }

        private void TeleportPlayer(Transform destination)
        {
            TeleportPlayer(destination.position, destination.rotation);
        }

        private void TeleportPlayer(Vector3 position, Quaternion rotation)
        {
            PlayerInputRouter inputRouter = playerRoot.GetComponent<PlayerInputRouter>();
            inputRouter?.ClearContinuousInput();

            bool controllerWasEnabled = characterController.enabled;
            characterController.enabled = false;
            playerRoot.transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = controllerWasEnabled;

            if (!playerRoot.activeSelf)
            {
                playerRoot.SetActive(true);
            }
        }

        private AsyncOperation BeginSceneUnload(Scene scene)
        {
            return unloadSceneOperation != null
                ? unloadSceneOperation(scene)
                : SceneManager.UnloadSceneAsync(scene);
        }

        private LevelDefinition FindLevel(string levelId)
        {
            string normalizedId = levelId?.Trim();

            for (int i = 0; i < availableLevels.Length; i++)
            {
                LevelDefinition definition = availableLevels[i];

                if (definition != null
                    && string.Equals(definition.LevelId, normalizedId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }

        private bool ValidateLevelCatalog(out string error)
        {
            if (initialLevel == null)
            {
                error = "Initial level is not assigned.";
                return false;
            }

            if (availableLevels == null || availableLevels.Length == 0)
            {
                error = "No available levels are assigned.";
                return false;
            }

            for (int i = 0; i < availableLevels.Length; i++)
            {
                LevelDefinition definition = availableLevels[i];

                if (definition == null)
                {
                    error = $"Entry {i} is empty.";
                    return false;
                }

                if (!definition.TryValidate(out error))
                {
                    return false;
                }

                for (int comparisonIndex = i + 1; comparisonIndex < availableLevels.Length; comparisonIndex++)
                {
                    LevelDefinition other = availableLevels[comparisonIndex];

                    if (other != null
                        && string.Equals(
                            definition.LevelId,
                            other.LevelId,
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Duplicate level ID: '{definition.LevelId}'.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryFindSpawnPoint(
            Scene scene,
            string spawnPointId,
            out SpawnPoint spawnPoint,
            out string error)
        {
            spawnPoint = null;
            int matchCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                SpawnPoint[] candidates = roots[rootIndex].GetComponentsInChildren<SpawnPoint>(true);

                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    SpawnPoint candidate = candidates[candidateIndex];

                    if (!string.Equals(
                        candidate.SpawnPointId,
                        spawnPointId,
                        System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    spawnPoint = candidate;
                    matchCount++;
                }
            }

            if (matchCount == 1)
            {
                error = string.Empty;
                return true;
            }

            error = matchCount == 0
                ? $"Spawn point '{spawnPointId}' was not found in scene '{scene.name}'."
                : $"Spawn point ID '{spawnPointId}' is duplicated in scene '{scene.name}'.";
            spawnPoint = null;
            return false;
        }

        private void SetPhase(
            FacilityTransitionPhase phase,
            string message,
            int requestId,
            string oldSceneName,
            string destinationName,
            string spawnId,
            float startedAt)
        {
            appState.SetTransitionPhase(phase, message);
            LogPhase(requestId, oldSceneName, destinationName, spawnId, phase, startedAt, "in-progress");
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void LogPhase(
            int requestId,
            string oldSceneName,
            string destinationName,
            string spawnId,
            FacilityTransitionPhase phase,
            float startedAt,
            string result)
        {
            Debug.Log(
                $"[Transition] request={requestId} old={oldSceneName} destination={destinationName} "
                + $"spawn={spawnId} phase={phase} elapsed={Time.realtimeSinceStartup - startedAt:0.000}s "
                + $"result={result}",
                this);
        }

        private void LogFailure(
            int requestId,
            string oldSceneName,
            string destinationName,
            string spawnId,
            float startedAt,
            string result)
        {
            Debug.LogError(
                $"[Transition] request={requestId} old={oldSceneName} destination={destinationName} "
                + $"spawn={spawnId} phase={FacilityTransitionPhase.Failed} "
                + $"elapsed={Time.realtimeSinceStartup - startedAt:0.000}s result={result}",
                this);
        }
    }
}

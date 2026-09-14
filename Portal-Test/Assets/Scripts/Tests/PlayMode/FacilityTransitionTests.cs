using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class FacilityTransitionTests
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
        private const float TimeoutSeconds = 10f;
        private static int capturedTeleportRequestCount;

        private Type appStateType;
        private Type serviceType;
        private Type levelDefinitionType;
        private Component appState;
        private Component service;
        private ScriptableObject invalidDefinition;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            appStateType = Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            serviceType = Type.GetType("FacilityViewer.Services.LevelTeleportService, Assembly-CSharp");
            levelDefinitionType = Type.GetType("FacilityViewer.Core.LevelDefinition, Assembly-CSharp");

            Assert.That(appStateType, Is.Not.Null);
            Assert.That(serviceType, Is.Not.Null);
            Assert.That(levelDefinitionType, Is.Not.Null);

            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
            appState = FindRuntimeComponent(appStateType);
            service = FindRuntimeComponent(serviceType);

            Assert.That(appState, Is.Not.Null);
            Assert.That(service, Is.Not.Null);
            yield return WaitForLevel("lobby");
        }

        [UnityTearDown]
        public IEnumerator CleanUpTransitionScene()
        {
            PlayerInput[] playerInputs = Resources.FindObjectsOfTypeAll<PlayerInput>()
                .Where(playerInput => playerInput.gameObject.scene.IsValid())
                .ToArray();

            foreach (PlayerInput playerInput in playerInputs)
            {
                playerInput.DeactivateInput();
                playerInput.actions?.Disable();
                playerInput.enabled = false;
            }

            yield return null;

            if (invalidDefinition != null)
            {
                UnityEngine.Object.Destroy(invalidDefinition);
                invalidDefinition = null;
                yield return null;
            }

            Scene cleanupScene = SceneManager.CreateScene($"FacilityTransitionCleanup-{Guid.NewGuid():N}");
            SceneManager.SetActiveScene(cleanupScene);
            Scene[] loadedScenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.handle != cleanupScene.handle)
                .ToArray();

            for (int index = 0; index < loadedScenes.Length; index++)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(loadedScenes[index]);

                if (unload != null)
                {
                    yield return unload;
                }
            }

            appState = null;
            service = null;

        }

        [UnityTest]
        public IEnumerator TransitionLoopRejectsOverlapAndRecoversFromMissingSpawn()
        {
            Assert.That(GetStateProperty<string>("CurrentLevelName"), Is.EqualTo("Lobby"));
            Assert.That(GetStateProperty<object>("TransitionPhase").ToString(), Is.EqualTo("Complete"));
            Assert.That(GameObject.Find("Player").activeSelf, Is.True);

            invalidDefinition = ScriptableObject.CreateInstance(levelDefinitionType);
            SetField(invalidDefinition, "levelId", "invalid-plant");
            SetField(invalidDefinition, "displayName", "Plant Room");
            SetField(invalidDefinition, "scenePath", "Assets/Scenes/PlantRoom.unity");
            SetField(invalidDefinition, "spawnPointId", "missing-spawn");

            LogAssert.Expect(
                LogType.Error,
                new Regex(@"\[Transition\].*destination=Plant Room.*spawn=missing-spawn.*phase=Failed.*Spawn point 'missing-spawn' was not found"));
            Assert.That(RequestTransition(invalidDefinition, levelDefinitionType), Is.True);
            yield return WaitForPhase("Failed");

            Assert.That(GetStateProperty<string>("CurrentLevelId"), Is.EqualTo("lobby"));
            Assert.That(SceneManager.GetSceneByPath("Assets/Scenes/PlantRoom.unity").isLoaded, Is.False);

            Assert.That(RequestTransition("operations-floor", typeof(string)), Is.True);
            LogAssert.Expect(
                LogType.Warning,
                "[Transition] Request rejected because another transition is active.");
            Assert.That(RequestTransition("plant-room", typeof(string)), Is.False);
            yield return WaitForLevel("operations-floor");

            Assert.That(RequestTransition("plant-room", typeof(string)), Is.True);
            yield return WaitForLevel("plant-room");

            Assert.That(RequestTransition("lobby", typeof(string)), Is.True);
            yield return WaitForLevel("lobby");

            Assert.That(
                Enumerable.Range(0, SceneManager.sceneCount).Select(index => SceneManager.GetSceneAt(index).name),
                Is.EquivalentTo(new[] { "Bootstrap", "Lobby" }));
            Assert.That(
                Resources.FindObjectsOfTypeAll<Camera>()
                    .Count(camera => camera.gameObject.scene.IsValid() && camera.gameObject.activeInHierarchy),
                Is.EqualTo(1));
            Assert.That(
                Resources.FindObjectsOfTypeAll<AudioListener>()
                    .Count(listener => listener.gameObject.scene.IsValid() && listener.gameObject.activeInHierarchy),
                Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UnloadStartFailureRestoresThePreviousFacilityAndPlayer()
        {
            GameObject player = GameObject.Find("Player");
            Behaviour playerController = player.GetComponent("PlayerController") as Behaviour;
            bool playerControllerWasEnabled = playerController.enabled;
            playerController.enabled = false;
            Scene lobbyScene = SceneManager.GetSceneByPath("Assets/Scenes/Lobby.unity");
            Vector3 previousPosition = player.transform.position;
            Quaternion previousRotation = player.transform.rotation;
            FieldInfo unloadOperationField = serviceType.GetField(
                "unloadSceneOperation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Func<Scene, AsyncOperation> originalUnloadOperation =
                (Func<Scene, AsyncOperation>)unloadOperationField.GetValue(service);

            try
            {
                unloadOperationField.SetValue(
                    service,
                    new Func<Scene, AsyncOperation>(scene =>
                        scene.handle == lobbyScene.handle
                            ? null
                            : SceneManager.UnloadSceneAsync(scene)));

                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"\[Transition\].*phase=Failed.*Unity could not start unloading the previous facility: Lobby\."));
                Assert.That(RequestTransition("operations-floor", typeof(string)), Is.True);
                yield return WaitForPhase("Failed");

                Assert.That(GetStateProperty<string>("CurrentLevelId"), Is.EqualTo("lobby"));
                Assert.That(lobbyScene.isLoaded, Is.True);
                Assert.That(
                    SceneManager.GetSceneByPath("Assets/Scenes/OperationsFloor.unity").isLoaded,
                    Is.False);
                Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(lobbyScene.handle));
                Assert.That(Vector3.Distance(player.transform.position, previousPosition), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(player.transform.rotation, previousRotation), Is.LessThan(0.001f));
                Assert.That((bool)serviceType.GetProperty("IsTransitionActive").GetValue(service), Is.False);
            }
            finally
            {
                if (service != null)
                {
                    unloadOperationField.SetValue(service, originalUnloadOperation);
                }

                if (playerController != null)
                {
                    playerController.enabled = playerControllerWasEnabled;
                }
            }
        }

        [UnityTest]
        public IEnumerator FailedDestinationCleanupFallsBackWhenTheConfiguredUnloaderReturnsNull()
        {
            invalidDefinition = ScriptableObject.CreateInstance(levelDefinitionType);
            SetField(invalidDefinition, "levelId", "invalid-plant-cleanup");
            SetField(invalidDefinition, "displayName", "Plant Room");
            SetField(invalidDefinition, "scenePath", "Assets/Scenes/PlantRoom.unity");
            SetField(invalidDefinition, "spawnPointId", "missing-cleanup-spawn");

            FieldInfo unloadOperationField = serviceType.GetField(
                "unloadSceneOperation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Func<Scene, AsyncOperation> originalUnloadOperation =
                (Func<Scene, AsyncOperation>)unloadOperationField.GetValue(service);

            try
            {
                unloadOperationField.SetValue(
                    service,
                    new Func<Scene, AsyncOperation>(scene =>
                        scene.path == "Assets/Scenes/PlantRoom.unity"
                            ? null
                            : originalUnloadOperation(scene)));

                LogAssert.Expect(
                    LogType.Warning,
                    new Regex(@"\[Transition\].*Cleanup unload did not start for failed destination Plant Room; retrying with Unity scene manager\."));
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"\[Transition\].*destination=Plant Room.*spawn=missing-cleanup-spawn.*phase=Failed.*Spawn point 'missing-cleanup-spawn' was not found"));
                Assert.That(RequestTransition(invalidDefinition, levelDefinitionType), Is.True);
                yield return WaitForPhase("Failed");

                Assert.That(GetStateProperty<string>("CurrentLevelId"), Is.EqualTo("lobby"));
                Assert.That(SceneManager.GetSceneByPath("Assets/Scenes/PlantRoom.unity").isLoaded, Is.False);
                Assert.That((bool)serviceType.GetProperty("IsTransitionActive").GetValue(service), Is.False);
            }
            finally
            {
                if (service != null)
                {
                    unloadOperationField.SetValue(service, originalUnloadOperation);
                }
            }
        }

        [UnityTest]
        public IEnumerator SameFacilityRequestUsesItsRequestedSpawn()
        {
            Scene lobbyScene = SceneManager.GetSceneByPath("Assets/Scenes/Lobby.unity");
            GameObject alternateSpawnObject = new("Alternate Spawn");
            SceneManager.MoveGameObjectToScene(alternateSpawnObject, lobbyScene);
            alternateSpawnObject.transform.SetPositionAndRotation(
                new Vector3(2f, 0f, 2f),
                Quaternion.Euler(0f, 135f, 0f));
            Type spawnPointType = Type.GetType("FacilityViewer.World.SpawnPoint, Assembly-CSharp");
            Component alternateSpawn = alternateSpawnObject.AddComponent(spawnPointType);
            SetField(alternateSpawn, "spawnPointId", "alternate");
            object lobbyDefinition = ((System.Collections.IEnumerable)serviceType
                    .GetProperty("OrderedLevelCatalog")
                    .GetValue(service))
                .Cast<object>()
                .Single(definition =>
                    (string)levelDefinitionType.GetProperty("LevelId").GetValue(definition)
                        == "lobby");

            try
            {
                MethodInfo requestWithSpawn = serviceType.GetMethod(
                    "RequestTransition",
                    new[] { levelDefinitionType, typeof(string) });
                Assert.That(
                    requestWithSpawn.Invoke(service, new[] { lobbyDefinition, "alternate" }),
                    Is.EqualTo(true));
                yield return WaitForLevel("lobby");

                GameObject player = GameObject.Find("Player");
                Assert.That(
                    Vector3.Distance(player.transform.position, alternateSpawnObject.transform.position),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(player.transform.rotation, alternateSpawnObject.transform.rotation),
                    Is.LessThan(0.001f));
                Assert.That(SceneManager.sceneCount, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(alternateSpawnObject);
            }
        }

        [UnityTest]
        public IEnumerator CatalogNavigationBuildsOneButtonPerLevelAndRoutesThroughTheService()
        {
            Type presenterType = Type.GetType("FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            Assert.That(presenterType, Is.Not.Null);

            Behaviour presenter = FindRuntimeComponent(presenterType) as Behaviour;
            Assert.That(presenter, Is.Not.Null);
            UIDocument document = presenter.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);

            VisualElement levelButtons = document.rootVisualElement.Q<VisualElement>("level-buttons");
            Assert.That(levelButtons, Is.Not.Null);
            Assert.That(levelButtons.Children().OfType<Button>().Select(button => button.name), Is.EqualTo(new[]
            {
                "level-lobby-button",
                "level-operations-floor-button",
                "level-plant-room-button"
            }));
            Button operationsButton = levelButtons.Q<Button>("level-operations-floor-button");
            Assert.That(operationsButton.text, Is.EqualTo("OPERATIONS FLOOR"));
            Assert.That(operationsButton.tooltip, Is.EqualTo("Move to Operations Floor"));

            presenter.enabled = false;
            yield return null;
            presenter.enabled = true;
            yield return null;

            levelButtons = document.rootVisualElement.Q<VisualElement>("level-buttons");
            Assert.That(levelButtons.Children().OfType<Button>().ToArray(), Has.Length.EqualTo(3));

            SendClick(levelButtons.Q<Button>("level-operations-floor-button"));
            yield return WaitForLevel("operations-floor");
        }

        [UnityTest]
        public IEnumerator StateFeedbackPresentsActiveFailureRecoveryAndCurrentLevelStates()
        {
            Type presenterType = Type.GetType("FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            Behaviour presenter = FindRuntimeComponent(presenterType) as Behaviour;
            UIDocument document = presenter.GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;
            VisualElement appShell = root.Q<VisualElement>("app-shell");
            VisualElement loadingOverlay = root.Q<VisualElement>("loading-overlay");
            Label phaseLabel = root.Q<Label>("transition-phase-label");
            Label statusLabel = root.Q<Label>("transition-status-label");
            Button lobbyButton = root.Q<Button>("level-lobby-button");
            Button operationsButton = root.Q<Button>("level-operations-floor-button");

            Assert.That(appShell.resolvedStyle.backgroundColor.a, Is.EqualTo(0f).Within(0.001f));
            Assert.That(lobbyButton.ClassListContains("is-selected"), Is.True);
            Assert.That(lobbyButton.enabledSelf, Is.False);
            Assert.That(operationsButton.enabledSelf, Is.True);

            foreach (string phaseName in new[] { "Validating", "Loading", "Teleporting", "Unloading" })
            {
                SetTransitionPhase(phaseName, $"Status for {phaseName}");
                yield return null;

                Assert.That(phaseLabel.text, Is.EqualTo($"PHASE  /  {GetDisplayPhaseName(phaseName)}"));
                Assert.That(statusLabel.text, Is.EqualTo($"Status for {phaseName}"));
                Assert.That(appShell.ClassListContains("app-shell--state-loading"), Is.True);
                Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.True);
                Assert.That(operationsButton.enabledSelf, Is.False);
            }

            appStateType.GetMethod("SetFailure").Invoke(appState, new object[] { "Destination unavailable" });
            yield return null;

            Assert.That(phaseLabel.text, Is.EqualTo("PHASE  /  ERROR"));
            Assert.That(statusLabel.text, Is.EqualTo("Destination unavailable"));
            Assert.That(appShell.ClassListContains("app-shell--state-error"), Is.True);
            Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.False);
            Assert.That(lobbyButton.ClassListContains("is-selected"), Is.True);
            Assert.That(operationsButton.enabledSelf, Is.True);

            SetTransitionPhase("Complete", "Recovery complete");
            yield return null;

            Assert.That(phaseLabel.text, Is.EqualTo("PHASE  /  COMPLETE"));
            Assert.That(appShell.ClassListContains("app-shell--state-success"), Is.True);
            Assert.That(appShell.ClassListContains("app-shell--state-error"), Is.False);
            Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.False);
        }

        [UnityTest]
        public IEnumerator PanelOwnershipFocusPickingAndCloseRoutesReturnInputToGameplay()
        {
            Type presenterType = Type.GetType("FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            Type coordinatorType = Type.GetType("FacilityViewer.Player.PlayerInputCoordinator, Assembly-CSharp");
            Assert.That(presenterType, Is.Not.Null);
            Assert.That(coordinatorType, Is.Not.Null);

            Behaviour presenter = FindRuntimeComponent(presenterType) as Behaviour;
            Component coordinator = FindRuntimeComponent(coordinatorType);
            Component router = coordinatorType.GetProperty("InputRouter").GetValue(coordinator) as Component;
            UIDocument document = presenter.GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;
            VisualElement appShell = root.Q<VisualElement>("app-shell");
            VisualElement statusCard = root.Q<VisualElement>("status-card");
            VisualElement facilityPanel = root.Q<VisualElement>("facility-panel");
            VisualElement loadingOverlay = root.Q<VisualElement>("loading-overlay");
            Button closeButton = root.Q<Button>("close-panel-button");
            VisualElement levelButtons = root.Q<VisualElement>("level-buttons");
            Type routerType = router.GetType();

            routerType.GetField("desktopMoveInput", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(router, new Vector2(1f, 1f));
            coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });
            yield return null;

            Assert.That((bool)coordinatorType.GetProperty("IsUiInputActive").GetValue(coordinator), Is.True);
            Assert.That(facilityPanel.ClassListContains("facility-panel--open"), Is.True);
            Assert.That(root.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(appShell.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(statusCard.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(facilityPanel.pickingMode, Is.EqualTo(PickingMode.Position));
            Assert.That(loadingOverlay.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(presenterType.GetProperty("FocusedControlName").GetValue(presenter), Is.EqualTo("level-operations-floor-button"));

            SendClick(closeButton);
            yield return null;

            Assert.That((bool)coordinatorType.GetProperty("IsUiInputActive").GetValue(coordinator), Is.False);
            Assert.That(routerType.GetProperty("InputOwner").GetValue(router).ToString(), Is.EqualTo("Gameplay"));
            Assert.That((Vector2)routerType.GetProperty("MoveInput").GetValue(router), Is.EqualTo(Vector2.zero));
            Assert.That(facilityPanel.ClassListContains("facility-panel--open"), Is.False);
            Assert.That(facilityPanel.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(presenterType.GetProperty("FocusedControlName").GetValue(presenter), Is.EqualTo(string.Empty));

            Type modeOverrideType = Type.GetType("FacilityViewer.Player.PlayerInputModeOverride, Assembly-CSharp");
            coordinatorType.GetMethod("SetInputModeOverride").Invoke(
                coordinator,
                new[] { Enum.Parse(modeOverrideType, "Mobile") });
            routerType.GetMethod("RequestMobileTogglePanel").Invoke(router, null);
            yield return null;
            Assert.That((bool)coordinatorType.GetProperty("IsUiInputActive").GetValue(coordinator), Is.True);
            routerType.GetMethod("RequestMobileTogglePanel").Invoke(router, null);
            yield return null;
            Assert.That((bool)coordinatorType.GetProperty("IsUiInputActive").GetValue(coordinator), Is.False);

            presenter.enabled = false;
            yield return null;
            presenter.enabled = true;
            yield return null;
            Assert.That(levelButtons.Children().OfType<Button>().ToArray(), Has.Length.EqualTo(3));

            coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });
            yield return null;
            root.SendEvent(KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None));
            yield return null;

            Assert.That((bool)coordinatorType.GetProperty("IsUiInputActive").GetValue(coordinator), Is.False);
            Assert.That(facilityPanel.ClassListContains("facility-panel--open"), Is.False);
        }

        [UnityTest]
        public IEnumerator TeleportPadsRejectInvalidRequestsAndTraverseEveryFacilityThroughSharedInput()
        {
            Type padType = Type.GetType("FacilityViewer.World.TeleportPad, Assembly-CSharp");
            Type controllerType = Type.GetType(
                "FacilityViewer.Services.TeleportPadTransitionController, Assembly-CSharp");
            Type interactorType = Type.GetType(
                "FacilityViewer.Player.PlayerTeleportPadInteractor, Assembly-CSharp");
            Type coordinatorType = Type.GetType(
                "FacilityViewer.Player.PlayerInputCoordinator, Assembly-CSharp");
            Type modeOverrideType = Type.GetType(
                "FacilityViewer.Player.PlayerInputModeOverride, Assembly-CSharp");
            Type promptPresenterType = Type.GetType(
                "FacilityViewer.UI.TeleportPadPromptPresenter, Assembly-CSharp");

            Assert.That(padType, Is.Not.Null);
            Assert.That(controllerType, Is.Not.Null);
            Assert.That(interactorType, Is.Not.Null);
            Assert.That(promptPresenterType, Is.Not.Null);

            Component transitionController = FindRuntimeComponent(controllerType);
            GameObject invalidPadObject = new("Controlled Invalid Teleport Pad");
            BoxCollider invalidTrigger = invalidPadObject.AddComponent<BoxCollider>();
            invalidTrigger.isTrigger = true;
            Component invalidPad = invalidPadObject.AddComponent(padType);
            Component invalidSpawnPad = null;

            try
            {
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"\[Teleport Pad\] Request rejected: Destination level is not assigned\."));
            Assert.That(
                controllerType.GetMethod("RequestTeleport").Invoke(
                    transitionController,
                    new object[] { invalidPad }),
                Is.EqualTo(false));
            Assert.That(GetStateProperty<string>("CurrentLevelId"), Is.EqualTo("lobby"));
            UnityEngine.Object.DestroyImmediate(invalidPadObject);
            invalidPadObject = null;

            invalidSpawnPad = FindLoadedPadTo(padType, "operations-floor");
            SetField(invalidSpawnPad, "destinationSpawnId", "missing-pad-spawn");
            LogAssert.Expect(
                LogType.Error,
                new Regex(@"\[Transition\].*destination=Operations Floor.*spawn=missing-pad-spawn.*phase=Failed.*Spawn point 'missing-pad-spawn' was not found"));
            Assert.That(
                controllerType.GetMethod("RequestTeleport").Invoke(
                    transitionController,
                    new object[] { invalidSpawnPad }),
                Is.EqualTo(true));
            yield return WaitForPhase("Failed");
            Assert.That(GetStateProperty<string>("CurrentLevelId"), Is.EqualTo("lobby"));
            Assert.That(
                SceneManager.GetSceneByPath("Assets/Scenes/OperationsFloor.unity").isLoaded,
                Is.False);
            SetField(invalidSpawnPad, "destinationSpawnId", "entrance");

            yield return TravelByTeleportPad(
                "operations-floor",
                "Operations Floor",
                padType,
                controllerType,
                interactorType,
                coordinatorType,
                promptPresenterType,
                modeOverrideType);
            yield return TravelByTeleportPad(
                "plant-room",
                "Plant Room",
                padType,
                controllerType,
                interactorType,
                coordinatorType,
                promptPresenterType,
                modeOverrideType);
            yield return TravelByTeleportPad(
                "lobby",
                "Lobby",
                padType,
                controllerType,
                interactorType,
                coordinatorType,
                promptPresenterType,
                modeOverrideType);

            Assert.That(
                controllerType.GetProperty("RegisteredPadCount").GetValue(transitionController),
                Is.EqualTo(2));
            Assert.That(
                Enumerable.Range(0, SceneManager.sceneCount)
                    .Select(index => SceneManager.GetSceneAt(index).name),
                Is.EquivalentTo(new[] { "Bootstrap", "Lobby" }));
            }
            finally
            {
                if (invalidSpawnPad != null)
                {
                    SetField(invalidSpawnPad, "destinationSpawnId", "entrance");
                }

                if (invalidPadObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(invalidPadObject);
                }
            }
        }

        private IEnumerator TravelByTeleportPad(
            string destinationLevelId,
            string destinationDisplayName,
            Type padType,
            Type controllerType,
            Type interactorType,
            Type coordinatorType,
            Type promptPresenterType,
            Type modeOverrideType)
        {
            Component transitionController = FindRuntimeComponent(controllerType);
            Component interactor = FindRuntimeComponent(interactorType);
            Component coordinator = FindRuntimeComponent(coordinatorType);
            Component promptPresenter = FindRuntimeComponent(promptPresenterType);
            Component pad = FindLoadedPadTo(padType, destinationLevelId);
            GameObject player = GameObject.Find("Player");
            Component inputRouter = player.GetComponent("PlayerInputRouter");
            Component[] facilityPads = FindLoadedPads(padType);

            Assert.That(facilityPads, Has.Length.EqualTo(2));
            Assert.That(facilityPads, Does.Contain(pad));
            Assert.That(
                controllerType.GetProperty("RegisteredPadCount").GetValue(transitionController),
                Is.EqualTo(facilityPads.Length));

            coordinatorType.GetMethod("SetInputModeOverride").Invoke(
                coordinator,
                new[]
                {
                    Enum.Parse(modeOverrideType, "Mobile")
                });
            yield return null;

            yield return MovePlayerIntoOnlyPadTrigger(
                player,
                pad,
                facilityPads,
                interactor,
                interactorType);
            Physics.SyncTransforms();
            interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);
            yield return null;

            Assert.That(interactorType.GetProperty("ActivePad").GetValue(interactor), Is.EqualTo(pad));
            Assert.That(promptPresenterType.GetProperty("IsPromptVisible").GetValue(promptPresenter), Is.True);
            Assert.That(
                promptPresenterType.GetProperty("PresentedDestination").GetValue(promptPresenter),
                Does.Contain(destinationDisplayName));

            UIDocument document = promptPresenter.GetComponent<UIDocument>();
            Label actionLabel = document.rootVisualElement.Q<Label>("teleport-prompt-action-label");
            Assert.That(
                actionLabel.text,
                Is.EqualTo("TAP USE"));

            coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });
            Assert.That(promptPresenterType.GetProperty("IsPromptVisible").GetValue(promptPresenter), Is.False);
            coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { false });
            Assert.That(promptPresenterType.GetProperty("IsPromptVisible").GetValue(promptPresenter), Is.True);

            UIDocument mobileControlsDocument = player.transform.Find("MobileControls")
                .GetComponent<UIDocument>();
            Button mobileUseButton = mobileControlsDocument.rootVisualElement.Q<Button>("interact-button");

            Assert.That(mobileUseButton, Is.Not.Null);
            Assert.That(mobileUseButton.text, Is.EqualTo("USE"));
            Assert.That(mobileUseButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            int buttonClickCount = 0;
            int routedInteractCount = 0;
            Action countButtonClick = () => buttonClickCount++;
            Action countRoutedInteract = () => routedInteractCount++;
            EventInfo interactRequested = inputRouter.GetType().GetEvent("InteractRequested");
            EventInfo teleportRequested = interactorType.GetEvent("TeleportRequested");
            MethodInfo captureTeleportRequest = typeof(FacilityTransitionTests)
                .GetMethod(nameof(CaptureTeleportRequest), BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(padType);
            Delegate countTeleportRequest = Delegate.CreateDelegate(
                teleportRequested.EventHandlerType,
                captureTeleportRequest);
            capturedTeleportRequestCount = 0;
            mobileUseButton.clicked += countButtonClick;
            interactRequested.AddEventHandler(inputRouter, countRoutedInteract);
            teleportRequested.AddEventHandler(interactor, countTeleportRequest);
            MobileControlsTests.ClickButton(mobileUseButton);
            mobileUseButton.clicked -= countButtonClick;
            interactRequested.RemoveEventHandler(inputRouter, countRoutedInteract);
            teleportRequested.RemoveEventHandler(interactor, countTeleportRequest);

            Assert.That(buttonClickCount, Is.EqualTo(1), "The live mobile USE button must invoke its click handlers.");
            Assert.That(routedInteractCount, Is.EqualTo(1), "The mobile USE button must route one interact request.");
            Assert.That(
                capturedTeleportRequestCount,
                Is.EqualTo(1),
                "The interactor must route the active pad after the mobile USE request.");

            Assert.That(
                GetStateProperty<bool>("IsTransitioning"),
                Is.True,
                "The transition must be observable before the mobile pointer event returns.");
            Assert.That(
                facilityPads.All(facilityPad =>
                    (bool)padType.GetProperty("IsTransitionLocked").GetValue(facilityPad)),
                Is.True,
                "Every registered pad in the source facility must lock before loading begins.");
            Assert.That(promptPresenterType.GetProperty("IsPromptVisible").GetValue(promptPresenter), Is.False);

            LogAssert.Expect(
                LogType.Warning,
                "[Teleport Pad] Request rejected: the pad is unavailable");
            Assert.That(
                controllerType.GetMethod("RequestTeleport").Invoke(
                    transitionController,
                    new object[] { pad }),
                Is.EqualTo(false));

            yield return null;

            yield return WaitForLevel(destinationLevelId);
            Assert.That(
                controllerType.GetProperty("RegisteredPadCount").GetValue(transitionController),
                Is.EqualTo(2));
        }

        private static Component[] FindLoadedPads(Type padType)
        {
            return Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.IsValid() && scene.isLoaded)
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(padType.IsInstanceOfType)
                .Cast<Component>()
                .ToArray();
        }

        private static IEnumerator MovePlayerIntoOnlyPadTrigger(
            GameObject player,
            Component targetPad,
            Component[] facilityPads,
            Component interactor,
            Type interactorType)
        {
            BoxCollider targetTrigger = targetPad.GetComponent<BoxCollider>();

            Assert.That(targetTrigger, Is.Not.Null);

            foreach (Component siblingPad in facilityPads.Where(pad => pad != targetPad))
            {
                BoxCollider siblingTrigger = siblingPad.GetComponent<BoxCollider>();

                Assert.That(siblingTrigger, Is.Not.Null);
                Assert.That(
                    targetTrigger.bounds.Intersects(siblingTrigger.bounds),
                    Is.False,
                    "The test must enter only the requested pad trigger, not rely on tie-breaking between overlapping pads.");
            }

            Component sibling = facilityPads.Single(pad => pad != targetPad);
            Vector3 towardSibling = sibling.transform.position - targetPad.transform.position;
            towardSibling.y = 0f;
            towardSibling.Normalize();

            CharacterController characterController = player.GetComponent<CharacterController>();
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            float startingDistance = targetTrigger.bounds.extents.x
                + characterController.radius
                + 0.75f;
            Vector3 startPosition = targetPad.transform.position + towardSibling * startingDistance;
            startPosition.y = targetPad.transform.position.y;
            player.transform.SetPositionAndRotation(
                startPosition,
                Quaternion.LookRotation(-towardSibling));
            characterController.enabled = wasEnabled;

            Physics.SyncTransforms();
            interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);
            Assert.That(
                interactorType.GetProperty("ActivePad").GetValue(interactor),
                Is.Null,
                "The traversal must begin outside both pad triggers.");

            const int maximumMovementSteps = 24;
            const float centerTolerance = 0.05f;

            for (int step = 0; step < maximumMovementSteps; step++)
            {
                Vector3 remaining = targetPad.transform.position - player.transform.position;
                remaining.y = 0f;
                characterController.Move(Vector3.ClampMagnitude(remaining, 0.25f));
                yield return null;
                interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);

                Vector3 centeredOffset = targetPad.transform.position - player.transform.position;
                centeredOffset.y = 0f;

                if (centeredOffset.sqrMagnitude <= centerTolerance * centerTolerance)
                {
                    Assert.That(
                        interactorType.GetProperty("ActivePad").GetValue(interactor),
                        Is.EqualTo(targetPad),
                        "The target pad must remain active at its center before interaction.");
                    yield break;
                }
            }

            Assert.Fail("CharacterController movement did not reach the requested teleport pad center.");
        }

        private static Component FindLoadedPadTo(Type padType, string destinationLevelId)
        {
            return Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.IsValid() && scene.isLoaded)
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(component =>
                    padType.IsInstanceOfType(component))
                .Single(component =>
                {
                    object destination = padType.GetProperty("DestinationLevel").GetValue(component);
                    return destination != null
                        && (string)destination.GetType().GetProperty("LevelId").GetValue(destination)
                            == destinationLevelId;
                });
        }

        private IEnumerator WaitForLevel(string levelId)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetStateProperty<string>("CurrentLevelId") == levelId
                    && !GetStateProperty<bool>("IsTransitioning"))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for level '{levelId}'. Current phase: {GetStateProperty<object>("TransitionPhase")}");
        }

        private IEnumerator WaitForPhase(string phaseName)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetStateProperty<object>("TransitionPhase").ToString() == phaseName)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for phase '{phaseName}'.");
        }

        private bool RequestTransition(object destination, Type parameterType)
        {
            MethodInfo method = serviceType.GetMethod("RequestTransition", new[] { parameterType });
            return (bool)method.Invoke(service, new[] { destination });
        }

        private void SetTransitionPhase(string phaseName, string message)
        {
            Type phaseType = GetStateProperty<object>("TransitionPhase").GetType();
            object phase = Enum.Parse(phaseType, phaseName);
            appStateType.GetMethod("SetTransitionPhase").Invoke(appState, new[] { phase, message });
        }

        private static string GetDisplayPhaseName(string phaseName)
        {
            return phaseName == "Unloading" ? "FINALIZING" : phaseName.ToUpperInvariant();
        }

        private T GetStateProperty<T>(string propertyName)
        {
            return (T)appStateType.GetProperty(propertyName).GetValue(appState);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static Component FindRuntimeComponent(Type type)
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .First(component => type.IsInstanceOfType(component) && component.gameObject.scene.IsValid());
        }

        private static void CaptureTeleportRequest<T>(T _)
        {
            capturedTeleportRequestCount++;
        }

        private static void SendClick(VisualElement target)
        {
            ClickEvent clickEvent = ClickEvent.GetPooled();
            clickEvent.target = target;
            target.SendEvent(clickEvent);
        }

    }
}

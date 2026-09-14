using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FacilityViewer.Tests
{
    public sealed class FacilityTransitionAssetTests
    {
        private const string BootstrapShellPath = "Assets/UI/Documents/BootstrapShell.uxml";
        private const string FacilitySectionHeadingTemplatePath =
            "Assets/UI/Templates/FacilitySectionHeading.uxml";
        private const string FacilityPanelBaseStylePath = "Assets/UI/Styles/FacilityPanelBase.uss";
        private const string FacilityPanelComponentsStylePath = "Assets/UI/Styles/FacilityPanelComponents.uss";
        private const string FacilityPanelResponsiveStylePath = "Assets/UI/Styles/FacilityPanelResponsive.uss";
        private const string MobileControlsStylePath = "Assets/UI/Styles/MobileControls.uss";
        private const string FacilityControlPanelPresenterPath =
            "Assets/Scripts/UI/FacilityControlPanelPresenter.cs";
        private const string FacilityControlPanelViewPath = "Assets/Scripts/UI/FacilityControlPanelView.cs";
        private const string FacilityControlPanelLayoutPath = "Assets/Scripts/UI/FacilityControlPanelLayout.cs";
        private const string FacilityControlPanelContractsPath = "Assets/Scripts/UI/FacilityControlPanelContracts.cs";
        private const string RuntimePanelSettingsPath = "Assets/Settings/RuntimePanelSettings.asset";

        private static readonly System.Collections.Generic.List<object> ThemeRequests = new();
        private static readonly System.Collections.Generic.List<object> LightRequests = new();

        private static readonly string[] ProductionScenes =
        {
            "Assets/Scenes/Bootstrap.unity",
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/OperationsFloor.unity",
            "Assets/Scenes/PlantRoom.unity"
        };

        private static readonly string[,] Definitions =
        {
            { "Assets/Data/LevelDefinitions/Lobby.asset", "lobby", "Lobby", "Assets/Scenes/Lobby.unity" },
            { "Assets/Data/LevelDefinitions/OperationsFloor.asset", "operations-floor", "Operations Floor", "Assets/Scenes/OperationsFloor.unity" },
            { "Assets/Data/LevelDefinitions/PlantRoom.asset", "plant-room", "Plant Room", "Assets/Scenes/PlantRoom.unity" }
        };

        [Test]
        public void LevelDefinitionsContainStableRuntimeIdentifiers()
        {
            for (int index = 0; index < Definitions.GetLength(0); index++)
            {
                ScriptableObject definition = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Definitions[index, 0]);
                SerializedObject serializedDefinition = new(definition);

                Assert.That(definition, Is.Not.Null, Definitions[index, 0]);
                Assert.That(serializedDefinition.FindProperty("levelId").stringValue, Is.EqualTo(Definitions[index, 1]));
                Assert.That(serializedDefinition.FindProperty("displayName").stringValue, Is.EqualTo(Definitions[index, 2]));
                Assert.That(serializedDefinition.FindProperty("scenePath").stringValue, Is.EqualTo(Definitions[index, 3]));
                Assert.That(serializedDefinition.FindProperty("spawnPointId").stringValue, Is.EqualTo("entrance"));
            }
        }

        [Test]
        public void FacilityScenesOwnOnlyLocalEnvironmentAndOneEntranceSpawn()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                for (int index = 1; index < ProductionScenes.Length; index++)
                {
                    Scene scene = EditorSceneManager.OpenScene(ProductionScenes[index], OpenSceneMode.Single);
                    GameObject[] roots = scene.GetRootGameObjects();
                    MonoBehaviour[] behaviours = roots
                        .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                        .ToArray();
                    MonoBehaviour[] spawnPoints = behaviours
                        .Where(component => component.GetType().FullName == "FacilityViewer.World.SpawnPoint")
                        .ToArray();

                    Assert.That(roots, Has.Length.EqualTo(1));
                    Assert.That(roots[0].transform.Find("Environment"), Is.Not.Null);
                    Assert.That(roots[0].transform.Find("Spawn Points"), Is.Not.Null);
                    Assert.That(roots[0].transform.Find("Lighting"), Is.Not.Null);
                    Assert.That(spawnPoints, Has.Length.EqualTo(1));
                    Assert.That(
                        new SerializedObject(spawnPoints[0]).FindProperty("spawnPointId").stringValue,
                        Is.EqualTo("entrance"));
                    Assert.That(roots[0].GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
                    Assert.That(roots[0].GetComponentsInChildren<Collider>(true), Is.Not.Empty);
                    Assert.That(roots[0].GetComponentsInChildren<Light>(true), Has.Length.EqualTo(1));
                    Assert.That(roots[0].GetComponentsInChildren<Camera>(true), Is.Empty);
                    Assert.That(roots[0].GetComponentsInChildren<AudioListener>(true), Is.Empty);
                    Assert.That(roots[0].GetComponentsInChildren<UIDocument>(true), Is.Empty);
                    Assert.That(
                        behaviours.Any(component => component.GetType().FullName == "FacilityViewer.Player.PlayerController"),
                        Is.False);
                }
            }
            finally
            {
                if (previousSetup.Any(scene => scene.isLoaded))
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void ProductionScenesAreEnabledInOrderAcrossBuildProfiles()
        {
            AssertBuildProfileScenes("Assets/Settings/BuildProfiles/Windows Development.asset");
            AssertBuildProfileScenes("Assets/Settings/BuildProfiles/Android Development.asset");
        }

        [Test]
        public void ProductionScenesAreEnabledInOrderInGlobalBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            Assert.That(scenes, Has.Length.EqualTo(ProductionScenes.Length));

            for (int index = 0; index < ProductionScenes.Length; index++)
            {
                Assert.That(scenes[index].enabled, Is.True, ProductionScenes[index]);
                Assert.That(scenes[index].path, Is.EqualTo(ProductionScenes[index]));
            }
        }

        [Test]
        public void BootstrapUiAndControlPanelPresenterAreFullyConnected()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                Scene scene = EditorSceneManager.OpenScene(ProductionScenes[0], OpenSceneMode.Single);
                GameObject root = scene.GetRootGameObjects().Single();
                Transform applicationUi = root.transform.Find("Application UI");
                Transform player = root.transform.Find("Player");
                UIDocument document = applicationUi.GetComponent<UIDocument>();
                Component presenter = applicationUi.GetComponent("FacilityControlPanelPresenter");
                Component service = root.GetComponent("LevelTeleportService");
                Component state = root.GetComponent("AppState");
                Component coordinator = player.GetComponent("PlayerInputCoordinator");
                TemplateContainer visualTree = document.visualTreeAsset.CloneTree();

                Assert.That(presenter, Is.Not.Null);
                Assert.That(applicationUi.GetComponent("FacilityTransitionPresenter"), Is.Null);
                Assert.That(
                    presenter.GetType().FullName,
                    Is.EqualTo("FacilityViewer.UI.FacilityControlPanelPresenter"));
                VisualElement levelButtons = visualTree.Q<VisualElement>("level-buttons");
                Assert.That(levelButtons, Is.Not.Null);
                Assert.That(levelButtons.childCount, Is.EqualTo(0));
                Assert.That(visualTree.Q<Button>("level-lobby-button"), Is.Null);
                Assert.That(visualTree.Q<Button>("level-operations-floor-button"), Is.Null);
                Assert.That(visualTree.Q<Button>("level-plant-room-button"), Is.Null);
                Assert.That(visualTree.Q<VisualElement>("facility-panel-header"), Is.Not.Null);
                VisualElement appSafeArea = visualTree.Q<VisualElement>("app-safe-area");
                Assert.That(appSafeArea, Is.Not.Null);
                Assert.That(appSafeArea.ClassListContains("app-safe-area"), Is.True);
                Assert.That(visualTree.Q<Label>("application-name-label"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("close-panel-button"), Is.Not.Null);
                Assert.That(visualTree.Q<VisualElement>("level-navigation-section"), Is.Not.Null);
                Assert.That(visualTree.Q<VisualElement>("theme-selection-section"), Is.Not.Null);
                Assert.That(visualTree.Q<VisualElement>("lighting-controls-section"), Is.Not.Null);
                Assert.That(visualTree.Q<VisualElement>("system-status-section"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("theme-standard-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("theme-maintenance-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("theme-emergency-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("lighting-ambient-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("lighting-operations-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("lighting-emergency-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Label>("connection-status-label"), Is.Not.Null);
                Assert.That(visualTree.Q<Label>("transition-phase-label"), Is.Not.Null);
                Assert.That(visualTree.Q<VisualElement>("loading-overlay"), Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetDependencies(BootstrapShellPath),
                    Does.Contain(FacilitySectionHeadingTemplatePath));
                Assert.That(
                    AssetDatabase.GetDependencies(BootstrapShellPath),
                    Does.Contain(FacilityPanelBaseStylePath));
                Assert.That(
                    AssetDatabase.GetDependencies(BootstrapShellPath),
                    Does.Contain(FacilityPanelComponentsStylePath));
                Assert.That(
                    AssetDatabase.GetDependencies(BootstrapShellPath),
                    Does.Contain(FacilityPanelResponsiveStylePath));

                SerializedObject serializedPresenter = new(presenter);
                Assert.That(serializedPresenter.FindProperty("uiDocument").objectReferenceValue, Is.EqualTo(document));
                Assert.That(serializedPresenter.FindProperty("appState").objectReferenceValue, Is.EqualTo(state));
                Assert.That(serializedPresenter.FindProperty("levelTeleportService").objectReferenceValue, Is.EqualTo(service));
                Assert.That(serializedPresenter.FindProperty("inputCoordinator").objectReferenceValue, Is.EqualTo(coordinator));

                SerializedObject serializedService = new(service);
                Assert.That(serializedService.FindProperty("initialLevel").objectReferenceValue.name, Is.EqualTo("Lobby"));
                Assert.That(serializedService.FindProperty("availableLevels").arraySize, Is.EqualTo(3));

                object catalog = service.GetType().GetProperty("OrderedLevelCatalog").GetValue(service);
                System.Collections.IList readOnlyCatalog = catalog as System.Collections.IList;
                Object[] catalogEntries = ((System.Collections.IEnumerable)catalog).Cast<Object>().ToArray();

                Assert.That(readOnlyCatalog, Is.Not.Null);
                Assert.That(readOnlyCatalog.IsReadOnly, Is.True);
                Assert.That(
                    catalogEntries.Select(entry => new SerializedObject(entry).FindProperty("levelId").stringValue),
                    Is.EqualTo(new[] { "lobby", "operations-floor", "plant-room" }));

                System.Type layoutType = System.Type.GetType(
                    "FacilityViewer.UI.FacilityControlPanelLayout, Assembly-CSharp");
                Assert.That(layoutType, Is.Not.Null);
                MethodInfo buttonNameBuilder = layoutType.GetMethod(
                    "BuildLevelButtonName",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.That(buttonNameBuilder, Is.Not.Null);
                Assert.That(buttonNameBuilder.Invoke(null, new object[] { "lobby" }), Is.EqualTo("level-lobby-button"));
                Assert.That(
                    buttonNameBuilder.Invoke(null, new object[] { "operations-floor" }),
                    Is.EqualTo("level-operations-floor-button"));
                Assert.That(buttonNameBuilder.Invoke(null, new object[] { "plant-room" }), Is.EqualTo("level-plant-room-button"));
                Assert.That(
                    File.ReadAllText(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour((MonoBehaviour)presenter))),
                    Does.Not.Contain("\"lobby\"")
                        .And.Not.Contain("\"operations-floor\"")
                        .And.Not.Contain("\"plant-room\""));
            }
            finally
            {
                if (previousSetup.Any(scene => scene.isLoaded))
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void FacilityPanelStylesExposeResponsiveAndAccessibleControlHooks()
        {
            AssertStyleSource(FacilityPanelBaseStylePath, ".app-safe-area");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".facility-panel--open");
            AssertStyleSource(FacilityPanelComponentsStylePath, "min-height: 72px");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".state-button.is-selected");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".level-button.is-selected");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".level-button:focus");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".app-shell--state-idle");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".app-shell--state-loading");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".app-shell--state-success");
            AssertStyleSource(FacilityPanelComponentsStylePath, ".app-shell--state-error");
            AssertStyleSource(FacilityPanelBaseStylePath, "background-color: rgba(0, 0, 0, 0)");
            AssertStyleSource(FacilityPanelResponsiveStylePath, ".app-shell--wide");
            AssertStyleSource(FacilityPanelResponsiveStylePath, ".app-shell--compact");
            AssertStyleSource(FacilityPanelResponsiveStylePath, ".state-button--last");
            AssertStyleSource(FacilityPanelResponsiveStylePath, "bottom: 136px");
        }

        [Test]
        public void FacilityPanelUsesExplicitLastControlHooksInsteadOfUnsupportedPseudoSelectors()
        {
            VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BootstrapShellPath);
            TemplateContainer visualTree = visualTreeAsset.CloneTree();

            Assert.That(visualTree.Q<Button>("theme-emergency-button").ClassListContains("state-button--last"), Is.True);
            Assert.That(visualTree.Q<Button>("lighting-emergency-button").ClassListContains("state-button--last"), Is.True);
            Assert.That(File.ReadAllText(FacilityPanelResponsiveStylePath), Does.Not.Contain(":last-child"));
        }

        [Test]
        public void FacilityPanelRuntimeResponsibilitiesAreSeparatedWithoutWorldCommands()
        {
            System.Type presenterType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            System.Type viewType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelView, Assembly-CSharp");
            System.Type layoutType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelLayout, Assembly-CSharp");
            System.Type contractsType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelIds, Assembly-CSharp");

            Assert.That(presenterType, Is.Not.Null);
            Assert.That(viewType, Is.Not.Null);
            Assert.That(layoutType, Is.Not.Null);
            Assert.That(contractsType, Is.Not.Null);
            Assert.That(presenterType.IsSubclassOf(typeof(MonoBehaviour)), Is.True);
            Assert.That(viewType.IsSubclassOf(typeof(MonoBehaviour)), Is.False);
            Assert.That(layoutType.IsAbstract && layoutType.IsSealed, Is.True);
            Assert.That(File.ReadAllText(FacilityControlPanelPresenterPath), Does.Not.Contain("SceneManager"));
            Assert.That(File.ReadAllText(FacilityControlPanelViewPath), Does.Not.Contain("GetComponent<Light>"));
            Assert.That(File.ReadAllText(FacilityControlPanelViewPath), Does.Not.Contain("Renderer."));
        }

        [Test]
        public void FacilityControlPanelPresenterScriptKeepsItsStableAssetIdentity()
        {
            string presenterGuid = AssetDatabase.AssetPathToGUID(FacilityControlPanelPresenterPath);
            MonoScript presenterScript = AssetDatabase.LoadAssetAtPath<MonoScript>(FacilityControlPanelPresenterPath);

            Assert.That(presenterGuid, Is.Not.Empty);
            Assert.That(AssetDatabase.GUIDToAssetPath(presenterGuid), Is.EqualTo(FacilityControlPanelPresenterPath));
            Assert.That(presenterScript.GetClass().FullName, Is.EqualTo("FacilityViewer.UI.FacilityControlPanelPresenter"));
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(FacilityControlPanelViewPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(FacilityControlPanelLayoutPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(FacilityControlPanelContractsPath), Is.Not.Null);
        }

        [Test]
        public void ControlPanelAndMobileControlsAuthorTouchTargetsAboveTheScaledMinimum()
        {
            AssertStyleSource(FacilityPanelComponentsStylePath, "min-height: 72px");
            AssertStyleSource(MobileControlsStylePath, "min-height: 72px");
        }

        [Test]
        public void ControlPanelViewPresentsPhaseStatusAndLoadingStatesFromAppState()
        {
            System.Type presenterType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            System.Type viewType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelView, Assembly-CSharp");
            System.Type appStateType = System.Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            System.Type phaseType = System.Type.GetType("FacilityViewer.Core.FacilityTransitionPhase, Assembly-CSharp");

            Assert.That(presenterType, Is.Not.Null);
            Assert.That(viewType, Is.Not.Null);
            Assert.That(appStateType, Is.Not.Null);
            Assert.That(phaseType, Is.Not.Null);
            VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BootstrapShellPath);
            TemplateContainer visualTree = visualTreeAsset.CloneTree();
            MethodInfo tryCreate = viewType.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.Public);
            object[] tryCreateArguments = { visualTree, null };
            Assert.That(tryCreate, Is.Not.Null);
            Assert.That((bool)tryCreate.Invoke(null, tryCreateArguments), Is.True);

            GameObject stateObject = new("Control Panel Presentation State");

            try
            {
                Component appState = stateObject.AddComponent(appStateType);
                MethodInfo present = viewType.GetMethod("Present", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo initialize = appStateType.GetMethod("Initialize");
                MethodInfo setPhase = appStateType.GetMethod("SetTransitionPhase");
                MethodInfo setFailure = appStateType.GetMethod("SetFailure");
                VisualElement appShell = visualTree.Q<VisualElement>("app-shell");
                VisualElement loadingOverlay = visualTree.Q<VisualElement>("loading-overlay");
                Label phaseLabel = visualTree.Q<Label>("transition-phase-label");
                Label statusLabel = visualTree.Q<Label>("transition-status-label");

                initialize.Invoke(appState, new object[] { "Application services ready" });
                present.Invoke(tryCreateArguments[1], new object[] { appState });
                Assert.That(phaseLabel.text, Is.EqualTo("PHASE  /  READY"));
                Assert.That(appShell.ClassListContains("app-shell--state-idle"), Is.True);
                Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.False);

                setPhase.Invoke(appState, new[] { Enum.Parse(phaseType, "Loading"), "Loading Operations Floor" });
                present.Invoke(tryCreateArguments[1], new object[] { appState });
                Assert.That(phaseLabel.text, Is.EqualTo("PHASE  /  LOADING"));
                Assert.That(statusLabel.text, Is.EqualTo("Loading Operations Floor"));
                Assert.That(appShell.ClassListContains("app-shell--state-loading"), Is.True);
                Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.True);

                setFailure.Invoke(appState, new object[] { "Destination unavailable" });
                present.Invoke(tryCreateArguments[1], new object[] { appState });
                Assert.That(phaseLabel.text, Is.EqualTo("PHASE  /  ERROR"));
                Assert.That(appShell.ClassListContains("app-shell--state-error"), Is.True);
                Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.False);

                setPhase.Invoke(appState, new[] { Enum.Parse(phaseType, "Complete"), "Recovery complete" });
                present.Invoke(tryCreateArguments[1], new object[] { appState });
                Assert.That(phaseLabel.text, Is.EqualTo("PHASE  /  COMPLETE"));
                Assert.That(appShell.ClassListContains("app-shell--state-success"), Is.True);
                Assert.That(appShell.ClassListContains("app-shell--state-error"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(stateObject);
            }
        }

        [Test]
        public void ControlPanelViewProvidesTypedThemeAndLightRequestSeamsWithoutWorldServices()
        {
            System.Type presenterType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            System.Type viewType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelView, Assembly-CSharp");
            System.Type themeType = System.Type.GetType("FacilityViewer.UI.FacilityThemeId, Assembly-CSharp");
            System.Type lightGroupType = System.Type.GetType("FacilityViewer.UI.FacilityLightGroupId, Assembly-CSharp");
            System.Type idsType = System.Type.GetType("FacilityViewer.UI.FacilityControlPanelIds, Assembly-CSharp");
            VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BootstrapShellPath);
            TemplateContainer visualTree = visualTreeAsset.CloneTree();
            MethodInfo tryCreate = viewType.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.Public);
            object[] tryCreateArguments = { visualTree, null };

            Assert.That(presenterType, Is.Not.Null);
            Assert.That(presenterType.GetEvent("ThemeRequested"), Is.Not.Null);
            Assert.That(presenterType.GetEvent("LightGroupRequested"), Is.Not.Null);
            Assert.That(presenterType.GetMethod("SetThemeControlsAvailable"), Is.Not.Null);
            Assert.That(presenterType.GetMethod("SetLightingControlsAvailable"), Is.Not.Null);
            Assert.That(themeType, Is.Not.Null);
            Assert.That(lightGroupType, Is.Not.Null);
            Assert.That(idsType, Is.Not.Null);
            Assert.That((bool)tryCreate.Invoke(null, tryCreateArguments), Is.True);

            object view = tryCreateArguments[1];
            MethodInfo subscribe = viewType.GetMethod("SubscribeControlRequests", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo unsubscribe = viewType.GetMethod("UnsubscribeControlRequests", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo presentThemes = viewType.GetMethod("PresentThemeControls", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo presentLights = viewType.GetMethod("PresentLightControls", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo presentAvailability = viewType.GetMethod("PresentControlAvailability", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo stableThemeId = idsType.GetMethod("GetStableId", new[] { themeType });
            MethodInfo stableLightId = idsType.GetMethod("GetStableId", new[] { lightGroupType });
            FieldInfo themeHandlersField = viewType.GetField("themeClickHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lightHandlersField = viewType.GetField("lightClickHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
            object maintenanceTheme = Enum.Parse(themeType, "Maintenance");
            object ambientLight = Enum.Parse(lightGroupType, "Ambient");

            ThemeRequests.Clear();
            LightRequests.Clear();
            subscribe.Invoke(view, new object[]
            {
                CreateTypedCaptureDelegate(themeType, nameof(CaptureThemeRequest)),
                CreateTypedCaptureDelegate(lightGroupType, nameof(CaptureLightRequest))
            });
            presentThemes.Invoke(view, new object[] { false, null });
            presentLights.Invoke(view, new object[] { false, null });
            presentAvailability.Invoke(view, new object[] { false, false });

            Button maintenanceButton = visualTree.Q<Button>("theme-maintenance-button");
            Button ambientButton = visualTree.Q<Button>("lighting-ambient-button");
            Label availabilityLabel = visualTree.Q<Label>("control-availability-label");
            Assert.That(maintenanceButton.enabledSelf, Is.False);
            Assert.That(maintenanceButton.text, Is.EqualTo("MAINTENANCE (UNAVAILABLE)"));
            Assert.That(maintenanceButton.tooltip, Does.Contain("unavailable"));
            Assert.That(ambientButton.enabledSelf, Is.False);
            Assert.That(availabilityLabel.text, Is.EqualTo("CONTROL SERVICES  /  THEME AND LIGHTING UNAVAILABLE"));

            presentThemes.Invoke(view, new object[] { true, maintenanceTheme });
            object lightStates = Activator.CreateInstance(
                typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(lightGroupType, typeof(bool)));
            lightStates.GetType().GetMethod("Add").Invoke(lightStates, new[] { ambientLight, (object)true });
            presentLights.Invoke(view, new[] { (object)true, lightStates });
            presentAvailability.Invoke(view, new object[] { true, true });

            Assert.That(maintenanceButton.enabledSelf, Is.True);
            Assert.That(maintenanceButton.ClassListContains("is-selected"), Is.True);
            Assert.That(ambientButton.enabledSelf, Is.True);
            Assert.That(ambientButton.ClassListContains("is-selected"), Is.True);
            Assert.That(stableThemeId.Invoke(null, new[] { maintenanceTheme }), Is.EqualTo("maintenance"));
            Assert.That(stableLightId.Invoke(null, new[] { ambientLight }), Is.EqualTo("ambient"));

            System.Collections.IDictionary themeHandlers =
                (System.Collections.IDictionary)themeHandlersField.GetValue(view);
            System.Collections.IDictionary lightHandlers =
                (System.Collections.IDictionary)lightHandlersField.GetValue(view);
            Delegate maintenanceHandler = (Delegate)themeHandlers[maintenanceTheme];
            Delegate ambientHandler = (Delegate)lightHandlers[ambientLight];
            maintenanceHandler.DynamicInvoke();
            ambientHandler.DynamicInvoke();
            Assert.That(ThemeRequests, Is.EqualTo(new[] { maintenanceTheme }));
            Assert.That(LightRequests, Is.EqualTo(new[] { ambientLight }));

            unsubscribe.Invoke(view, null);
            maintenanceHandler.DynamicInvoke();
            ambientHandler.DynamicInvoke();
            Assert.That(ThemeRequests, Has.Count.EqualTo(1));
            Assert.That(LightRequests, Has.Count.EqualTo(1));
        }

        [Test]
        public void ControlPanelViewRejectsMissingThemeAndLightingButtons()
        {
            System.Type viewType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelView, Assembly-CSharp");
            VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BootstrapShellPath);

            Assert.That(viewType, Is.Not.Null);
            MethodInfo tryCreate = viewType.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.Public);
            Assert.That(tryCreate, Is.Not.Null);

            string[] controlButtonNames =
            {
                "theme-standard-button",
                "theme-maintenance-button",
                "theme-emergency-button",
                "lighting-ambient-button",
                "lighting-operations-button",
                "lighting-emergency-button"
            };

            for (int index = 0; index < controlButtonNames.Length; index++)
            {
                TemplateContainer incompleteTree = visualTreeAsset.CloneTree();
                incompleteTree.Q<Button>(controlButtonNames[index]).RemoveFromHierarchy();
                object[] tryCreateArguments = { incompleteTree, null };

                Assert.That(
                    (bool)tryCreate.Invoke(null, tryCreateArguments),
                    Is.False,
                    controlButtonNames[index]);
                Assert.That(tryCreateArguments[1], Is.Null, controlButtonNames[index]);
            }
        }

        [Test]
        public void RuntimePanelSettingsUseTheApprovedScaleWithScreenSizeBaseline()
        {
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(RuntimePanelSettingsPath);
            Assert.That(panelSettings, Is.Not.Null);

            Assert.That((int)panelSettings.scaleMode, Is.EqualTo(2));
            Assert.That(panelSettings.referenceResolution.x, Is.EqualTo(1920));
            Assert.That(panelSettings.referenceResolution.y, Is.EqualTo(1080));
            Assert.That(panelSettings.match, Is.EqualTo(0.5f));
        }

        [Test]
        public void FacilityPanelSafeAreaMapperPreservesInsetsAndLayoutBreakpoints()
        {
            System.Type layoutType = System.Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelLayout, Assembly-CSharp");
            Assert.That(layoutType, Is.Not.Null);
            MethodInfo safeAreaMapper = layoutType.GetMethod(
                "CalculateSafeArea",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo layoutMapper = layoutType.GetMethod(
                "IsCompactLandscape",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(safeAreaMapper, Is.Not.Null);
            Assert.That(layoutMapper, Is.Not.Null);

            Rect mappedSafeArea = (Rect)safeAreaMapper.Invoke(
                null,
                new object[]
                {
                    new Rect(100f, 50f, 1720f, 970f),
                    new Vector2(1920f, 1080f),
                    new Vector2(960f, 540f)
                });

            Assert.That(mappedSafeArea, Is.EqualTo(new Rect(50f, 25f, 860f, 485f)));
            Assert.That(layoutMapper.Invoke(null, new object[] { new Vector2(1920f, 1080f) }), Is.EqualTo(false));
            Assert.That(layoutMapper.Invoke(null, new object[] { new Vector2(1024f, 576f) }), Is.EqualTo(true));
            Assert.That(layoutMapper.Invoke(null, new object[] { new Vector2(1024f, 768f) }), Is.EqualTo(true));
        }

        private static void AssertStyleSource(string path, string requiredText)
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(path), Is.Not.Null, path);
            Assert.That(File.ReadAllText(path), Does.Contain(requiredText), path);
        }

        private static Delegate CreateTypedCaptureDelegate(System.Type parameterType, string methodName)
        {
            MethodInfo method = typeof(FacilityTransitionAssetTests)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(parameterType);
            return Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(parameterType), method);
        }

        private static void CaptureThemeRequest<T>(T theme)
        {
            ThemeRequests.Add(theme);
        }

        private static void CaptureLightRequest<T>(T group)
        {
            LightRequests.Add(group);
        }

        private static void AssertBuildProfileScenes(string path)
        {
            Object profile = AssetDatabase.LoadMainAssetAtPath(path);
            SerializedProperty scenes = new SerializedObject(profile).FindProperty("m_Scenes");

            Assert.That(scenes.arraySize, Is.EqualTo(ProductionScenes.Length), path);

            for (int index = 0; index < ProductionScenes.Length; index++)
            {
                SerializedProperty scene = scenes.GetArrayElementAtIndex(index);
                Assert.That(scene.FindPropertyRelative("m_enabled").boolValue, Is.True, path);
                Assert.That(scene.FindPropertyRelative("m_path").stringValue, Is.EqualTo(ProductionScenes[index]), path);
            }
        }
    }
}

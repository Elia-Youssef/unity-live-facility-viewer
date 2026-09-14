using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class ProjectFoundationTests
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string PanelSettingsPath = "Assets/Settings/RuntimePanelSettings.asset";
        private const string UxmlPath = "Assets/UI/Documents/BootstrapShell.uxml";
        private const string StyleSheetPath = "Assets/UI/Styles/BootstrapShell.uss";
        private const string ShaderPath = "Assets/Art/Shaders/FoundationSurface.shadergraph";
        private const string MaterialPath = "Assets/Art/Materials/Standard/FoundationSurface.mat";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string WindowsProfilePath = "Assets/Settings/BuildProfiles/Windows Development.asset";
        private const string AndroidProfilePath = "Assets/Settings/BuildProfiles/Android Development.asset";
        private const string DefaultVolumeProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
        private const string UniversalGlobalSettingsPath = "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset";

        [Test]
        public void BootstrapIsFirstEnabledBuildScene()
        {
            EditorBuildSettingsScene firstEnabledScene = EditorBuildSettings.scenes.First(scene => scene.enabled);

            Assert.That(firstEnabledScene.path, Is.EqualTo(BootstrapScenePath));
        }

        [Test]
        public void BootstrapSceneContainsConnectedUiDocument()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestorePreviousSetup = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                UIDocument document = Object.FindAnyObjectByType<UIDocument>();

                Assert.That(document, Is.Not.Null);
                Assert.That(document.panelSettings, Is.EqualTo(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath)));
                Assert.That(document.visualTreeAsset, Is.EqualTo(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)));
            }
            finally
            {
                if (canRestorePreviousSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void BootstrapSceneOwnsOneExplicitPersistentComposition()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestorePreviousSetup = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                GameObject[] roots = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene()
                    .GetRootGameObjects();

                Assert.That(roots, Has.Length.EqualTo(1));
                Assert.That(roots[0].name, Is.EqualTo("Bootstrap"));

                Transform applicationUi = roots[0].transform.Find("Application UI");
                Transform player = roots[0].transform.Find("Player");

                Assert.That(applicationUi, Is.Not.Null);
                Assert.That(player, Is.Not.Null);
                Assert.That(player.gameObject.activeSelf, Is.False);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(player.gameObject), Is.Not.Null);

                MonoBehaviour[] rootComponents = roots[0].GetComponents<MonoBehaviour>();
                MonoBehaviour appState = rootComponents.Single(component =>
                    component.GetType().FullName == "FacilityViewer.Core.AppState");
                MonoBehaviour teleportService = rootComponents.Single(component =>
                    component.GetType().FullName == "FacilityViewer.Services.LevelTeleportService");
                MonoBehaviour appBootstrapper = rootComponents.Single(component =>
                    component.GetType().FullName == "FacilityViewer.Core.AppBootstrapper");

                Assert.That(appState, Is.Not.Null);
                Assert.That(teleportService, Is.Not.Null);
                Assert.That(appBootstrapper, Is.Not.Null);
                Assert.That(applicationUi.GetComponent<UIDocument>(), Is.Not.Null);
                Assert.That(player.GetComponent<CharacterController>(), Is.Not.Null);
                Assert.That(player.GetComponentInChildren<Camera>(true), Is.Not.Null);
                Assert.That(roots[0].GetComponentsInChildren<Camera>(true), Has.Length.EqualTo(1));
                Assert.That(roots[0].GetComponentsInChildren<AudioListener>(true), Has.Length.EqualTo(1));
                Assert.That(roots[0].GetComponentsInChildren<Light>(true), Is.Empty);

                SerializedObject serializedBootstrapper = new(appBootstrapper);
                SerializedObject serializedTeleportService = new(teleportService);

                Assert.That(
                    serializedBootstrapper.FindProperty("appState").objectReferenceValue,
                    Is.EqualTo(appState));
                Assert.That(
                    serializedBootstrapper.FindProperty("levelTeleportService").objectReferenceValue,
                    Is.EqualTo(teleportService));
                Assert.That(
                    serializedBootstrapper.FindProperty("applicationUi").objectReferenceValue,
                    Is.EqualTo(applicationUi.GetComponent<UIDocument>()));
                Assert.That(
                    serializedTeleportService.FindProperty("appState").objectReferenceValue,
                    Is.EqualTo(appState));
                Assert.That(
                    serializedTeleportService.FindProperty("playerRoot").objectReferenceValue,
                    Is.EqualTo(player.gameObject));
                Assert.That(
                    serializedTeleportService.FindProperty("characterController").objectReferenceValue,
                    Is.EqualTo(player.GetComponent<CharacterController>()));
            }
            finally
            {
                if (canRestorePreviousSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void BootstrapVisualTreeContainsRequiredLabels()
        {
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            TemplateContainer root = visualTree.CloneTree();

            Assert.That(root.Q<Label>("title-label"), Is.Not.Null);
            Assert.That(root.Q<Label>("status-label"), Is.Not.Null);
            Assert.That(AssetDatabase.GetDependencies(UxmlPath), Does.Contain(StyleSheetPath));
        }

        [Test]
        public void FoundationMaterialUsesSupportedShaderGraph()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.EqualTo(shader));
        }

        [Test]
        public void DevelopmentBuildProfilesExist()
        {
            Object windowsProfile = AssetDatabase.LoadMainAssetAtPath(WindowsProfilePath);

            Assert.That(windowsProfile, Is.Not.Null);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(AndroidProfilePath), Is.Not.Null);

            SerializedProperty developmentProperty = new SerializedObject(windowsProfile)
                .FindProperty("m_PlatformBuildProfile.m_Development");

            Assert.That(developmentProperty, Is.Not.Null);
            Assert.That(developmentProperty.boolValue, Is.True);
        }

        [Test]
        public void InputActionsRemainPreloaded()
        {
            Object inputActions = AssetDatabase.LoadMainAssetAtPath(InputActionsPath);

            Assert.That(inputActions, Is.Not.Null);
            Assert.That(PlayerSettings.GetPreloadedAssets(), Does.Contain(inputActions));
        }

        [Test]
        public void DefaultVolumeProfileContainsOnlyLoadableComponents()
        {
            Object profile = AssetDatabase.LoadMainAssetAtPath(DefaultVolumeProfilePath);

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.name, Is.EqualTo("DefaultVolumeProfile"));
            Assert.That(
                AssetDatabase.GetDependencies(UniversalGlobalSettingsPath),
                Does.Contain(DefaultVolumeProfilePath));

            SerializedProperty components = new SerializedObject(profile).FindProperty("components");

            Assert.That(components, Is.Not.Null);
            Assert.That(components.isArray, Is.True);
            Assert.That(components.arraySize, Is.GreaterThan(0));

            for (int index = 0; index < components.arraySize; index++)
            {
                Assert.That(
                    components.GetArrayElementAtIndex(index).objectReferenceValue,
                    Is.Not.Null,
                    $"Volume component {index} must be loadable.");
            }

            Assert.That(AssetDatabase.LoadAllAssetsAtPath(DefaultVolumeProfilePath), Has.None.Null);

            string serializedProfile = File.ReadAllText(DefaultVolumeProfilePath);
            Assert.That(serializedProfile, Does.Not.Contain("m_Script: {fileID: 0}"));
            Assert.That(serializedProfile, Does.Not.Contain("Unity.RenderPipelines.Core.Editor.Tests"));
        }
    }
}

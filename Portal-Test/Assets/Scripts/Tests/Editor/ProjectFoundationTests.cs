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
    }
}

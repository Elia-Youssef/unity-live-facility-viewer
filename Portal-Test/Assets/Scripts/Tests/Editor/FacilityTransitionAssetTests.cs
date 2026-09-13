using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class FacilityTransitionAssetTests
    {
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
        public void BootstrapUiAndTransitionServiceAreFullyConnected()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                Scene scene = EditorSceneManager.OpenScene(ProductionScenes[0], OpenSceneMode.Single);
                GameObject root = scene.GetRootGameObjects().Single();
                Transform applicationUi = root.transform.Find("Application UI");
                Transform player = root.transform.Find("Player");
                UIDocument document = applicationUi.GetComponent<UIDocument>();
                Component presenter = applicationUi.GetComponent("FacilityTransitionPresenter");
                Component service = root.GetComponent("LevelTeleportService");
                Component state = root.GetComponent("AppState");
                Component coordinator = player.GetComponent("PlayerInputCoordinator");
                TemplateContainer visualTree = document.visualTreeAsset.CloneTree();

                Assert.That(presenter, Is.Not.Null);
                Assert.That(visualTree.Q<Button>("level-lobby-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("level-operations-button"), Is.Not.Null);
                Assert.That(visualTree.Q<Button>("level-plant-button"), Is.Not.Null);
                Assert.That(visualTree.Q<VisualElement>("loading-overlay"), Is.Not.Null);

                SerializedObject serializedPresenter = new(presenter);
                Assert.That(serializedPresenter.FindProperty("uiDocument").objectReferenceValue, Is.EqualTo(document));
                Assert.That(serializedPresenter.FindProperty("appState").objectReferenceValue, Is.EqualTo(state));
                Assert.That(serializedPresenter.FindProperty("levelTeleportService").objectReferenceValue, Is.EqualTo(service));
                Assert.That(serializedPresenter.FindProperty("inputCoordinator").objectReferenceValue, Is.EqualTo(coordinator));

                SerializedObject serializedService = new(service);
                Assert.That(serializedService.FindProperty("initialLevel").objectReferenceValue.name, Is.EqualTo("Lobby"));
                Assert.That(serializedService.FindProperty("availableLevels").arraySize, Is.EqualTo(3));
            }
            finally
            {
                if (previousSetup.Any(scene => scene.isLoaded))
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
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

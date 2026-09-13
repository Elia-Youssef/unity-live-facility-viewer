using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class TeleportPadAssetTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const string TeleportPadPrefabPath = "Assets/Prefabs/TeleportPads/TeleportPad.prefab";
        private const string TeleportPadMaterialPath =
            "Assets/Art/Materials/Standard/TeleportPadEmission.mat";
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string BootstrapShellPath = "Assets/UI/Documents/BootstrapShell.uxml";
        private const string PromptStylePath = "Assets/UI/Styles/TeleportPadPrompt.uss";
        private const float MinimumPadPositionSeparation = 0.01f;

        [Test]
        public void TeleportPadRuntimeTypesExposeTheExpectedBoundaries()
        {
            System.Type padType = System.Type.GetType("FacilityViewer.World.TeleportPad, Assembly-CSharp");
            System.Type interactorType = System.Type.GetType(
                "FacilityViewer.Player.PlayerTeleportPadInteractor, Assembly-CSharp");
            System.Type transitionControllerType = System.Type.GetType(
                "FacilityViewer.Services.TeleportPadTransitionController, Assembly-CSharp");
            System.Type promptPresenterType = System.Type.GetType(
                "FacilityViewer.UI.TeleportPadPromptPresenter, Assembly-CSharp");
            System.Type levelDefinitionType = System.Type.GetType(
                "FacilityViewer.Core.LevelDefinition, Assembly-CSharp");
            System.Type serviceType = System.Type.GetType(
                "FacilityViewer.Services.LevelTeleportService, Assembly-CSharp");

            Assert.That(padType, Is.Not.Null);
            Assert.That(interactorType, Is.Not.Null);
            Assert.That(transitionControllerType, Is.Not.Null);
            Assert.That(promptPresenterType, Is.Not.Null);
            Assert.That(padType.IsSubclassOf(typeof(MonoBehaviour)), Is.True);
            Assert.That(interactorType.IsSubclassOf(typeof(MonoBehaviour)), Is.True);
            Assert.That(padType.GetProperty("DestinationLevel"), Is.Not.Null);
            Assert.That(padType.GetProperty("DestinationSpawnId")?.PropertyType, Is.EqualTo(typeof(string)));
            Assert.That(padType.GetProperty("CanInteract")?.PropertyType, Is.EqualTo(typeof(bool)));
            Assert.That(padType.GetMethod("TryValidate"), Is.Not.Null);
            Assert.That(padType.GetMethod("SetAvailable"), Is.Not.Null);
            Assert.That(padType.GetMethod("SetTransitionLocked"), Is.Not.Null);
            Assert.That(interactorType.GetProperty("ActivePad")?.PropertyType, Is.EqualTo(padType));
            Assert.That(interactorType.GetEvent("TeleportRequested"), Is.Not.Null);
            Assert.That(transitionControllerType.GetMethod("RequestTeleport", new[] { padType }), Is.Not.Null);
            Assert.That(
                serviceType.GetMethod("RequestTransition", new[] { levelDefinitionType, typeof(string) }),
                Is.Not.Null);
        }

        [Test]
        public void TeleportPadPrefabHasVisibleTriggerDrivenStructure()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TeleportPadPrefabPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TeleportPadMaterialPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(prefab.name, Is.EqualTo("TeleportPad"));

            Component pad = prefab.GetComponent("TeleportPad");
            BoxCollider trigger = prefab.GetComponent<BoxCollider>();
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            TextMesh destinationLabel = prefab.GetComponentInChildren<TextMesh>(true);
            Transform promptAnchor = prefab.transform.Find("PromptAnchor");

            Assert.That(pad, Is.Not.Null);
            Assert.That(trigger, Is.Not.Null);
            Assert.That(trigger.isTrigger, Is.True);
            Assert.That(trigger.size, Is.EqualTo(new Vector3(2.6f, 2f, 2.6f)));
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(renderers.Select(renderer => renderer.sharedMaterial), Does.Contain(material));
            Assert.That(destinationLabel, Is.Not.Null);
            Assert.That(destinationLabel.text, Is.EqualTo("DESTINATION"));
            Assert.That(promptAnchor, Is.Not.Null);

            SerializedObject serializedPad = new(pad);
            Assert.That(serializedPad.FindProperty("destinationLevel").objectReferenceValue, Is.Null);
            Assert.That(serializedPad.FindProperty("destinationSpawnId").stringValue, Is.EqualTo("entrance"));
            Assert.That(serializedPad.FindProperty("interactionPrompt").stringValue, Is.Empty);
            Assert.That(serializedPad.FindProperty("isAvailable").boolValue, Is.True);
            Assert.That(serializedPad.FindProperty("interactionTrigger").objectReferenceValue, Is.EqualTo(trigger));
            Assert.That(serializedPad.FindProperty("destinationLabel").objectReferenceValue, Is.EqualTo(destinationLabel));

            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Does.Contain("Universal Render Pipeline/Lit"));
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
            Color emission = material.GetColor("_EmissionColor");
            Assert.That(Mathf.Max(emission.r, emission.g, emission.b), Is.GreaterThan(1f));
        }

        [Test]
        public void PlayerPrefabOwnsOneInteractorWiredToItsInputRouter()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Component inputRouter = player.GetComponent("PlayerInputRouter");
            Component interactor = player.GetComponent("PlayerTeleportPadInteractor");

            Assert.That(player, Is.Not.Null);
            Assert.That(inputRouter, Is.Not.Null);
            Assert.That(interactor, Is.Not.Null);
            Assert.That(
                player.GetComponents<Component>().Count(component =>
                    component.GetType().Name == "PlayerTeleportPadInteractor"),
                Is.EqualTo(1));

            SerializedObject serializedInteractor = new(interactor);
            Assert.That(
                serializedInteractor.FindProperty("inputRouter").objectReferenceValue,
                Is.EqualTo(inputRouter));
            Assert.That(
                serializedInteractor.FindProperty("characterController").objectReferenceValue,
                Is.EqualTo(player.GetComponent<CharacterController>()));
        }

        [Test]
        public void BootstrapOwnsTransitionCoordinatorAndUiToolkitPrompt()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                Scene scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                GameObject bootstrap = scene.GetRootGameObjects().Single();
                Transform player = bootstrap.transform.Find("Player");
                Transform applicationUi = bootstrap.transform.Find("Application UI");
                Component appState = bootstrap.GetComponent("AppState");
                Component service = bootstrap.GetComponent("LevelTeleportService");
                Component controller = bootstrap.GetComponent("TeleportPadTransitionController");
                Component interactor = player.GetComponent("PlayerTeleportPadInteractor");
                Component inputRouter = player.GetComponent("PlayerInputRouter");
                UIDocument document = applicationUi.GetComponent<UIDocument>();
                Component promptPresenter = applicationUi.GetComponent("TeleportPadPromptPresenter");

                Assert.That(controller, Is.Not.Null);
                Assert.That(promptPresenter, Is.Not.Null);

                SerializedObject serializedController = new(controller);
                Assert.That(serializedController.FindProperty("appState").objectReferenceValue, Is.EqualTo(appState));
                Assert.That(
                    serializedController.FindProperty("levelTeleportService").objectReferenceValue,
                    Is.EqualTo(service));
                Assert.That(
                    serializedController.FindProperty("playerInteractor").objectReferenceValue,
                    Is.EqualTo(interactor));

                SerializedObject serializedPrompt = new(promptPresenter);
                Assert.That(serializedPrompt.FindProperty("uiDocument").objectReferenceValue, Is.EqualTo(document));
                Assert.That(serializedPrompt.FindProperty("appState").objectReferenceValue, Is.EqualTo(appState));
                Assert.That(
                    serializedPrompt.FindProperty("playerInteractor").objectReferenceValue,
                    Is.EqualTo(interactor));
                Assert.That(serializedPrompt.FindProperty("inputRouter").objectReferenceValue, Is.EqualTo(inputRouter));

                VisualTreeAsset shell = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BootstrapShellPath);
                TemplateContainer visualTree = shell.CloneTree();
                Assert.That(visualTree.Q<VisualElement>("teleport-prompt"), Is.Not.Null);
                Assert.That(visualTree.Q<Label>("teleport-prompt-action-label"), Is.Not.Null);
                Assert.That(visualTree.Q<Label>("teleport-prompt-destination-label"), Is.Not.Null);
                Assert.That(AssetDatabase.GetDependencies(BootstrapShellPath), Does.Contain(PromptStylePath));
                Assert.That(bootstrap.GetComponentsInChildren<Canvas>(true), Is.Empty);
            }
            finally
            {
                RestoreSceneSetupOrOpenEmptyScene(previousSetup, canRestore);
            }
        }

        [Test]
        public void FacilityScenesContainTwoPrefabLinkedReachableRoutes()
        {
            Dictionary<string, string[]> expectedRoutes = new()
            {
                ["Assets/Scenes/Lobby.unity"] = new[] { "operations-floor", "plant-room" },
                ["Assets/Scenes/OperationsFloor.unity"] = new[] { "lobby", "plant-room" },
                ["Assets/Scenes/PlantRoom.unity"] = new[] { "lobby", "operations-floor" }
            };
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                foreach (KeyValuePair<string, string[]> expectedRoute in expectedRoutes)
                {
                    Scene scene = EditorSceneManager.OpenScene(expectedRoute.Key, OpenSceneMode.Single);
                    MonoBehaviour[] pads = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                        .Where(component => component.GetType().FullName == "FacilityViewer.World.TeleportPad")
                        .ToArray();

                    Assert.That(pads, Has.Length.EqualTo(2), expectedRoute.Key);
                    Assert.That(
                        pads.Select(pad => GetDestinationLevelId(pad)),
                        Is.EquivalentTo(expectedRoute.Value),
                        expectedRoute.Key);

                    BoxCollider[] triggers = pads
                        .Select(pad => pad.GetComponent<BoxCollider>())
                        .ToArray();

                    Assert.That(triggers, Has.All.Not.Null, expectedRoute.Key);
                    Assert.That(
                        (pads[0].transform.position - pads[1].transform.position).sqrMagnitude,
                        Is.GreaterThan(MinimumPadPositionSeparation * MinimumPadPositionSeparation),
                        $"{expectedRoute.Key} places both teleport pads at the same position.");
                    Assert.That(
                        triggers[0].bounds.Intersects(triggers[1].bounds),
                        Is.False,
                        $"{expectedRoute.Key} has overlapping teleport-pad triggers.");

                    foreach (MonoBehaviour pad in pads)
                    {
                        Assert.That(
                            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(pad.gameObject),
                            Is.EqualTo(TeleportPadPrefabPath));
                        Assert.That(
                            pad.GetType().GetProperty("DestinationSpawnId").GetValue(pad),
                            Is.EqualTo("entrance"));
                        Assert.That(
                            pad.GetType().GetProperty("HasValidConfiguration").GetValue(pad),
                            Is.EqualTo(true));
                    }
                }
            }
            finally
            {
                RestoreSceneSetupOrOpenEmptyScene(previousSetup, canRestore);
            }
        }

        private static void RestoreSceneSetupOrOpenEmptyScene(
            SceneSetup[] previousSetup,
            bool canRestore)
        {
            if (canRestore)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static string GetDestinationLevelId(MonoBehaviour pad)
        {
            object destination = pad.GetType().GetProperty("DestinationLevel").GetValue(pad);
            return (string)destination.GetType().GetProperty("LevelId").GetValue(destination);
        }
    }
}

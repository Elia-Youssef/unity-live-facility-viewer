using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FacilityViewer.Tests
{
    public sealed class PlayerPrefabTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const string PlayerControlsPath = "Assets/Settings/Input/PlayerControls.inputactions";
        private const string MovementTestScenePath = "Assets/Scenes/PlayerMovementTest.unity";

        [Test]
        public void PlayerPrefabHasExpectedStructureAndReferences()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            Assert.That(player, Is.Not.Null);
            Assert.That(player.name, Is.EqualTo("Player"));
            Assert.That(player.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(player.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(player.transform.localScale, Is.EqualTo(Vector3.one));

            CharacterController characterController = player.GetComponent<CharacterController>();
            PlayerInput playerInput = player.GetComponent<PlayerInput>();
            Component playerController = player.GetComponent("PlayerController");
            Component inputRouter = player.GetComponent("PlayerInputRouter");
            Component inputCoordinator = player.GetComponent("PlayerInputCoordinator");
            Transform cameraPivot = player.transform.Find("CameraPivot");
            Transform cameraTransform = cameraPivot?.Find("PlayerCamera");
            Camera playerCamera = cameraTransform?.GetComponent<Camera>();
            AudioListener audioListener = cameraTransform?.GetComponent<AudioListener>();
            ScriptableObject playerSettings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Data/PlayerSettings/DefaultPlayerSettings.asset");

            Assert.That(player.GetComponents<CharacterController>(), Has.Length.EqualTo(1));
            Assert.That(player.GetComponents<PlayerInput>(), Has.Length.EqualTo(1));
            Assert.That(
                player.GetComponents<Component>().Count(component => component.GetType().Name == "PlayerController"),
                Is.EqualTo(1));
            Assert.That(
                player.GetComponents<Component>().Count(component => component.GetType().Name == "PlayerInputRouter"),
                Is.EqualTo(1));
            Assert.That(
                player.GetComponents<Component>().Count(component => component.GetType().Name == "PlayerInputCoordinator"),
                Is.EqualTo(1));
            Assert.That(cameraPivot, Is.Not.Null);
            Assert.That(cameraTransform, Is.Not.Null);
            Assert.That(playerCamera, Is.Not.Null);
            Assert.That(audioListener, Is.Not.Null);
            Assert.That(
                cameraTransform.GetComponents<Component>()
                    .Count(component => component.GetType().Name == "UniversalAdditionalCameraData"),
                Is.EqualTo(1));
            Assert.That(cameraPivot.localPosition, Is.EqualTo(new Vector3(0f, 1.65f, 0f)));
            Assert.That(cameraTransform.localPosition, Is.EqualTo(Vector3.zero));

            Assert.That(characterController.height, Is.EqualTo(1.8f));
            Assert.That(characterController.radius, Is.EqualTo(0.35f));
            Assert.That(characterController.center, Is.EqualTo(new Vector3(0f, 0.9f, 0f)));
            Assert.That(playerCamera.nearClipPlane, Is.EqualTo(0.05f));
            Assert.That(playerCamera.farClipPlane, Is.EqualTo(1000f));
            Assert.That(playerCamera.fieldOfView, Is.EqualTo(60f));
            Assert.That(playerInput.actions, Is.EqualTo(
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(PlayerControlsPath)));
            Assert.That(AssetDatabase.GetAssetPath(playerInput.actions), Is.EqualTo(PlayerControlsPath));
            Assert.That(AssetDatabase.GetAssetPath(playerInput.actions), Is.Not.EqualTo(
                "Assets/InputSystem_Actions.inputactions"));
            Assert.That(playerInput.defaultActionMap, Is.EqualTo("Gameplay"));
            Assert.That(playerInput.defaultControlScheme, Is.EqualTo("Keyboard&Mouse"));
            Assert.That(playerInput.notificationBehavior, Is.EqualTo(PlayerNotifications.InvokeCSharpEvents));

            SerializedObject serializedRouter = new(inputRouter);

            Assert.That(
                serializedRouter.FindProperty("playerInput").objectReferenceValue,
                Is.EqualTo(playerInput));
            Assert.That(inputRouter.GetType().GetProperty("MoveInput")?.PropertyType, Is.EqualTo(typeof(Vector2)));
            Assert.That(inputRouter.GetType().GetProperty("LookInput")?.PropertyType, Is.EqualTo(typeof(Vector2)));
            Assert.That(inputRouter.GetType().GetProperty("IsSprinting")?.PropertyType, Is.EqualTo(typeof(bool)));
            Assert.That(inputRouter.GetType().GetEvent("InteractRequested"), Is.Not.Null);
            Assert.That(inputRouter.GetType().GetEvent("TogglePanelRequested"), Is.Not.Null);

            SerializedObject serializedCoordinator = new(inputCoordinator);

            Assert.That(
                serializedCoordinator.FindProperty("inputRouter").objectReferenceValue,
                Is.EqualTo(inputRouter));
            Assert.That(
                serializedCoordinator.FindProperty("inputModeOverride").enumDisplayNames[
                    serializedCoordinator.FindProperty("inputModeOverride").enumValueIndex],
                Is.EqualTo("Automatic"));

            SerializedObject serializedController = new(playerController);

            Assert.That(
                serializedController.FindProperty("playerSettings").objectReferenceValue,
                Is.EqualTo(playerSettings));
            Assert.That(
                serializedController.FindProperty("characterController").objectReferenceValue,
                Is.EqualTo(characterController));
            Assert.That(
                serializedController.FindProperty("inputRouter").objectReferenceValue,
                Is.EqualTo(inputRouter));
            Assert.That(
                serializedController.FindProperty("cameraPivot").objectReferenceValue,
                Is.EqualTo(cameraPivot));
            Assert.That(
                serializedController.FindProperty("playerCamera").objectReferenceValue,
                Is.EqualTo(playerCamera));
            Assert.That(playerController.GetType().GetProperty("IsGrounded")?.PropertyType, Is.EqualTo(typeof(bool)));
            Assert.That(
                playerController.GetType().GetProperty("CurrentHorizontalSpeed")?.PropertyType,
                Is.EqualTo(typeof(float)));
            Assert.That(
                playerController.GetType().GetProperty("VerticalVelocity")?.PropertyType,
                Is.EqualTo(typeof(float)));
            Assert.That(playerController.GetType().GetProperty("Pitch")?.PropertyType, Is.EqualTo(typeof(float)));
        }

        [Test]
        public void MovementTestSceneContainsExpectedCourseAndStaysOutOfBuild()
        {
            Assert.That(SceneUtility.GetBuildIndexByScenePath(MovementTestScenePath), Is.EqualTo(-1));

            Scene scene = EditorSceneManager.OpenScene(MovementTestScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject course = scene.GetRootGameObjects().Single(root => root.name == "MovementCourse");
                GameObject player = scene.GetRootGameObjects().Single(root => root.name == "Player");
                string[] requiredCourseObjects =
                {
                    "Floor",
                    "LowStep_0.2m",
                    "Wall",
                    "HighObstacle_0.8m",
                    "WalkableSlope_25deg",
                    "SteepSlope_55deg",
                    "PlayerStart"
                };

                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(player),
                    Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath)));
                Assert.That(player.transform.position, Is.EqualTo(new Vector3(0f, 0f, -6f)));
                Component coordinator = player.GetComponent("PlayerInputCoordinator");
                SerializedObject serializedCoordinator = new(coordinator);
                SerializedProperty inputModeOverride = serializedCoordinator.FindProperty("inputModeOverride");

                Assert.That(
                    inputModeOverride.enumDisplayNames[inputModeOverride.enumValueIndex],
                    Is.EqualTo("Mobile"));
                Assert.That(course.transform.Cast<Transform>().Select(child => child.name),
                    Is.SupersetOf(requiredCourseObjects));
                Assert.That(course.transform.Find("Floor").GetComponent<Collider>().enabled, Is.True);
                Assert.That(course.transform.Find("LowStep_0.2m").localScale.y, Is.EqualTo(0.2f));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

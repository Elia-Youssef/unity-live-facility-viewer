#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class PlayerControllerTests : InputTestFixture
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

        [UnityTest]
        public IEnumerator PlayerFallsAcceleratesSprintsStopsAndClampsLook()
        {
            GameObject floor = CreateBlock(
                "TestFloor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(30f, 1f, 30f));
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            PlayerInput playerInput = CreatePlayer(keyboard, mouse, new Vector3(0f, 2f, 0f));

            try
            {
                Component controller = playerInput.GetComponent("PlayerController");
                TypeInfo controllerType = controller.GetType().GetTypeInfo();

                yield return new WaitForSeconds(0.75f);

                Assert.That((bool)GetProperty(controllerType, "IsGrounded").GetValue(controller), Is.True);
                Assert.That(playerInput.transform.position.y, Is.InRange(-0.01f, 0.05f));
                Assert.That((float)GetProperty(controllerType, "VerticalVelocity").GetValue(controller),
                    Is.EqualTo(-2f).Within(0.01f));

                Press(keyboard.wKey);
                yield return new WaitForSeconds(0.35f);

                float walkingSpeed =
                    (float)GetProperty(controllerType, "CurrentHorizontalSpeed").GetValue(controller);

                Assert.That(walkingSpeed, Is.InRange(3.8f, 4.05f));
                Assert.That(playerInput.transform.position.z, Is.GreaterThan(0.5f));

                Press(keyboard.leftShiftKey);
                yield return new WaitForSeconds(0.25f);

                float sprintingSpeed =
                    (float)GetProperty(controllerType, "CurrentHorizontalSpeed").GetValue(controller);

                Assert.That(sprintingSpeed, Is.GreaterThan(walkingSpeed));
                Assert.That(sprintingSpeed, Is.InRange(6.2f, 6.55f));

                float yawBefore = playerInput.transform.eulerAngles.y;
                Set(mouse.delta, new Vector2(100f, 2000f));
                yield return null;

                float pitch = (float)GetProperty(controllerType, "Pitch").GetValue(controller);
                float yawChange = Mathf.Abs(Mathf.DeltaAngle(yawBefore, playerInput.transform.eulerAngles.y));

                Assert.That(pitch, Is.EqualTo(-85f).Within(0.01f));
                Assert.That(yawChange, Is.GreaterThan(1f));

                Release(keyboard.wKey);
                yield return null;
                Release(keyboard.leftShiftKey);
                yield return new WaitForSeconds(0.4f);

                float stoppedSpeed =
                    (float)GetProperty(controllerType, "CurrentHorizontalSpeed").GetValue(controller);

                Assert.That(stoppedSpeed, Is.LessThan(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(playerInput.gameObject);
                Object.DestroyImmediate(floor);
            }
        }

        [UnityTest]
        public IEnumerator PlayerClimbsConfiguredStepAndStopsAtWall()
        {
            GameObject floor = CreateBlock(
                "TestFloor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(20f, 1f, 20f));
            GameObject step = CreateBlock(
                "TestStep",
                new Vector3(0f, 0.1f, 1f),
                new Vector3(2f, 0.2f, 1.5f));
            GameObject wall = CreateBlock(
                "TestWall",
                new Vector3(0f, 1.2f, 2f),
                new Vector3(2f, 2f, 0.3f));
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            PlayerInput playerInput = CreatePlayer(keyboard, mouse, new Vector3(0f, 0f, -1f));

            try
            {
                CharacterController characterController = playerInput.GetComponent<CharacterController>();

                yield return null;

                Assert.That(characterController.slopeLimit, Is.EqualTo(45f));
                Assert.That(characterController.stepOffset, Is.EqualTo(0.3f));

                Press(keyboard.wKey);
                yield return new WaitForSeconds(1f);

                Assert.That(playerInput.transform.position.y, Is.InRange(0.18f, 0.25f));
                Assert.That(playerInput.transform.position.z, Is.LessThan(1.6f));
            }
            finally
            {
                Object.DestroyImmediate(playerInput.gameObject);
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(step);
                Object.DestroyImmediate(floor);
            }
        }

        private static PlayerInput CreatePlayer(Keyboard keyboard, Mouse mouse, Vector3 position)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            Camera playerCamera = playerInput.GetComponentInChildren<Camera>(true);
            AudioListener audioListener = playerInput.GetComponentInChildren<AudioListener>(true);

            playerInput.transform.SetPositionAndRotation(position, Quaternion.identity);
            playerCamera.enabled = false;
            audioListener.enabled = false;
            return playerInput;
        }

        private static GameObject CreateBlock(string name, Vector3 position, Vector3 scale)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);

            block.name = name;
            block.transform.position = position;
            block.transform.localScale = scale;
            return block;
        }

        private static PropertyInfo GetProperty(TypeInfo type, string name)
        {
            PropertyInfo property = type.GetProperty(name);

            Assert.That(property, Is.Not.Null, $"Missing observable property '{name}'.");
            return property;
        }
    }
}
#endif

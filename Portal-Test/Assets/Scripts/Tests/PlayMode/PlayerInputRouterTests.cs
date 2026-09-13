#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class PlayerInputRouterTests : InputTestFixture
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

        [Test]
        public void PlayerPrefabRoutesDesktopInputAndClearsReleasedState()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = null;

            try
            {
                playerInput = PlayerInput.Instantiate(
                    prefab,
                    controlScheme: "Keyboard&Mouse",
                    pairWithDevices: new InputDevice[] { keyboard, mouse });

                Camera playerCamera = playerInput.GetComponentInChildren<Camera>(true);
                AudioListener audioListener = playerInput.GetComponentInChildren<AudioListener>(true);

                playerCamera.enabled = false;
                audioListener.enabled = false;

                Component router = playerInput.GetComponent("PlayerInputRouter");
                Type routerType = router.GetType();
                PropertyInfo moveInput = routerType.GetProperty("MoveInput");
                PropertyInfo lookInput = routerType.GetProperty("LookInput");
                PropertyInfo isSprinting = routerType.GetProperty("IsSprinting");
                int interactCount = 0;
                int togglePanelCount = 0;

                routerType.GetEvent("InteractRequested")?.AddEventHandler(
                    router,
                    new Action(() => interactCount++));
                routerType.GetEvent("TogglePanelRequested")?.AddEventHandler(
                    router,
                    new Action(() => togglePanelCount++));

                Press(keyboard.wKey);
                Press(keyboard.dKey);
                Press(keyboard.leftShiftKey);
                Set(mouse.delta, new Vector2(12f, -4f));

                Assert.That(
                    Vector2.Distance(
                        (Vector2)moveInput.GetValue(router),
                        new Vector2(1f, 1f).normalized),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector2.Distance(
                        (Vector2)lookInput.GetValue(router),
                        new Vector2(12f, -4f)),
                    Is.LessThan(0.0001f));
                Assert.That((bool)isSprinting.GetValue(router), Is.True);

                Click(keyboard.eKey);
                Click(keyboard.tabKey);

                Assert.That(interactCount, Is.EqualTo(1));
                Assert.That(togglePanelCount, Is.EqualTo(1));

                Release(keyboard.wKey);
                Release(keyboard.dKey);
                Release(keyboard.leftShiftKey);
                Set(mouse.delta, Vector2.zero);

                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((Vector2)lookInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((bool)isSprinting.GetValue(router), Is.False);

                Press(keyboard.wKey);
                Press(keyboard.leftShiftKey);
                ((Behaviour)router).enabled = false;

                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((Vector2)lookInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((bool)isSprinting.GetValue(router), Is.False);
            }
            finally
            {
                if (playerInput != null)
                {
                    UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator PlayerEnabledAfterItsParentClaimsDesktopDevicesAndEnablesGameplay()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject inactiveBootstrap = new("Inactive Bootstrap");
            inactiveBootstrap.SetActive(false);
            GameObject player = UnityEngine.Object.Instantiate(prefab, inactiveBootstrap.transform);

            try
            {
                PlayerInput playerInput = player.GetComponent<PlayerInput>();

                Assert.That(player.activeInHierarchy, Is.False);

                inactiveBootstrap.SetActive(true);
                yield return null;

                Assert.That(playerInput.inputIsActive, Is.True);
                Assert.That(playerInput.currentActionMap?.name, Is.EqualTo("Gameplay"));
                Assert.That(playerInput.currentControlScheme, Is.EqualTo("Keyboard&Mouse"));
                Assert.That(playerInput.devices, Does.Contain(keyboard));
                Assert.That(playerInput.devices, Does.Contain(mouse));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inactiveBootstrap);
            }
        }
    }
}
#endif

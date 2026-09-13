#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class MobileControlsTests : InputTestFixture
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

        [UnityTest]
        public IEnumerator MobileControlsCaptureIndependentPointersRouteValuesAndCancelCleanly()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });

            try
            {
                DisablePlayerOutput(playerInput.gameObject);
                Component coordinator = playerInput.GetComponent("PlayerInputCoordinator");
                SetInputModeOverride(coordinator, "Mobile");
                yield return null;
                yield return null;

                Transform mobileControls = playerInput.transform.Find("MobileControls");
                UIDocument uiDocument = mobileControls.GetComponent<UIDocument>();
                Behaviour presenter = (Behaviour)mobileControls.GetComponent("MobileControlsPresenter");
                Component router = playerInput.GetComponent("PlayerInputRouter");
                Type presenterType = presenter.GetType();
                Type routerType = router.GetType();
                VisualElement moveStick = uiDocument.rootVisualElement.Q<VisualElement>("move-stick");
                VisualElement lookRegion = uiDocument.rootVisualElement.Q<VisualElement>("look-region");
                Button interactButton = uiDocument.rootVisualElement.Q<Button>("interact-button");
                Button panelButton = uiDocument.rootVisualElement.Q<Button>("panel-button");
                PropertyInfo moveInput = routerType.GetProperty("MoveInput");
                PropertyInfo lookInput = routerType.GetProperty("LookInput");
                PropertyInfo activeMovePointer = presenterType.GetProperty("ActiveMovePointerId");
                PropertyInfo activeLookPointer = presenterType.GetProperty("ActiveLookPointerId");
                PropertyInfo gameplayControlsVisible = presenterType.GetProperty("AreGameplayControlsVisible");
                PropertyInfo inputOwner = routerType.GetProperty("InputOwner");
                int interactCount = 0;
                int panelCount = 0;

                routerType.GetEvent("InteractRequested")?.AddEventHandler(router, new Action(() => interactCount++));
                routerType.GetEvent("TogglePanelRequested")?.AddEventHandler(router, new Action(() => panelCount++));

                Assert.That((bool)presenterType.GetProperty("IsReady").GetValue(presenter), Is.True);
                Assert.That((bool)presenterType.GetProperty("AreControlsVisible").GetValue(presenter), Is.True);
                Assert.That((bool)gameplayControlsVisible.GetValue(presenter), Is.True);
                Assert.That(moveStick.worldBound.width, Is.GreaterThan(0f));
                Assert.That(lookRegion.worldBound.width, Is.GreaterThan(0f));

                int movePointerId = PointerId.touchPointerIdBase;
                int lookPointerId = PointerId.touchPointerIdBase + 1;
                Vector2 moveCenter = moveStick.worldBound.center;
                Vector2 moveUp = moveCenter + new Vector2(0f, -48f);
                Vector2 lookStart = lookRegion.worldBound.center;
                Vector2 lookEnd = lookStart + new Vector2(24f, -12f);

                SendPointerEvent<PointerDownEvent>(moveStick, EventType.MouseDown, moveUp, Vector2.zero, movePointerId);

                Assert.That((int)activeMovePointer.GetValue(presenter), Is.EqualTo(movePointerId));
                Assert.That(moveStick.HasPointerCapture(movePointerId), Is.True);
                Assert.That(((Vector2)moveInput.GetValue(router)).y, Is.GreaterThan(0.5f));

                Vector2 moveBeforeIgnoredPointer = (Vector2)moveInput.GetValue(router);
                SendPointerEvent<PointerMoveEvent>(
                    moveStick,
                    EventType.MouseDrag,
                    moveCenter + new Vector2(-48f, 0f),
                    new Vector2(-48f, 0f),
                    movePointerId + 5);

                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(moveBeforeIgnoredPointer));

                SendPointerEvent<PointerDownEvent>(lookRegion, EventType.MouseDown, lookStart, Vector2.zero, lookPointerId);
                SendPointerEvent<PointerMoveEvent>(
                    lookRegion,
                    EventType.MouseDrag,
                    lookEnd,
                    lookEnd - lookStart,
                    lookPointerId);

                Assert.That((int)activeLookPointer.GetValue(presenter), Is.EqualTo(lookPointerId));
                Assert.That(lookRegion.HasPointerCapture(lookPointerId), Is.True);
                Assert.That((Vector2)lookInput.GetValue(router), Is.EqualTo(new Vector2(12f, 6f)));

                Vector2 consumedLook = (Vector2)routerType.GetMethod("ConsumeLookInput").Invoke(router, null);

                Assert.That(consumedLook, Is.EqualTo(new Vector2(12f, 6f)));
                Assert.That((Vector2)lookInput.GetValue(router), Is.EqualTo(Vector2.zero));

                ClickButton(interactButton);

                Assert.That(interactCount, Is.EqualTo(1));

                ClickButton(panelButton);

                Assert.That(panelCount, Is.EqualTo(1));
                Assert.That(inputOwner.GetValue(router).ToString(), Is.EqualTo("UserInterface"));
                Assert.That((bool)gameplayControlsVisible.GetValue(presenter), Is.False);
                Assert.That(panelButton.text, Is.EqualTo("RETURN"));
                Assert.That((int)activeMovePointer.GetValue(presenter), Is.EqualTo(PointerId.invalidPointerId));
                Assert.That((int)activeLookPointer.GetValue(presenter), Is.EqualTo(PointerId.invalidPointerId));
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));

                yield return null;
                ClickButton(panelButton);

                Assert.That(panelCount, Is.EqualTo(2));
                Assert.That(inputOwner.GetValue(router).ToString(), Is.EqualTo("Gameplay"));
                Assert.That((bool)gameplayControlsVisible.GetValue(presenter), Is.True);
                Assert.That(panelButton.text, Is.EqualTo("PANEL"));

                SendPointerEvent<PointerCancelEvent>(
                    moveStick,
                    EventType.Ignore,
                    moveUp,
                    Vector2.zero,
                    movePointerId);
                SendPointerEvent<PointerUpEvent>(
                    lookRegion,
                    EventType.MouseUp,
                    lookEnd,
                    Vector2.zero,
                    lookPointerId);

                Assert.That((int)activeMovePointer.GetValue(presenter), Is.EqualTo(PointerId.invalidPointerId));
                Assert.That((int)activeLookPointer.GetValue(presenter), Is.EqualTo(PointerId.invalidPointerId));
                Assert.That(moveStick.HasPointerCapture(movePointerId), Is.False);
                Assert.That(lookRegion.HasPointerCapture(lookPointerId), Is.False);
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));

                SendPointerEvent<PointerDownEvent>(moveStick, EventType.MouseDown, moveUp, Vector2.zero, movePointerId);
                presenter.enabled = false;

                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That(moveStick.HasPointerCapture(movePointerId), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CoordinatorSelectsOneSourceAndBlocksGameplayWhileUiOwnsInput()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });

            try
            {
                playerInput.GetComponentInChildren<Camera>(true).enabled = false;
                playerInput.GetComponentInChildren<AudioListener>(true).enabled = false;

                Component router = playerInput.GetComponent("PlayerInputRouter");
                Component coordinator = playerInput.GetComponent("PlayerInputCoordinator");
                Component presenter = playerInput.transform.Find("MobileControls").GetComponent("MobileControlsPresenter");
                Type routerType = router.GetType();
                Type coordinatorType = coordinator.GetType();
                Type presenterType = presenter.GetType();
                PropertyInfo moveInput = routerType.GetProperty("MoveInput");
                PropertyInfo lookInput = routerType.GetProperty("LookInput");
                PropertyInfo sprinting = routerType.GetProperty("IsSprinting");
                FieldInfo desktopMoveInput = routerType.GetField(
                    "desktopMoveInput",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo desktopLookInput = routerType.GetField(
                    "desktopLookInput",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo desktopSprintInput = routerType.GetField(
                    "desktopSprintInput",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(routerType.GetProperty("InputMode").GetValue(router).ToString(), Is.EqualTo("Desktop"));
                Assert.That(routerType.GetProperty("InputOwner").GetValue(router).ToString(), Is.EqualTo("Gameplay"));
                Assert.That(((Behaviour)router).enabled, Is.True);
                Assert.That(playerInput.enabled, Is.True);
                Assert.That(playerInput.inputIsActive, Is.True);
                Assert.That(playerInput.currentActionMap, Is.Not.Null);
                Assert.That(playerInput.currentActionMap.name, Is.EqualTo("Gameplay"));
                Assert.That(playerInput.actions.FindActionMap("Gameplay").enabled, Is.True);
                Assert.That((bool)presenterType.GetProperty("AreControlsVisible").GetValue(presenter), Is.False);
                Assert.That((bool)coordinatorType.GetProperty("WantsLockedCursor").GetValue(coordinator), Is.True);

                desktopMoveInput.SetValue(router, Vector2.up);
                desktopLookInput.SetValue(router, new Vector2(8f, -3f));
                desktopSprintInput.SetValue(router, true);

                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.up));
                Assert.That((Vector2)lookInput.GetValue(router), Is.EqualTo(new Vector2(8f, -3f)));
                Assert.That((bool)sprinting.GetValue(router), Is.True);

                coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });

                Assert.That(routerType.GetProperty("InputOwner").GetValue(router).ToString(), Is.EqualTo("UserInterface"));
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((Vector2)lookInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((bool)sprinting.GetValue(router), Is.False);
                Assert.That((bool)coordinatorType.GetProperty("WantsLockedCursor").GetValue(coordinator), Is.False);

                coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { false });
                Assert.That(routerType.GetProperty("InputOwner").GetValue(router).ToString(), Is.EqualTo("Gameplay"));

                desktopMoveInput.SetValue(router, Vector2.up);
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.up));

                SetInputModeOverride(coordinator, "Mobile");

                Assert.That(routerType.GetProperty("InputMode").GetValue(router).ToString(), Is.EqualTo("Mobile"));
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That((bool)presenterType.GetProperty("AreControlsVisible").GetValue(presenter), Is.True);
                Assert.That((bool)coordinatorType.GetProperty("WantsLockedCursor").GetValue(coordinator), Is.False);

                routerType.GetMethod("SetMobileMoveInput").Invoke(router, new object[] { Vector2.right });
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.right));

                coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });
                Assert.That((Vector2)moveInput.GetValue(router), Is.EqualTo(Vector2.zero));
                Assert.That(
                    (bool)presenterType.GetProperty("AreGameplayControlsVisible").GetValue(presenter),
                    Is.False);

                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator PresenterReportsAndDisablesForAnIncompleteVisualTree()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            VisualTreeAsset incompleteTree = ScriptableObject.CreateInstance<VisualTreeAsset>();
            GameObject failureObject = new("IncompleteMobileControls");
            failureObject.SetActive(false);
            failureObject.transform.SetParent(playerInput.transform, false);

            try
            {
                DisablePlayerOutput(playerInput.gameObject);
                UIDocument document = failureObject.AddComponent<UIDocument>();
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Settings/RuntimePanelSettings.asset");
                document.visualTreeAsset = incompleteTree;
                Component presenter = failureObject.AddComponent(
                    Type.GetType("FacilityViewer.UI.MobileControlsPresenter, Assembly-CSharp"));
                SerializedObject serializedPresenter = new(presenter);
                serializedPresenter.FindProperty("uiDocument").objectReferenceValue = document;
                serializedPresenter.FindProperty("inputRouter").objectReferenceValue =
                    playerInput.GetComponent("PlayerInputRouter");
                serializedPresenter.ApplyModifiedPropertiesWithoutUndo();

                LogAssert.Expect(
                    LogType.Error,
                    "[MobileControls] Required UI Toolkit elements are missing from MobileControls.");
                failureObject.SetActive(true);
                yield return null;

                Assert.That(((Behaviour)presenter).enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
                UnityEngine.Object.DestroyImmediate(incompleteTree);
            }
        }

        private static void DisablePlayerOutput(GameObject player)
        {
            player.GetComponentInChildren<Camera>(true).enabled = false;
            player.GetComponentInChildren<AudioListener>(true).enabled = false;
            player.GetComponent("PlayerController").GetType().GetProperty("enabled")?.SetValue(
                player.GetComponent("PlayerController"),
                false);
        }

        private static void SetInputModeOverride(Component coordinator, string modeName)
        {
            MethodInfo method = coordinator.GetType().GetMethod("SetInputModeOverride");
            Type modeType = method.GetParameters()[0].ParameterType;
            method.Invoke(coordinator, new[] { Enum.Parse(modeType, modeName) });
        }

        private static void ClickButton(Button button)
        {
            Vector2 center = button.worldBound.center;
            SendPointerEvent<PointerDownEvent>(
                button,
                EventType.MouseDown,
                center,
                Vector2.zero,
                PointerId.mousePointerId);
            SendPointerEvent<PointerUpEvent>(
                button,
                EventType.MouseUp,
                center,
                Vector2.zero,
                PointerId.mousePointerId);
        }

        private static void SendPointerEvent<TEvent>(
            VisualElement target,
            EventType systemEventType,
            Vector2 position,
            Vector2 delta,
            int pointerId)
            where TEvent : EventBase<TEvent>, new()
        {
            Type pointerEventBase = typeof(TEvent).BaseType;
            EventBase pointerEvent = pointerId == PointerId.mousePointerId
                ? CreateMousePointerEvent(pointerEventBase, systemEventType, position, delta)
                : CreateTouchPointerEvent(pointerEventBase, systemEventType, position, delta, pointerId);

            try
            {
                target.SendEvent(pointerEvent);
            }
            finally
            {
                pointerEvent.Dispose();
            }
        }

        private static EventBase CreateMousePointerEvent(
            Type pointerEventBase,
            EventType systemEventType,
            Vector2 position,
            Vector2 delta)
        {
            MethodInfo factory = pointerEventBase
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "GetPooled"
                        && parameters.Length == 7
                        && parameters[0].ParameterType == typeof(EventType);
                });

            return (EventBase)factory.Invoke(
                null,
                new object[]
                {
                    systemEventType,
                    new Vector3(position.x, position.y, 0f),
                    delta,
                    0,
                    1,
                    EventModifiers.None,
                    0
                });
        }

        private static EventBase CreateTouchPointerEvent(
            Type pointerEventBase,
            EventType systemEventType,
            Vector2 position,
            Vector2 delta,
            int pointerId)
        {
            UnityEngine.TouchPhase phase = systemEventType switch
            {
                EventType.MouseDown => UnityEngine.TouchPhase.Began,
                EventType.MouseUp => UnityEngine.TouchPhase.Ended,
                EventType.Ignore => UnityEngine.TouchPhase.Canceled,
                _ => UnityEngine.TouchPhase.Moved
            };
            object boxedTouch = default(Touch);

            SetTouchField(boxedTouch, "m_FingerId", pointerId - PointerId.touchPointerIdBase);
            SetTouchField(boxedTouch, "m_Position", position);
            SetTouchField(boxedTouch, "m_RawPosition", position);
            SetTouchField(boxedTouch, "m_PositionDelta", delta);
            SetTouchField(boxedTouch, "m_TapCount", 1);
            SetTouchField(boxedTouch, "m_Phase", phase);

            MethodInfo factory = pointerEventBase
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "GetPooled"
                        && parameters.Length == 4
                        && parameters[0].ParameterType == typeof(Touch)
                        && parameters[1].ParameterType == typeof(int);
                });

            return (EventBase)factory.Invoke(
                null,
                new[] { boxedTouch, (object)pointerId, EventModifiers.None, 0 });
        }

        private static void SetTouchField(object boxedTouch, string fieldName, object value)
        {
            typeof(Touch).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(boxedTouch, value);
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class TeleportPadInteractionTests : InputTestFixture
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const int TeleportPadLayer = 8;

        private static readonly List<Component> RequestedPads = new();

        [Test]
        public void ActivePadRoutesDesktopAndMobileUseWithoutOwningTheInputSource()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            GameObject padObject = CreateConfiguredPad();

            try
            {
                DisablePlayerOutput(playerInput.gameObject);

                Component inputRouter = playerInput.GetComponent("PlayerInputRouter");
                Component interactor = playerInput.GetComponent("PlayerTeleportPadInteractor");
                Component pad = padObject.GetComponent("TeleportPad");
                Type interactorType = interactor.GetType();
                Type routerType = inputRouter.GetType();
                EventInfo teleportRequested = interactorType.GetEvent("TeleportRequested");
                Delegate capture = CreateCaptureDelegate(teleportRequested.EventHandlerType, pad.GetType());
                EventInfo interactRequested = routerType.GetEvent("InteractRequested");
                int routedInteractCount = 0;
                Action countRoutedInteract = () => routedInteractCount++;

                RequestedPads.Clear();
                teleportRequested.AddEventHandler(interactor, capture);
                interactRequested.AddEventHandler(inputRouter, countRoutedInteract);
                Physics.SyncTransforms();
                interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);

                Assert.That(interactorType.GetProperty("ActivePad").GetValue(interactor), Is.EqualTo(pad));
                Assert.That((bool)interactorType.GetProperty("HasAvailablePad").GetValue(interactor), Is.True);

                MethodInfo setInputMode = routerType.GetMethod("SetInputMode");
                object desktopMode = Enum.Parse(setInputMode.GetParameters()[0].ParameterType, "Desktop");
                setInputMode.Invoke(inputRouter, new[] { desktopMode });
                Assert.That(playerInput.inputIsActive, Is.True);
                Assert.That(playerInput.currentActionMap?.name, Is.EqualTo("Gameplay"));
                Assert.That(playerInput.currentControlScheme, Is.EqualTo("Keyboard&Mouse"));

                Click(keyboard.eKey);
                Assert.That(routedInteractCount, Is.EqualTo(1));
                Assert.That(RequestedPads, Is.EqualTo(new[] { pad }));

                object mobileMode = Enum.Parse(setInputMode.GetParameters()[0].ParameterType, "Mobile");
                setInputMode.Invoke(inputRouter, new[] { mobileMode });
                routerType.GetMethod("RequestMobileInteract").Invoke(inputRouter, null);
                Assert.That(RequestedPads, Is.EqualTo(new[] { pad, pad }));

                MethodInfo setInputOwner = routerType.GetMethod("SetInputOwner");
                object uiOwner = Enum.Parse(setInputOwner.GetParameters()[0].ParameterType, "UserInterface");
                setInputOwner.Invoke(inputRouter, new[] { uiOwner });
                routerType.GetMethod("RequestMobileInteract").Invoke(inputRouter, null);
                Assert.That(RequestedPads, Has.Count.EqualTo(2));

                padObject.transform.position = Vector3.forward * 10f;
                Physics.SyncTransforms();
                interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);
                Assert.That(interactorType.GetProperty("ActivePad").GetValue(interactor), Is.Null);

                teleportRequested.RemoveEventHandler(interactor, capture);
                interactRequested.RemoveEventHandler(inputRouter, countRoutedInteract);
            }
            finally
            {
                DestroyConfiguredPad(padObject);
                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
                RequestedPads.Clear();
            }
        }

        [UnityTest]
        public IEnumerator AvailabilityChangesRefreshTheActivePadImmediately()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            GameObject padObject = CreateConfiguredPad();

            try
            {
                DisablePlayerOutput(playerInput.gameObject);
                yield return null;

                Component interactor = playerInput.GetComponent("PlayerTeleportPadInteractor");
                Component pad = padObject.GetComponent("TeleportPad");
                PropertyInfo activePad = interactor.GetType().GetProperty("ActivePad");

                Physics.SyncTransforms();
                interactor.GetType().GetMethod("RefreshDetectedPads").Invoke(interactor, null);
                Assert.That(activePad.GetValue(interactor), Is.EqualTo(pad));

                pad.GetType().GetMethod("SetAvailable").Invoke(pad, new object[] { false });
                Assert.That(activePad.GetValue(interactor), Is.Null);

                pad.GetType().GetMethod("SetAvailable").Invoke(pad, new object[] { true });
                Assert.That(activePad.GetValue(interactor), Is.EqualTo(pad));
            }
            finally
            {
                DestroyConfiguredPad(padObject);
                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CharacterControllerMovementProducesPadEnterAndExit()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            GameObject padObject = CreateConfiguredPad();

            try
            {
                DisablePlayerOutput(playerInput.gameObject);
                CharacterController characterController = playerInput.GetComponent<CharacterController>();
                Component interactor = playerInput.GetComponent("PlayerTeleportPadInteractor");
                Component pad = padObject.GetComponent("TeleportPad");
                PropertyInfo activePad = interactor.GetType().GetProperty("ActivePad");
                playerInput.transform.position = new Vector3(0f, 0f, -3f);
                padObject.transform.position = Vector3.zero;
                Physics.SyncTransforms();

                for (int step = 0; step < 15; step++)
                {
                    characterController.Move(Vector3.forward * 0.2f);
                    yield return null;
                }

                interactor.GetType().GetMethod("RefreshDetectedPads").Invoke(interactor, null);

                Assert.That(activePad.GetValue(interactor), Is.EqualTo(pad));

                for (int step = 0; step < 15; step++)
                {
                    characterController.Move(Vector3.back * 0.2f);
                    yield return null;
                }

                interactor.GetType().GetMethod("RefreshDetectedPads").Invoke(interactor, null);

                Assert.That(activePad.GetValue(interactor), Is.Null);
            }
            finally
            {
                DestroyConfiguredPad(padObject);
                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator PadsEnabledAfterSceneLoadRegisterAndReceiveTheCurrentTransitionLock()
        {
            Type appStateType = Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            Type serviceType = Type.GetType(
                "FacilityViewer.Services.LevelTeleportService, Assembly-CSharp");
            Type routerType = Type.GetType("FacilityViewer.Player.PlayerInputRouter, Assembly-CSharp");
            Type interactorType = Type.GetType(
                "FacilityViewer.Player.PlayerTeleportPadInteractor, Assembly-CSharp");
            Type controllerType = Type.GetType(
                "FacilityViewer.Services.TeleportPadTransitionController, Assembly-CSharp");
            Type padType = Type.GetType("FacilityViewer.World.TeleportPad, Assembly-CSharp");

            Assert.That(appStateType, Is.Not.Null);
            Assert.That(serviceType, Is.Not.Null);
            Assert.That(routerType, Is.Not.Null);
            Assert.That(interactorType, Is.Not.Null);
            Assert.That(controllerType, Is.Not.Null);
            Assert.That(padType, Is.Not.Null);

            GameObject bootstrap = new("Teleport Pad Lifecycle Test Bootstrap");
            bootstrap.SetActive(false);
            Component appState = bootstrap.AddComponent(appStateType);
            Component service = bootstrap.AddComponent(serviceType);
            GameObject player = new("Teleport Pad Lifecycle Test Player");
            player.transform.SetParent(bootstrap.transform);
            player.AddComponent<CharacterController>();
            Behaviour router = (Behaviour)player.AddComponent(routerType);
            router.enabled = false;
            Component interactor = player.AddComponent(interactorType);
            Component controller = bootstrap.AddComponent(controllerType);
            Scene padScene = default;
            GameObject padObject = null;

            try
            {
                SetField(controller, "appState", appState);
                SetField(controller, "levelTeleportService", service);
                SetField(controller, "playerInteractor", interactor);
                bootstrap.SetActive(true);
                yield return null;

                appStateType.GetMethod("Initialize").Invoke(
                    appState,
                    new object[] { "Application services ready" });
                int initialRegisteredPadCount = (int)controllerType
                    .GetProperty("RegisteredPadCount")
                    .GetValue(controller);

                MethodInfo setTransitionPhase = appStateType.GetMethod("SetTransitionPhase");
                object loading = Enum.Parse(
                    setTransitionPhase.GetParameters()[0].ParameterType,
                    "Loading");
                setTransitionPhase.Invoke(appState, new[] { loading, "Loading test facility" });

                padScene = SceneManager.CreateScene("Teleport Pad Lifecycle Test Scene");
                padObject = CreateConfiguredPad(false);
                SceneManager.MoveGameObjectToScene(padObject, padScene);
                Component pad = padObject.GetComponent(padType);
                padObject.SetActive(true);

                Assert.That(
                    controllerType.GetProperty("RegisteredPadCount").GetValue(controller),
                    Is.EqualTo(initialRegisteredPadCount + 1));
                Assert.That(
                    padType.GetProperty("IsTransitionLocked").GetValue(pad),
                    Is.True);

                padObject.SetActive(false);

                Assert.That(
                    controllerType.GetProperty("RegisteredPadCount").GetValue(controller),
                    Is.EqualTo(initialRegisteredPadCount));
                Assert.That(
                    padType.GetProperty("IsTransitionLocked").GetValue(pad),
                    Is.False);

                object complete = Enum.Parse(
                    setTransitionPhase.GetParameters()[0].ParameterType,
                    "Complete");
                setTransitionPhase.Invoke(appState, new[] { complete, "Test transition complete" });
                padObject.SetActive(true);

                Assert.That(
                    controllerType.GetProperty("RegisteredPadCount").GetValue(controller),
                    Is.EqualTo(initialRegisteredPadCount + 1));
                Assert.That(
                    padType.GetProperty("IsTransitionLocked").GetValue(pad),
                    Is.False);
            }
            finally
            {
                if (padObject != null)
                {
                    DestroyConfiguredPad(padObject);
                }

                if (bootstrap != null)
                {
                    UnityEngine.Object.DestroyImmediate(bootstrap);
                }

                if (padScene.IsValid() && padScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(padScene);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SaturatedOverlapRecoversAllNearbyTeleportPads()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            List<GameObject> padObjects = new();

            try
            {
                DisablePlayerOutput(playerInput.gameObject);
                Component interactor = playerInput.GetComponent("PlayerTeleportPadInteractor");
                Type interactorType = interactor.GetType();
                Component closestPad = null;

                for (int index = 0; index < 17; index++)
                {
                    GameObject padObject = CreateConfiguredPad();
                    padObject.transform.position = new Vector3(index * 0.02f, 0f, 0f);
                    padObjects.Add(padObject);

                    if (index == 0)
                    {
                        closestPad = padObject.GetComponent("TeleportPad");
                    }
                }

                Physics.SyncTransforms();
                interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);

                Assert.That(
                    interactorType.GetProperty("UsedOverlapRecovery").GetValue(interactor),
                    Is.True);
                Assert.That(
                    interactorType.GetProperty("IsOverlapRecoverySaturated").GetValue(interactor),
                    Is.False);
                Assert.That(
                    interactorType.GetProperty("DetectedPadCount").GetValue(interactor),
                    Is.EqualTo(padObjects.Count));
                Assert.That(interactorType.GetProperty("ActivePad").GetValue(interactor), Is.EqualTo(closestPad));
            }
            finally
            {
                foreach (GameObject padObject in padObjects)
                {
                    DestroyConfiguredPad(padObject);
                }

                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DefaultLayerCollidersCannotCrowdOutTeleportPadDetection()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerInput playerInput = PlayerInput.Instantiate(
                prefab,
                controlScheme: "Keyboard&Mouse",
                pairWithDevices: new InputDevice[] { keyboard, mouse });
            List<GameObject> ordinaryColliders = new();
            GameObject padObject = null;

            try
            {
                DisablePlayerOutput(playerInput.gameObject);
                Component interactor = playerInput.GetComponent("PlayerTeleportPadInteractor");
                Type interactorType = interactor.GetType();

                for (int index = 0; index < 128; index++)
                {
                    GameObject ordinaryCollider = new($"Default Collider {index}");
                    ordinaryCollider.layer = 0;
                    ordinaryCollider.transform.position = Vector3.zero;
                    ordinaryCollider.AddComponent<BoxCollider>();
                    ordinaryColliders.Add(ordinaryCollider);
                }

                padObject = CreateConfiguredPad();
                padObject.transform.position = Vector3.zero;
                Component pad = padObject.GetComponent("TeleportPad");

                Physics.SyncTransforms();
                interactorType.GetMethod("RefreshDetectedPads").Invoke(interactor, null);

                Assert.That(
                    interactorType.GetProperty("UsedOverlapRecovery").GetValue(interactor),
                    Is.False,
                    "Default-layer colliders must not enter the teleport-pad query.");
                Assert.That(
                    interactorType.GetProperty("IsOverlapRecoverySaturated").GetValue(interactor),
                    Is.False);
                Assert.That(
                    interactorType.GetProperty("DetectedPadCount").GetValue(interactor),
                    Is.EqualTo(1));
                Assert.That(interactorType.GetProperty("ActivePad").GetValue(interactor), Is.EqualTo(pad));
            }
            finally
            {
                DestroyConfiguredPad(padObject);

                foreach (GameObject ordinaryCollider in ordinaryColliders)
                {
                    UnityEngine.Object.DestroyImmediate(ordinaryCollider);
                }

                UnityEngine.Object.DestroyImmediate(playerInput.gameObject);
            }

            yield return null;
        }

        private static GameObject CreateConfiguredPad(bool active = true)
        {
            Type padType = Type.GetType("FacilityViewer.World.TeleportPad, Assembly-CSharp");
            Type levelType = Type.GetType("FacilityViewer.Core.LevelDefinition, Assembly-CSharp");
            GameObject padObject = new("Configured Teleport Pad");
            padObject.layer = TeleportPadLayer;

            if (!active)
            {
                padObject.SetActive(false);
            }

            BoxCollider trigger = padObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            Component pad = padObject.AddComponent(padType);
            ScriptableObject destination = ScriptableObject.CreateInstance(levelType);

            SetField(destination, "levelId", "operations-floor");
            SetField(destination, "displayName", "Operations Floor");
            SetField(destination, "scenePath", "Assets/Scenes/OperationsFloor.unity");
            SetField(destination, "spawnPointId", "entrance");
            SetField(pad, "destinationLevel", destination);
            SetField(pad, "destinationSpawnId", "entrance");
            SetField(pad, "interactionTrigger", trigger);
            return padObject;
        }

        private static void DestroyConfiguredPad(GameObject padObject)
        {
            if (padObject == null)
            {
                return;
            }

            Component pad = padObject.GetComponent("TeleportPad");
            ScriptableObject destination = pad?.GetType()
                .GetProperty("DestinationLevel")
                .GetValue(pad) as ScriptableObject;

            UnityEngine.Object.DestroyImmediate(padObject);

            if (destination != null)
            {
                UnityEngine.Object.DestroyImmediate(destination);
            }
        }

        private static void DisablePlayerOutput(GameObject player)
        {
            player.GetComponentInChildren<Camera>(true).enabled = false;
            player.GetComponentInChildren<AudioListener>(true).enabled = false;
            ((Behaviour)player.GetComponent("PlayerController")).enabled = false;
        }

        private static Delegate CreateCaptureDelegate(Type eventHandlerType, Type padType)
        {
            MethodInfo captureMethod = typeof(TeleportPadInteractionTests)
                .GetMethod(nameof(CapturePad), BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(padType);
            return Delegate.CreateDelegate(eventHandlerType, captureMethod);
        }

        private static void CapturePad<T>(T pad) where T : Component
        {
            RequestedPads.Add(pad);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
#endif

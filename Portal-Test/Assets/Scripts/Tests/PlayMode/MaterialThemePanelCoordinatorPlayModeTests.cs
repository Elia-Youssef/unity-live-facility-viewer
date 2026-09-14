using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class MaterialThemePanelCoordinatorPlayModeTests
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const float TimeoutSeconds = 10f;

        [UnityTest]
        public IEnumerator ThemePanelUsesStableServiceIdsAndKeepsFailureStateNonTransitional()
        {
            Type appStateType = GetRuntimeType("FacilityViewer.Core.AppState");
            Type serviceType = GetRuntimeType("FacilityViewer.Services.MaterialThemeService");
            Type presenterType = GetRuntimeType("FacilityViewer.UI.FacilityControlPanelPresenter");
            Type coordinatorType = GetRuntimeType("FacilityViewer.UI.MaterialThemePanelCoordinator");
            Type inputCoordinatorType = GetRuntimeType("FacilityViewer.Player.PlayerInputCoordinator");
            Type targetType = GetRuntimeType("FacilityViewer.World.ThemeTarget");
            Type definitionType = GetRuntimeType("FacilityViewer.Core.MaterialThemeDefinition");
            Type themeType = GetRuntimeType("FacilityViewer.UI.FacilityThemeId");

            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);

            Component appState = FindRuntimeComponent(appStateType);
            Component service = FindRuntimeComponent(serviceType);
            Component presenter = FindRuntimeComponent(presenterType);
            Component coordinator = FindRuntimeComponent(coordinatorType);
            Component inputCoordinator = FindRuntimeComponent(inputCoordinatorType);
            yield return WaitForLevel(appState, "lobby");

            UIDocument document = presenter.GetComponent<UIDocument>();
            Button standardButton = document.rootVisualElement.Q<Button>("theme-standard-button");
            Button maintenanceButton = document.rootVisualElement.Q<Button>("theme-maintenance-button");
            Button emergencyButton = document.rootVisualElement.Q<Button>("theme-emergency-button");
            Assert.That(standardButton, Is.Not.Null);
            Assert.That(maintenanceButton, Is.Not.Null);
            Assert.That(emergencyButton, Is.Not.Null);
            Assert.That(GetProperty<bool>(coordinator, "IsThemeControlsAvailable"), Is.True);
            Assert.That(standardButton.enabledSelf, Is.True);
            Assert.That(standardButton.ClassListContains("is-selected"), Is.True);
            Assert.That(maintenanceButton.ClassListContains("is-selected"), Is.False);

            inputCoordinatorType.GetMethod("SetUiInputActive").Invoke(inputCoordinator, new object[] { true });
            yield return null;

            Component lobbyTarget = FindTarget(SceneManager.GetSceneByPath(LobbyPath), targetType);
            object lobbySlot = GetFirstSlot(lobbyTarget, targetType);
            Renderer lobbyRenderer = GetSlotRenderer(lobbySlot);
            int lobbyMaterialSlot = GetSlotIndex(lobbySlot);
            Material initialMaterial = GetSharedMaterials(lobbyRenderer)[lobbyMaterialSlot];
            string initialPhase = GetProperty<object>(appState, "TransitionPhase").ToString();

            SetProperty(service, "IsReady", false);
            InvokePrivate(coordinator, "RefreshPresentation");
            Assert.That(maintenanceButton.enabledSelf, Is.False);

            InvokePrivate(
                coordinator,
                "RequestTheme",
                Enum.Parse(themeType, "Maintenance"));
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo("Theme service is unavailable."));
            Assert.That(GetProperty<object>(appState, "TransitionPhase").ToString(), Is.EqualTo(initialPhase));
            Assert.That(GetSharedMaterials(lobbyRenderer)[lobbyMaterialSlot], Is.SameAs(initialMaterial));

            SetProperty(service, "IsReady", true);
            InvokePrivate(coordinator, "RefreshPresentation");
            Assert.That(maintenanceButton.enabledSelf, Is.True);

            InvokePrivate(coordinator, "RequestTheme", Enum.ToObject(themeType, 999));
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo("Theme request is invalid."));
            Assert.That(GetProperty<object>(appState, "TransitionPhase").ToString(), Is.EqualTo(initialPhase));
            Assert.That(GetSharedMaterials(lobbyRenderer)[lobbyMaterialSlot], Is.SameAs(initialMaterial));

            Assert.That(GetEventSubscriberCount(presenter, "ThemeRequested"), Is.EqualTo(1));
            MobileControlsTests.ClickButton(maintenanceButton);
            yield return null;

            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("maintenance"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo("Maintenance theme active"));
            Assert.That(GetProperty<object>(appState, "TransitionPhase").ToString(), Is.EqualTo(initialPhase));
            Assert.That(maintenanceButton.ClassListContains("is-selected"), Is.True);
            Assert.That(standardButton.ClassListContains("is-selected"), Is.False);
            Assert.That(GetSharedMaterials(lobbyRenderer)[lobbyMaterialSlot], Is.SameAs(GetThemeMaterial(
                GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"),
                definitionType,
                GetSlotGroupId(lobbySlot))));

            Assert.That(RequestTheme(serviceType, service, "standard"), Is.True);
            yield return null;
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(standardButton.ClassListContains("is-selected"), Is.True);
            Assert.That(emergencyButton.ClassListContains("is-selected"), Is.False);

            Assert.That(GetEventSubscriberCount(presenter, "ThemeRequested"), Is.EqualTo(1));
            ((Behaviour)coordinator).enabled = false;
            yield return null;
            ((Behaviour)coordinator).enabled = true;
            yield return null;
            Assert.That(GetEventSubscriberCount(presenter, "ThemeRequested"), Is.EqualTo(1));
            Assert.That(emergencyButton.enabledSelf, Is.True);

            InvokePrivate(presenter, "RequestTheme", Enum.Parse(themeType, "Emergency"));
            yield return null;
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("emergency"));
            Assert.That(emergencyButton.ClassListContains("is-selected"), Is.True);
            Assert.That(maintenanceButton.ClassListContains("is-selected"), Is.False);

            yield return CleanUpLoadedScenes();
        }

        [UnityTest]
        public IEnumerator MaintenanceThemeButtonRejectsInvalidLiveTargetWithoutChangingSelectedPresentation()
        {
            Type appStateType = GetRuntimeType("FacilityViewer.Core.AppState");
            Type serviceType = GetRuntimeType("FacilityViewer.Services.MaterialThemeService");
            Type presenterType = GetRuntimeType("FacilityViewer.UI.FacilityControlPanelPresenter");
            Type inputCoordinatorType = GetRuntimeType("FacilityViewer.Player.PlayerInputCoordinator");
            Type targetType = GetRuntimeType("FacilityViewer.World.ThemeTarget");

            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);

            Component appState = FindRuntimeComponent(appStateType);
            Component service = FindRuntimeComponent(serviceType);
            Component presenter = FindRuntimeComponent(presenterType);
            Component inputCoordinator = FindRuntimeComponent(inputCoordinatorType);
            yield return WaitForLevel(appState, "lobby");

            UIDocument document = presenter.GetComponent<UIDocument>();
            Button standardButton = document.rootVisualElement.Q<Button>("theme-standard-button");
            Button maintenanceButton = document.rootVisualElement.Q<Button>("theme-maintenance-button");
            Label transitionStatusLabel = document.rootVisualElement.Q<Label>("transition-status-label");
            Assert.That(standardButton, Is.Not.Null);
            Assert.That(maintenanceButton, Is.Not.Null);
            Assert.That(transitionStatusLabel, Is.Not.Null);
            Assert.That(standardButton.ClassListContains("is-selected"), Is.True);
            Assert.That(maintenanceButton.enabledSelf, Is.True);

            inputCoordinatorType.GetMethod("SetUiInputActive").Invoke(inputCoordinator, new object[] { true });
            yield return null;

            Component lobbyTarget = FindTarget(SceneManager.GetSceneByPath(LobbyPath), targetType);
            object lobbySlot = GetFirstSlot(lobbyTarget, targetType);
            Renderer lobbyRenderer = GetSlotRenderer(lobbySlot);
            int originalMaterialSlot = GetSlotIndex(lobbySlot);
            Material materialBeforeRejectedRequest =
                GetSharedMaterials(lobbyRenderer)[originalMaterialSlot];
            int targetCountBeforeRejectedRequest = GetProperty<int>(service, "RegisteredTargetCount");
            string selectedThemeBeforeRejectedRequest = GetProperty<string>(appState, "SelectedThemeId");
            string statusBeforeRejectedRequest = GetProperty<string>(appState, "StatusMessage");
            string phaseBeforeRejectedRequest = GetProperty<object>(appState, "TransitionPhase").ToString();

            Assert.That(selectedThemeBeforeRejectedRequest, Is.EqualTo("standard"));
            Assert.That(transitionStatusLabel.text, Is.EqualTo(statusBeforeRejectedRequest));

            try
            {
                SetSlotIndex(lobbySlot, -1);
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"\[Material Theme\] Theme request rejected:.*negative material-slot index"));
                MobileControlsTests.ClickButton(maintenanceButton);
                yield return null;

                Assert.That(GetProperty<string>(appState, "SelectedThemeId"),
                    Is.EqualTo(selectedThemeBeforeRejectedRequest));
                Assert.That(GetProperty<string>(appState, "StatusMessage"),
                    Is.EqualTo("Unable to activate MAINTENANCE theme."));
                Assert.That(GetProperty<object>(appState, "TransitionPhase").ToString(),
                    Is.EqualTo(phaseBeforeRejectedRequest));
                Assert.That(GetProperty<int>(service, "RegisteredTargetCount"),
                    Is.EqualTo(targetCountBeforeRejectedRequest));
                Assert.That(GetSharedMaterials(lobbyRenderer)[originalMaterialSlot],
                    Is.SameAs(materialBeforeRejectedRequest));
                Assert.That(standardButton.ClassListContains("is-selected"), Is.True);
                Assert.That(maintenanceButton.ClassListContains("is-selected"), Is.False);
                Assert.That(transitionStatusLabel.text, Is.EqualTo("Unable to activate MAINTENANCE theme."));
            }
            finally
            {
                SetSlotIndex(lobbySlot, originalMaterialSlot);
            }

            Assert.That(GetSlotIndex(lobbySlot), Is.EqualTo(originalMaterialSlot));
            yield return CleanUpLoadedScenes();
        }

        private static Type GetRuntimeType(string name)
        {
            Type type = Type.GetType($"{name}, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        private static bool RequestTheme(Type serviceType, Component service, string themeId)
        {
            return (bool)serviceType.GetMethod("RequestTheme", new[] { typeof(string) })
                .Invoke(service, new object[] { themeId });
        }

        private static Component FindTarget(Scene scene, Type themeTargetType)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Single(component => themeTargetType.IsInstanceOfType(component));
        }

        private static Component FindRuntimeComponent(Type type)
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Single(component => type.IsInstanceOfType(component) && component.gameObject.scene.IsValid());
        }

        private static object GetFirstSlot(Component target, Type targetType)
        {
            var slots = (IEnumerable)targetType.GetProperty("MaterialSlots").GetValue(target);
            return slots.Cast<object>().First();
        }

        private static Renderer GetSlotRenderer(object slot)
        {
            return (Renderer)slot.GetType().GetProperty("Renderer").GetValue(slot);
        }

        private static int GetSlotIndex(object slot)
        {
            return (int)slot.GetType().GetProperty("MaterialSlotIndex").GetValue(slot);
        }

        private static void SetSlotIndex(object slot, int materialSlotIndex)
        {
            slot.GetType().GetField("materialSlotIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(slot, materialSlotIndex);
        }

        private static string GetSlotGroupId(object slot)
        {
            return (string)slot.GetType().GetProperty("GroupId").GetValue(slot);
        }

        private static Material GetThemeMaterial(UnityEngine.Object definition, Type definitionType, string groupId)
        {
            object[] arguments = { groupId, null };
            bool found = (bool)definitionType.GetMethod("TryGetMaterial").Invoke(definition, arguments);
            Assert.That(found, Is.True, groupId);
            return (Material)arguments[1];
        }

        private static List<Material> GetSharedMaterials(Renderer renderer)
        {
            List<Material> materials = new();
            renderer.GetSharedMaterials(materials);
            return materials;
        }

        private static void SetProperty(Component component, string propertyName, object value)
        {
            component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetSetMethod(true)
                .Invoke(component, new[] { value });
        }

        private static void InvokePrivate(Component component, string methodName, params object[] arguments)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, arguments);
        }

        private static int GetEventSubscriberCount(Component component, string eventName)
        {
            Delegate subscribers = (Delegate)component.GetType().GetField(
                    eventName,
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(component);
            return subscribers?.GetInvocationList().Length ?? 0;
        }

        private static IEnumerator WaitForLevel(Component appState, string levelId)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetProperty<string>(appState, "CurrentLevelId") == levelId
                    && !GetProperty<bool>(appState, "IsTransitioning"))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for facility level '{levelId}'.");
        }

        private static IEnumerator CleanUpLoadedScenes()
        {
            Scene cleanupScene = SceneManager.CreateScene("MaterialThemePanelCoordinatorCleanup");
            SceneManager.SetActiveScene(cleanupScene);
            Scene[] loadedScenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.handle != cleanupScene.handle)
                .ToArray();

            for (int index = 0; index < loadedScenes.Length; index++)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(loadedScenes[index]);
                if (unload != null)
                {
                    yield return unload;
                }
            }
        }

        private static T GetProperty<T>(Component component, string propertyName)
        {
            return (T)component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(component);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class MaterialThemeServicePlayModeTests
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string OperationsFloorPath = "Assets/Scenes/OperationsFloor.unity";
        private const float TimeoutSeconds = 10f;

        [UnityTest]
        public IEnumerator SelectedThemePersistsAcrossAdditiveFacilityTransitionsWithoutMaterialCopies()
        {
            Type appStateType = Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            Type teleportServiceType = Type.GetType(
                "FacilityViewer.Services.LevelTeleportService, Assembly-CSharp");
            Type themeServiceType = Type.GetType(
                "FacilityViewer.Services.MaterialThemeService, Assembly-CSharp");
            Type themeTargetType = Type.GetType("FacilityViewer.World.ThemeTarget, Assembly-CSharp");
            Type definitionType = Type.GetType(
                "FacilityViewer.Core.MaterialThemeDefinition, Assembly-CSharp");

            Assert.That(appStateType, Is.Not.Null);
            Assert.That(teleportServiceType, Is.Not.Null);
            Assert.That(themeServiceType, Is.Not.Null);
            Assert.That(themeTargetType, Is.Not.Null);
            Assert.That(definitionType, Is.Not.Null);

            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);

            Component appState = FindRuntimeComponent(appStateType);
            Component teleportService = FindRuntimeComponent(teleportServiceType);
            Component themeService = FindRuntimeComponent(themeServiceType);
            yield return WaitForLevel(appState, "lobby");

            Assert.That(GetProperty<bool>(themeService, "IsReady"), Is.True);
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<int>(themeService, "RegisteredTargetCount"), Is.EqualTo(1));

            Component lobbyTarget = FindTarget(SceneManager.GetSceneByPath(LobbyPath), themeTargetType);
            object lobbySlot = GetFirstSlot(lobbyTarget, themeTargetType);
            Renderer lobbyRenderer = GetSlotRenderer(lobbySlot);
            int lobbyMaterialSlot = GetSlotIndex(lobbySlot);
            string lobbyGroupId = GetSlotGroupId(lobbySlot);
            Assert.That(RequestTheme(themeServiceType, themeService, "maintenance"), Is.True);
            Material expectedMaintenanceMaterial = GetThemeMaterial(
                GetProperty<UnityEngine.Object>(themeService, "CurrentThemeDefinition"),
                definitionType,
                lobbyGroupId);
            Assert.That(GetSharedMaterials(lobbyRenderer)[lobbyMaterialSlot],
                Is.SameAs(expectedMaintenanceMaterial));

            int stableTargetCount = GetProperty<int>(themeService, "RegisteredTargetCount");
            Assert.That(RequestTheme(themeServiceType, themeService, "maintenance"), Is.True);
            Assert.That(GetProperty<int>(themeService, "RegisteredTargetCount"), Is.EqualTo(stableTargetCount));
            Assert.That(GetSharedMaterials(lobbyRenderer)[lobbyMaterialSlot],
                Is.SameAs(expectedMaintenanceMaterial));

            Assert.That(RequestTransition(teleportServiceType, teleportService, "operations-floor"), Is.True);
            yield return WaitForLevel(appState, "operations-floor");

            Component operationsTarget = FindTarget(
                SceneManager.GetSceneByPath(OperationsFloorPath),
                themeTargetType);
            object operationsSlot = GetFirstSlot(operationsTarget, themeTargetType);
            Renderer operationsRenderer = GetSlotRenderer(operationsSlot);
            int operationsMaterialSlot = GetSlotIndex(operationsSlot);
            Material expectedOperationsMaterial = GetThemeMaterial(
                GetProperty<UnityEngine.Object>(themeService, "CurrentThemeDefinition"),
                definitionType,
                GetSlotGroupId(operationsSlot));

            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("maintenance"));
            Assert.That(GetProperty<int>(themeService, "RegisteredTargetCount"), Is.EqualTo(stableTargetCount));
            Assert.That(GetSharedMaterials(operationsRenderer)[operationsMaterialSlot],
                Is.SameAs(expectedOperationsMaterial));

            yield return CleanUpLoadedScenes();
        }

        private static bool RequestTheme(Type serviceType, Component service, string themeId)
        {
            return (bool)serviceType.GetMethod("RequestTheme", new[] { typeof(string) })
                .Invoke(service, new object[] { themeId });
        }

        private static bool RequestTransition(Type serviceType, Component service, string levelId)
        {
            return (bool)serviceType.GetMethod("RequestTransition", new[] { typeof(string) })
                .Invoke(service, new object[] { levelId });
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
            IEnumerable slots = (IEnumerable)targetType.GetProperty("MaterialSlots").GetValue(target);
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

        private static string GetSlotGroupId(object slot)
        {
            return (string)slot.GetType().GetProperty("GroupId").GetValue(slot);
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
            Scene cleanupScene = SceneManager.CreateScene("MaterialThemeServiceCleanup");
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

        private static T GetProperty<T>(Component target, string propertyName)
        {
            return (T)target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(target);
        }
    }
}

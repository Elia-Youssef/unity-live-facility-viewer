using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class FacilityTransitionTests
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
        private const float TimeoutSeconds = 10f;

        private Type appStateType;
        private Type serviceType;
        private Type levelDefinitionType;
        private Component appState;
        private Component service;
        private ScriptableObject invalidDefinition;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            appStateType = Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            serviceType = Type.GetType("FacilityViewer.Services.LevelTeleportService, Assembly-CSharp");
            levelDefinitionType = Type.GetType("FacilityViewer.Core.LevelDefinition, Assembly-CSharp");

            Assert.That(appStateType, Is.Not.Null);
            Assert.That(serviceType, Is.Not.Null);
            Assert.That(levelDefinitionType, Is.Not.Null);

            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
            appState = FindRuntimeComponent(appStateType);
            service = FindRuntimeComponent(serviceType);

            Assert.That(appState, Is.Not.Null);
            Assert.That(service, Is.Not.Null);
            yield return WaitForLevel("lobby");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (invalidDefinition != null)
            {
                UnityEngine.Object.Destroy(invalidDefinition);
            }

            Scene cleanupScene = SceneManager.CreateScene($"FacilityTransitionCleanup-{Guid.NewGuid():N}");
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

        [UnityTest]
        public IEnumerator TransitionLoopRejectsOverlapAndRecoversFromMissingSpawn()
        {
            Assert.That(GetStateProperty<string>("CurrentLevelName"), Is.EqualTo("Lobby"));
            Assert.That(GetStateProperty<object>("TransitionPhase").ToString(), Is.EqualTo("Complete"));
            Assert.That(GameObject.Find("Player").activeSelf, Is.True);

            invalidDefinition = ScriptableObject.CreateInstance(levelDefinitionType);
            SetField(invalidDefinition, "levelId", "invalid-plant");
            SetField(invalidDefinition, "displayName", "Plant Room");
            SetField(invalidDefinition, "scenePath", "Assets/Scenes/PlantRoom.unity");
            SetField(invalidDefinition, "spawnPointId", "missing-spawn");

            LogAssert.Expect(
                LogType.Error,
                new Regex(@"\[Transition\].*destination=Plant Room.*spawn=missing-spawn.*phase=Failed.*Spawn point 'missing-spawn' was not found"));
            Assert.That(RequestTransition(invalidDefinition, levelDefinitionType), Is.True);
            yield return WaitForPhase("Failed");

            Assert.That(GetStateProperty<string>("CurrentLevelId"), Is.EqualTo("lobby"));
            Assert.That(SceneManager.GetSceneByPath("Assets/Scenes/PlantRoom.unity").isLoaded, Is.False);

            Assert.That(RequestTransition("operations-floor", typeof(string)), Is.True);
            LogAssert.Expect(
                LogType.Warning,
                "[Transition] Request rejected because another transition is active.");
            Assert.That(RequestTransition("plant-room", typeof(string)), Is.False);
            yield return WaitForLevel("operations-floor");

            Assert.That(RequestTransition("plant-room", typeof(string)), Is.True);
            yield return WaitForLevel("plant-room");

            Assert.That(RequestTransition("lobby", typeof(string)), Is.True);
            yield return WaitForLevel("lobby");

            Assert.That(
                Enumerable.Range(0, SceneManager.sceneCount).Select(index => SceneManager.GetSceneAt(index).name),
                Is.EquivalentTo(new[] { "Bootstrap", "Lobby" }));
            Assert.That(
                Resources.FindObjectsOfTypeAll<Camera>()
                    .Count(camera => camera.gameObject.scene.IsValid() && camera.gameObject.activeInHierarchy),
                Is.EqualTo(1));
            Assert.That(
                Resources.FindObjectsOfTypeAll<AudioListener>()
                    .Count(listener => listener.gameObject.scene.IsValid() && listener.gameObject.activeInHierarchy),
                Is.EqualTo(1));
        }

        private IEnumerator WaitForLevel(string levelId)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetStateProperty<string>("CurrentLevelId") == levelId
                    && !GetStateProperty<bool>("IsTransitioning"))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for level '{levelId}'. Current phase: {GetStateProperty<object>("TransitionPhase")}");
        }

        private IEnumerator WaitForPhase(string phaseName)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetStateProperty<object>("TransitionPhase").ToString() == phaseName)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for phase '{phaseName}'.");
        }

        private bool RequestTransition(object destination, Type parameterType)
        {
            MethodInfo method = serviceType.GetMethod("RequestTransition", new[] { parameterType });
            return (bool)method.Invoke(service, new[] { destination });
        }

        private T GetStateProperty<T>(string propertyName)
        {
            return (T)appStateType.GetProperty(propertyName).GetValue(appState);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static Component FindRuntimeComponent(Type type)
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .First(component => type.IsInstanceOfType(component) && component.gameObject.scene.IsValid());
        }
    }
}

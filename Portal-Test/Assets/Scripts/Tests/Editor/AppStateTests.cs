using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FacilityViewer.Tests
{
    public sealed class AppStateTests
    {
        private const string AppStateScriptPath = "Assets/Scripts/Core/AppState.cs";

        private GameObject stateObject;
        private Component appState;
        private Type appStateType;
        private Type transitionPhaseType;

        [SetUp]
        public void SetUp()
        {
            MonoScript appStateScript = AssetDatabase.LoadAssetAtPath<MonoScript>(AppStateScriptPath);
            appStateType = appStateScript.GetClass();
            transitionPhaseType = appStateType.Assembly.GetType("FacilityViewer.Core.FacilityTransitionPhase");
            stateObject = new GameObject("App State Test");
            appState = stateObject.AddComponent(appStateType);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(stateObject);
        }

        [Test]
        public void InitializeCreatesAnObservableIdleState()
        {
            int changeCount = 0;
            EventInfo changedEvent = appStateType.GetEvent("Changed");
            Delegate handler = CreateChangedHandler(changedEvent.EventHandlerType, () => changeCount++);
            changedEvent.AddEventHandler(appState, handler);

            Invoke("Initialize", "Application services ready");

            Assert.That(GetProperty<bool>("IsInitialized"), Is.True);
            Assert.That(GetProperty<object>("TransitionPhase").ToString(), Is.EqualTo("Idle"));
            Assert.That(GetProperty<bool>("IsTransitioning"), Is.False);
            Assert.That(GetProperty<bool>("HasError"), Is.False);
            Assert.That(GetProperty<string>("StatusMessage"), Is.EqualTo("Application services ready"));
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [TestCase("Validating")]
        [TestCase("Loading")]
        [TestCase("Teleporting")]
        [TestCase("Unloading")]
        public void ActiveTransitionPhasesReportBusy(string phaseName)
        {
            Invoke("Initialize", "Application services ready");
            object phase = Enum.Parse(transitionPhaseType, phaseName);

            Invoke("SetTransitionPhase", phase, phaseName);

            Assert.That(GetProperty<bool>("IsTransitioning"), Is.True);
            Assert.That(GetProperty<string>("StatusMessage"), Is.EqualTo(phaseName));
        }

        [Test]
        public void FailureStatePreservesReadableMessage()
        {
            Invoke("Initialize", "Application services ready");

            Invoke("SetFailure", "Missing destination spawn");

            Assert.That(GetProperty<object>("TransitionPhase").ToString(), Is.EqualTo("Failed"));
            Assert.That(GetProperty<bool>("HasError"), Is.True);
            Assert.That(GetProperty<bool>("IsTransitioning"), Is.False);
            Assert.That(GetProperty<string>("StatusMessage"), Is.EqualTo("Missing destination spawn"));
        }

        private void Invoke(string methodName, params object[] arguments)
        {
            appStateType.GetMethod(methodName)?.Invoke(appState, arguments);
        }

        private T GetProperty<T>(string propertyName)
        {
            return (T)appStateType.GetProperty(propertyName)?.GetValue(appState);
        }

        private static Delegate CreateChangedHandler(Type handlerType, Action callback)
        {
            MethodInfo factory = typeof(AppStateTests)
                .GetMethod(nameof(CreateTypedChangedHandler), BindingFlags.Static | BindingFlags.NonPublic)
                ?.MakeGenericMethod(handlerType.GenericTypeArguments[0]);

            return (Delegate)factory?.Invoke(null, new object[] { callback });
        }

        private static Action<T> CreateTypedChangedHandler<T>(Action callback)
        {
            return _ => callback();
        }
    }
}

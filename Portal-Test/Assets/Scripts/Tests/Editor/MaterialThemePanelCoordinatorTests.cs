using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FacilityViewer.Tests
{
    public sealed class MaterialThemePanelCoordinatorTests
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        [Test]
        public void BootstrapWiresOneThemePanelCoordinatorToTheTypedPresentationAndServiceSeams()
        {
            Type appStateType = Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            Type serviceType = Type.GetType(
                "FacilityViewer.Services.MaterialThemeService, Assembly-CSharp");
            Type presenterType = Type.GetType(
                "FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            Type coordinatorType = Type.GetType(
                "FacilityViewer.UI.MaterialThemePanelCoordinator, Assembly-CSharp");

            Assert.That(appStateType, Is.Not.Null);
            Assert.That(serviceType, Is.Not.Null);
            Assert.That(presenterType, Is.Not.Null);
            Assert.That(coordinatorType, Is.Not.Null);

            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestorePreviousSetup = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                var scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                GameObject bootstrap = scene.GetRootGameObjects().Single(root => root.name == "Bootstrap");
                GameObject applicationUi = bootstrap.transform.Cast<Transform>()
                    .Single(child => child.name == "Application UI").gameObject;
                Component coordinator = applicationUi.GetComponent(coordinatorType);
                Component presenter = applicationUi.GetComponent(presenterType);
                Component service = bootstrap.GetComponent(serviceType);
                Component appState = bootstrap.GetComponent(appStateType);

                Assert.That(coordinator, Is.Not.Null);
                Assert.That(applicationUi.GetComponents(coordinatorType), Has.Length.EqualTo(1));
                Assert.That(presenter, Is.Not.Null);
                Assert.That(service, Is.Not.Null);
                Assert.That(appState, Is.Not.Null);

                SerializedObject serializedCoordinator = new(coordinator);
                Assert.That(
                    serializedCoordinator.FindProperty("panelPresenter").objectReferenceValue,
                    Is.EqualTo(presenter));
                Assert.That(
                    serializedCoordinator.FindProperty("materialThemeService").objectReferenceValue,
                    Is.EqualTo(service));
                Assert.That(
                    serializedCoordinator.FindProperty("appState").objectReferenceValue,
                    Is.EqualTo(appState));
            }
            finally
            {
                if (canRestorePreviousSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class FacilityControlPanelRegressionTests
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

        [UnityTest]
        public IEnumerator OpenPanelClassResolvesToFlexDisplay()
        {
            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(0.5f);

            Type presenterType = Type.GetType("FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            Type coordinatorType = Type.GetType("FacilityViewer.Player.PlayerInputCoordinator, Assembly-CSharp");
            Component presenter = FindRuntimeComponent(presenterType);
            Component coordinator = FindRuntimeComponent(coordinatorType);
            UIDocument document = presenter.GetComponent<UIDocument>();
            VisualElement facilityPanel = document.rootVisualElement.Q<VisualElement>("facility-panel");

            coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.That(facilityPanel.ClassListContains("facility-panel--open"), Is.True);
            Debug.Log($"[Panel Regression] openClass={facilityPanel.ClassListContains("facility-panel--open")} display={facilityPanel.resolvedStyle.display}");
            Assert.That(facilityPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator ResponsiveRuntimeVisualTreePreservesPanelAndMobileReturnClearance()
        {
            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(0.5f);

            Type presenterType = Type.GetType("FacilityViewer.UI.FacilityControlPanelPresenter, Assembly-CSharp");
            Type coordinatorType = Type.GetType("FacilityViewer.Player.PlayerInputCoordinator, Assembly-CSharp");
            Type mobilePresenterType = Type.GetType("FacilityViewer.UI.MobileControlsPresenter, Assembly-CSharp");
            Type appStateType = Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
            Type inputModeOverrideType = Type.GetType("FacilityViewer.Player.PlayerInputModeOverride, Assembly-CSharp");
            Component presenter = FindRuntimeComponent(presenterType);
            Component coordinator = FindRuntimeComponent(coordinatorType);
            Component mobilePresenter = FindRuntimeComponent(mobilePresenterType);
            Component appState = FindRuntimeComponent(appStateType);
            UIDocument panelDocument = presenter.GetComponent<UIDocument>();
            UIDocument mobileDocument = mobilePresenter.GetComponent<UIDocument>();
            VisualElement panelRoot = panelDocument.rootVisualElement;
            VisualElement mobileRoot = mobileDocument.rootVisualElement;
            VisualElement appShell = panelRoot.Q<VisualElement>("app-shell");
            VisualElement appSafeArea = panelRoot.Q<VisualElement>("app-safe-area");
            VisualElement facilityPanel = panelRoot.Q<VisualElement>("facility-panel");
            VisualElement loadingOverlay = panelRoot.Q<VisualElement>("loading-overlay");
            Button lobbyButton = panelRoot.Q<Button>("level-lobby-button");
            Button returnButton = mobileRoot.Q<Button>("panel-button");

            Assert.That(appShell, Is.Not.Null);
            Assert.That(appSafeArea, Is.Not.Null);
            Assert.That(facilityPanel, Is.Not.Null);
            Assert.That(loadingOverlay, Is.Not.Null);
            Assert.That(lobbyButton, Is.Not.Null);
            Assert.That(returnButton, Is.Not.Null);
            Assert.That(facilityPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            coordinatorType.GetMethod("SetInputModeOverride").Invoke(
                coordinator,
                new[] { Enum.Parse(inputModeOverrideType, "Mobile") });
            coordinatorType.GetMethod("SetUiInputActive").Invoke(coordinator, new object[] { true });
            yield return null;

            Assert.That(facilityPanel.ClassListContains("facility-panel--open"), Is.True);
            Assert.That(facilityPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(returnButton.text, Is.EqualTo("RETURN"));
            Assert.That(returnButton.resolvedStyle.height, Is.GreaterThanOrEqualTo(56f));
            Assert.That(lobbyButton.resolvedStyle.height, Is.GreaterThanOrEqualTo(56f));

            Type layoutType = Type.GetType("FacilityViewer.UI.FacilityControlPanelLayout, Assembly-CSharp");
            Type viewType = Type.GetType("FacilityViewer.UI.FacilityControlPanelView, Assembly-CSharp");
            object panelView = presenterType.GetField("panelView", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(presenter);
            MethodInfo isCompactLandscape = layoutType.GetMethod(
                "IsCompactLandscape",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo presentLayoutMode = viewType.GetMethod("PresentLayoutMode");
            Assert.That(panelView, Is.Not.Null);
            Assert.That(isCompactLandscape, Is.Not.Null);
            Assert.That(presentLayoutMode, Is.Not.Null);

            foreach ((int width, int height, bool compact) in new[]
            {
                (1920, 1080, false),
                (1024, 768, true),
                (2340, 1080, false)
            })
            {
                bool mappedCompact = (bool)isCompactLandscape.Invoke(
                    null,
                    new object[] { new Vector2(width, height) });
                Assert.That(mappedCompact, Is.EqualTo(compact));
                presentLayoutMode.Invoke(panelView, new object[] { mappedCompact });
                yield return null;

                Assert.That(panelRoot.contentRect.width, Is.GreaterThan(0f));
                Assert.That(panelRoot.contentRect.height, Is.GreaterThan(0f));
                Assert.That(appShell.ClassListContains("app-shell--compact"), Is.EqualTo(mappedCompact));
                Assert.That(appShell.ClassListContains("app-shell--wide"), Is.EqualTo(!mappedCompact));
                Assert.That(appSafeArea.worldBound.Overlaps(facilityPanel.worldBound), Is.True);
                Assert.That(facilityPanel.worldBound.yMax, Is.LessThanOrEqualTo(returnButton.worldBound.yMin));
            }

            Type phaseType = Type.GetType("FacilityViewer.Core.FacilityTransitionPhase, Assembly-CSharp");
            appStateType.GetMethod("SetTransitionPhase").Invoke(
                appState,
                new object[] { Enum.Parse(phaseType, "Loading"), "Loading responsive validation" });
            yield return null;
            Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.True);
            Assert.That(loadingOverlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(lobbyButton.ClassListContains("is-selected"), Is.True);

            appStateType.GetMethod("SetFailure").Invoke(appState, new object[] { "Responsive validation failure" });
            yield return null;
            Assert.That(appShell.ClassListContains("app-shell--state-error"), Is.True);
            Assert.That(loadingOverlay.ClassListContains("is-visible"), Is.False);
            Assert.That(loadingOverlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene cleanupScene = SceneManager.CreateScene($"PanelRegressionCleanup-{Guid.NewGuid():N}");
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

        private static Component FindRuntimeComponent(Type type)
        {
            Assert.That(type, Is.Not.Null);
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .First(component => type.IsInstanceOfType(component) && component.gameObject.scene.IsValid());
        }
    }
}

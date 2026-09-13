using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.Tests
{
    public sealed class MobileControlsAssetTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const string DocumentPath = "Assets/UI/Documents/MobileControls.uxml";
        private const string StylePath = "Assets/UI/Styles/MobileControls.uss";
        private const string PanelSettingsPath = "Assets/Settings/RuntimePanelSettings.asset";

        [Test]
        public void MobileControlsAssetsAndPrefabWiringAreComplete()
        {
            VisualTreeAsset document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            Assert.That(document, Is.Not.Null);
            Assert.That(style, Is.Not.Null);
            Assert.That(panelSettings, Is.Not.Null);
            Assert.That(AssetDatabase.GetDependencies(DocumentPath), Does.Contain(StylePath));

            VisualElement clonedRoot = document.CloneTree();

            Assert.That(clonedRoot.Q<VisualElement>("mobile-controls-root"), Is.Not.Null);
            Assert.That(clonedRoot.Q<VisualElement>("mobile-safe-area"), Is.Not.Null);
            Assert.That(clonedRoot.Q<VisualElement>("move-stick"), Is.Not.Null);
            Assert.That(clonedRoot.Q<VisualElement>("move-knob"), Is.Not.Null);
            Assert.That(clonedRoot.Q<VisualElement>("look-region"), Is.Not.Null);
            Assert.That(clonedRoot.Q<Button>("interact-button"), Is.Not.Null);
            Assert.That(clonedRoot.Q<Button>("panel-button"), Is.Not.Null);

            Transform mobileControls = player.transform.Find("MobileControls");
            Component router = player.GetComponent("PlayerInputRouter");
            Component coordinator = player.GetComponent("PlayerInputCoordinator");
            UIDocument uiDocument = mobileControls?.GetComponent<UIDocument>();
            Component presenter = mobileControls?.GetComponent("MobileControlsPresenter");

            Assert.That(mobileControls, Is.Not.Null);
            Assert.That(coordinator, Is.Not.Null);
            Assert.That(uiDocument, Is.Not.Null);
            Assert.That(presenter, Is.Not.Null);
            Assert.That(player.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(uiDocument.panelSettings, Is.EqualTo(panelSettings));
            Assert.That(uiDocument.visualTreeAsset, Is.EqualTo(document));
            Assert.That(uiDocument.sortingOrder, Is.EqualTo(10f));

            SerializedObject serializedPresenter = new(presenter);

            Assert.That(
                serializedPresenter.FindProperty("uiDocument").objectReferenceValue,
                Is.EqualTo(uiDocument));
            Assert.That(
                serializedPresenter.FindProperty("inputRouter").objectReferenceValue,
                Is.EqualTo(router));
            Assert.That(serializedPresenter.FindProperty("touchLookScale").floatValue, Is.EqualTo(0.5f));

            SerializedObject serializedCoordinator = new(coordinator);

            Assert.That(
                serializedCoordinator.FindProperty("inputRouter").objectReferenceValue,
                Is.EqualTo(router));
            Assert.That(
                serializedCoordinator.FindProperty("inputModeOverride").enumDisplayNames[
                    serializedCoordinator.FindProperty("inputModeOverride").enumValueIndex],
                Is.EqualTo("Automatic"));
            Assert.That(serializedCoordinator.FindProperty("lockCursorInDesktopGameplay").boolValue, Is.True);

            TypeInfo routerType = router.GetType().GetTypeInfo();

            Assert.That(routerType.GetMethod("SetMobileMoveInput"), Is.Not.Null);
            Assert.That(routerType.GetMethod("AddMobileLookDelta"), Is.Not.Null);
            Assert.That(routerType.GetMethod("ConsumeLookInput"), Is.Not.Null);
            Assert.That(routerType.GetMethod("SetInputMode"), Is.Not.Null);
            Assert.That(routerType.GetMethod("SetInputOwner"), Is.Not.Null);
            Assert.That(routerType.GetMethod("RequestMobileInteract"), Is.Not.Null);
            Assert.That(routerType.GetMethod("RequestMobileTogglePanel"), Is.Not.Null);
            Assert.That(routerType.GetMethod("ClearMobileInput"), Is.Not.Null);
            Assert.That(routerType.GetMethod("ClearContinuousInput"), Is.Not.Null);
            Assert.That(routerType.GetEvent("InputModeChanged"), Is.Not.Null);
            Assert.That(routerType.GetEvent("InputOwnerChanged"), Is.Not.Null);

            TypeInfo coordinatorType = coordinator.GetType().GetTypeInfo();

            Assert.That(coordinatorType.GetMethod("SetInputModeOverride"), Is.Not.Null);
            Assert.That(coordinatorType.GetMethod("SetUiInputActive"), Is.Not.Null);
            Assert.That(coordinatorType.GetProperty("WantsLockedCursor"), Is.Not.Null);
        }

        [Test]
        public void AutomaticInputModeUsesPlatformWhileExplicitOverridesRemainDeterministic()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Component coordinator = player.GetComponent("PlayerInputCoordinator");
            MethodInfo resolve = coordinator.GetType().GetMethod(
                "ResolveInputMode",
                BindingFlags.Static | BindingFlags.NonPublic);
            Type overrideType = resolve.GetParameters()[0].ParameterType;

            object automatic = Enum.Parse(overrideType, "Automatic");
            object desktop = Enum.Parse(overrideType, "Desktop");
            object mobile = Enum.Parse(overrideType, "Mobile");

            Assert.That(resolve.Invoke(null, new[] { automatic, (object)false }).ToString(), Is.EqualTo("Desktop"));
            Assert.That(resolve.Invoke(null, new[] { automatic, (object)true }).ToString(), Is.EqualTo("Mobile"));
            Assert.That(resolve.Invoke(null, new[] { desktop, (object)true }).ToString(), Is.EqualTo("Desktop"));
            Assert.That(resolve.Invoke(null, new[] { mobile, (object)false }).ToString(), Is.EqualTo("Mobile"));
        }

        [Test]
        public void SafeAreaConversionMapsBottomLeftScreenCoordinatesToTopLeftPanelCoordinates()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Component presenter = player.transform.Find("MobileControls").GetComponent("MobileControlsPresenter");
            MethodInfo calculate = presenter.GetType().GetMethod(
                "CalculatePanelSafeArea",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(calculate, Is.Not.Null);

            Rect result = (Rect)calculate.Invoke(
                null,
                new object[]
                {
                    new Rect(80f, 40f, 2240f, 1000f),
                    new Vector2(2400f, 1080f),
                    new Vector2(1920f, 864f)
                });

            Assert.That(result.x, Is.EqualTo(64f).Within(0.001f));
            Assert.That(result.y, Is.EqualTo(32f).Within(0.001f));
            Assert.That(result.width, Is.EqualTo(1792f).Within(0.001f));
            Assert.That(result.height, Is.EqualTo(800f).Within(0.001f));
        }
    }
}

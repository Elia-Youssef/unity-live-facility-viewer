using System;
using FacilityViewer.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MobileControlsPresenter : MonoBehaviour
    {
        private const string SafeAreaName = "mobile-safe-area";
        private const string ControlsRootName = "mobile-controls-root";
        private const string MoveClusterName = "move-cluster";
        private const string MoveStickName = "move-stick";
        private const string MoveKnobName = "move-knob";
        private const string LookRegionName = "look-region";
        private const string InteractButtonName = "interact-button";
        private const string PanelButtonName = "panel-button";

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField, Min(0f)] private float touchLookScale = 0.5f;

        private VisualElement root;
        private VisualElement controlsRoot;
        private VisualElement safeArea;
        private VisualElement moveCluster;
        private VisualElement moveStick;
        private VisualElement lookRegion;
        private Button interactButton;
        private Button panelButton;
        private VirtualJoystickBinding joystickBinding;
        private TouchLookBinding lookBinding;

        public bool IsReady { get; private set; }
        public bool AreControlsVisible { get; private set; }
        public bool AreGameplayControlsVisible { get; private set; }
        public int ActiveMovePointerId => joystickBinding?.ActivePointerId ?? PointerId.invalidPointerId;
        public int ActiveLookPointerId => lookBinding?.ActivePointerId ?? PointerId.invalidPointerId;
        public Rect AppliedSafeArea { get; private set; }

        private void Reset()
        {
            uiDocument = GetComponent<UIDocument>();
            inputRouter = GetComponentInParent<PlayerInputRouter>();
        }

        private void OnEnable()
        {
            uiDocument ??= GetComponent<UIDocument>();
            inputRouter ??= GetComponentInParent<PlayerInputRouter>();

            if (uiDocument == null || inputRouter == null)
            {
                Debug.LogError("[MobileControls] A UIDocument and PlayerInputRouter are required.", this);
                enabled = false;
                return;
            }

            root = uiDocument.rootVisualElement;
            controlsRoot = root.Q<VisualElement>(ControlsRootName);
            safeArea = root.Q<VisualElement>(SafeAreaName);
            moveCluster = root.Q<VisualElement>(MoveClusterName);
            moveStick = root.Q<VisualElement>(MoveStickName);
            VisualElement moveKnob = root.Q<VisualElement>(MoveKnobName);
            lookRegion = root.Q<VisualElement>(LookRegionName);
            interactButton = root.Q<Button>(InteractButtonName);
            panelButton = root.Q<Button>(PanelButtonName);

            if (controlsRoot == null
                || safeArea == null
                || moveCluster == null
                || moveStick == null
                || moveKnob == null
                || lookRegion == null
                || interactButton == null
                || panelButton == null)
            {
                Debug.LogError("[MobileControls] Required UI Toolkit elements are missing from MobileControls.", this);
                enabled = false;
                return;
            }

            root.pickingMode = PickingMode.Ignore;
            safeArea.pickingMode = PickingMode.Ignore;
            controlsRoot.pickingMode = PickingMode.Ignore;
            joystickBinding = new VirtualJoystickBinding(moveStick, moveKnob, inputRouter.SetMobileMoveInput);
            lookBinding = new TouchLookBinding(
                lookRegion,
                delta => inputRouter.AddMobileLookDelta(delta * touchLookScale));

            interactButton.clicked += inputRouter.RequestMobileInteract;
            panelButton.clicked += inputRouter.RequestMobileTogglePanel;
            inputRouter.InputModeChanged += OnInputModeChanged;
            inputRouter.InputOwnerChanged += OnInputOwnerChanged;
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            Application.focusChanged += OnApplicationFocusChanged;

            IsReady = true;
            RefreshControlVisibility();
            RefreshSafeArea();
        }

        private void OnDisable()
        {
            Application.focusChanged -= OnApplicationFocusChanged;

            if (root != null)
            {
                root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            }

            if (interactButton != null && inputRouter != null)
            {
                interactButton.clicked -= inputRouter.RequestMobileInteract;
            }

            if (panelButton != null && inputRouter != null)
            {
                panelButton.clicked -= inputRouter.RequestMobileTogglePanel;
            }

            if (inputRouter != null)
            {
                inputRouter.InputModeChanged -= OnInputModeChanged;
                inputRouter.InputOwnerChanged -= OnInputOwnerChanged;
            }

            joystickBinding?.Dispose();
            lookBinding?.Dispose();
            inputRouter?.ClearMobileInput();

            joystickBinding = null;
            lookBinding = null;
            IsReady = false;
            AreControlsVisible = false;
            AreGameplayControlsVisible = false;
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                CancelActivePointers();
            }
        }

        public void RefreshSafeArea()
        {
            if (root == null || safeArea == null)
            {
                return;
            }

            Vector2 panelSize = new(root.resolvedStyle.width, root.resolvedStyle.height);

            if (panelSize.x <= 0f || panelSize.y <= 0f || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            AppliedSafeArea = CalculatePanelSafeArea(
                Screen.safeArea,
                new Vector2(Screen.width, Screen.height),
                panelSize);
            safeArea.style.left = AppliedSafeArea.xMin;
            safeArea.style.top = AppliedSafeArea.yMin;
            safeArea.style.width = AppliedSafeArea.width;
            safeArea.style.height = AppliedSafeArea.height;
        }

        internal static Rect CalculatePanelSafeArea(Rect screenSafeArea, Vector2 screenSize, Vector2 panelSize)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f || panelSize.x <= 0f || panelSize.y <= 0f)
            {
                return new Rect(Vector2.zero, panelSize);
            }

            float scaleX = panelSize.x / screenSize.x;
            float scaleY = panelSize.y / screenSize.y;
            float left = screenSafeArea.xMin * scaleX;
            float top = (screenSize.y - screenSafeArea.yMax) * scaleY;
            float width = screenSafeArea.width * scaleX;
            float height = screenSafeArea.height * scaleY;
            return new Rect(left, top, width, height);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            RefreshSafeArea();
        }

        private void OnInputModeChanged(PlayerInputMode inputMode)
        {
            RefreshControlVisibility();
        }

        private void OnInputOwnerChanged(PlayerInputOwner inputOwner)
        {
            RefreshControlVisibility();
        }

        private void OnApplicationFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                CancelActivePointers();
            }
        }

        private void CancelActivePointers()
        {
            joystickBinding?.Cancel();
            lookBinding?.Cancel();
            inputRouter?.ClearMobileInput();
        }

        private void RefreshControlVisibility()
        {
            if (controlsRoot == null || inputRouter == null)
            {
                return;
            }

            bool showMobileControls = inputRouter.InputMode == PlayerInputMode.Mobile;
            bool showGameplayControls =
                showMobileControls && inputRouter.InputOwner == PlayerInputOwner.Gameplay;

            controlsRoot.style.display = showMobileControls ? DisplayStyle.Flex : DisplayStyle.None;
            moveCluster.style.display = showGameplayControls ? DisplayStyle.Flex : DisplayStyle.None;
            lookRegion.style.display = showGameplayControls ? DisplayStyle.Flex : DisplayStyle.None;
            interactButton.style.display = showGameplayControls ? DisplayStyle.Flex : DisplayStyle.None;
            panelButton.text = showGameplayControls ? "PANEL" : "RETURN";

            AreControlsVisible = showMobileControls;
            AreGameplayControlsVisible = showGameplayControls;

            if (!showGameplayControls)
            {
                CancelActivePointers();
            }
        }

        private sealed class VirtualJoystickBinding : IDisposable
        {
            private const float DeadZone = 0.08f;

            private readonly VisualElement surface;
            private readonly VisualElement knob;
            private readonly Action<Vector2> valueChanged;

            public int ActivePointerId { get; private set; } = PointerId.invalidPointerId;

            public VirtualJoystickBinding(
                VisualElement surface,
                VisualElement knob,
                Action<Vector2> valueChanged)
            {
                this.surface = surface;
                this.knob = knob;
                this.valueChanged = valueChanged;

                surface.RegisterCallback<PointerDownEvent>(OnPointerDown);
                surface.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                surface.RegisterCallback<PointerUpEvent>(OnPointerUp);
                surface.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
                surface.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            public void Dispose()
            {
                Cancel();
                surface.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                surface.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                surface.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                surface.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
                surface.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            public void Cancel()
            {
                if (ActivePointerId == PointerId.invalidPointerId)
                {
                    return;
                }

                int pointerId = ActivePointerId;
                ActivePointerId = PointerId.invalidPointerId;

                if (surface.HasPointerCapture(pointerId))
                {
                    surface.ReleasePointer(pointerId);
                }

                ResetValue();
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (ActivePointerId != PointerId.invalidPointerId || evt.button != 0)
                {
                    return;
                }

                ActivePointerId = evt.pointerId;
                surface.CapturePointer(evt.pointerId);
                surface.AddToClassList("is-active");
                UpdateValue(evt.localPosition);
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != ActivePointerId)
                {
                    return;
                }

                UpdateValue(evt.localPosition);
                evt.StopPropagation();
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                EndPointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                EndPointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId != ActivePointerId)
                {
                    return;
                }

                ActivePointerId = PointerId.invalidPointerId;
                ResetValue();
            }

            private void EndPointer(int pointerId)
            {
                if (pointerId != ActivePointerId)
                {
                    return;
                }

                ActivePointerId = PointerId.invalidPointerId;

                if (surface.HasPointerCapture(pointerId))
                {
                    surface.ReleasePointer(pointerId);
                }

                ResetValue();
            }

            private void UpdateValue(Vector2 localPosition)
            {
                Rect bounds = surface.contentRect;
                Vector2 center = bounds.center;
                float knobRadius = knob.resolvedStyle.width * 0.5f;
                float radius = Mathf.Max(1f, Mathf.Min(bounds.width, bounds.height) * 0.5f - knobRadius);
                Vector2 offset = Vector2.ClampMagnitude(localPosition - center, radius);
                Vector2 normalized = new(offset.x / radius, -offset.y / radius);

                if (normalized.sqrMagnitude < DeadZone * DeadZone)
                {
                    normalized = Vector2.zero;
                }

                knob.style.translate = new Translate(new Length(offset.x), new Length(offset.y));
                valueChanged(normalized);
            }

            private void ResetValue()
            {
                surface.RemoveFromClassList("is-active");
                knob.style.translate = new Translate(0f, 0f);
                valueChanged(Vector2.zero);
            }
        }

        private sealed class TouchLookBinding : IDisposable
        {
            private readonly VisualElement surface;
            private readonly Action<Vector2> deltaChanged;
            private Vector2 previousPosition;

            public int ActivePointerId { get; private set; } = PointerId.invalidPointerId;

            public TouchLookBinding(VisualElement surface, Action<Vector2> deltaChanged)
            {
                this.surface = surface;
                this.deltaChanged = deltaChanged;

                surface.RegisterCallback<PointerDownEvent>(OnPointerDown);
                surface.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                surface.RegisterCallback<PointerUpEvent>(OnPointerUp);
                surface.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
                surface.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            public void Dispose()
            {
                Cancel();
                surface.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                surface.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                surface.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                surface.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
                surface.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            public void Cancel()
            {
                if (ActivePointerId == PointerId.invalidPointerId)
                {
                    return;
                }

                int pointerId = ActivePointerId;
                ActivePointerId = PointerId.invalidPointerId;

                if (surface.HasPointerCapture(pointerId))
                {
                    surface.ReleasePointer(pointerId);
                }

                surface.RemoveFromClassList("is-active");
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (ActivePointerId != PointerId.invalidPointerId || evt.button != 0)
                {
                    return;
                }

                ActivePointerId = evt.pointerId;
                previousPosition = evt.position;
                surface.CapturePointer(evt.pointerId);
                surface.AddToClassList("is-active");
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != ActivePointerId)
                {
                    return;
                }

                Vector2 currentPosition = evt.position;
                Vector2 pointerDelta = currentPosition - previousPosition;
                previousPosition = currentPosition;
                deltaChanged(new Vector2(pointerDelta.x, -pointerDelta.y));
                evt.StopPropagation();
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                EndPointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                EndPointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId != ActivePointerId)
                {
                    return;
                }

                ActivePointerId = PointerId.invalidPointerId;
                surface.RemoveFromClassList("is-active");
            }

            private void EndPointer(int pointerId)
            {
                if (pointerId != ActivePointerId)
                {
                    return;
                }

                ActivePointerId = PointerId.invalidPointerId;

                if (surface.HasPointerCapture(pointerId))
                {
                    surface.ReleasePointer(pointerId);
                }

                surface.RemoveFromClassList("is-active");
            }
        }
    }
}

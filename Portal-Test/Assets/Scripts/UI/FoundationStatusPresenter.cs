using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FacilityViewer.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FoundationStatusPresenter : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private string[] requiredScenePaths =
        {
            "Assets/Scenes/Bootstrap.unity"
        };

        private void Reset()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            uiDocument ??= GetComponent<UIDocument>();

            VisualElement root = uiDocument.rootVisualElement;
            Label titleLabel = root.Q<Label>("title-label");
            Label statusLabel = root.Q<Label>("status-label");

            if (titleLabel == null || statusLabel == null)
            {
                Debug.LogError("[Foundation] Required UI labels are missing from BootstrapShell.", this);
                enabled = false;
                return;
            }

            titleLabel.text = Application.productName;

            for (int i = 0; i < requiredScenePaths.Length; i++)
            {
                string scenePath = requiredScenePaths[i];
                if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
                {
                    statusLabel.text = $"Configuration error: {scenePath} is not in the build.";
                    Debug.LogError($"[Foundation] Required scene is not enabled in the build: {scenePath}", this);
                    return;
                }
            }

            statusLabel.text = "Project foundation ready";
            Debug.Log(
                $"[Foundation] UI Toolkit shell and required scenes validated. Development build: {Debug.isDebugBuild}.",
                this);
        }
    }
}

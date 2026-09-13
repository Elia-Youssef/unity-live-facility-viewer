using UnityEngine;

namespace FacilityViewer.Core
{
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField] private string levelId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string scenePath = string.Empty;
        [SerializeField] private string spawnPointId = string.Empty;

        public string LevelId => levelId;
        public string DisplayName => displayName;
        public string ScenePath => scenePath;
        public string SpawnPointId => spawnPointId;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(levelId))
            {
                error = "Level ID is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                error = $"Level '{levelId}' has no display name.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.EndsWith(".unity"))
            {
                error = $"Level '{levelId}' has an invalid scene path.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(spawnPointId))
            {
                error = $"Level '{levelId}' has no destination spawn ID.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            levelId = levelId?.Trim() ?? string.Empty;
            displayName = displayName?.Trim() ?? string.Empty;
            scenePath = scenePath?.Trim() ?? string.Empty;
            spawnPointId = spawnPointId?.Trim() ?? string.Empty;
        }
    }
}

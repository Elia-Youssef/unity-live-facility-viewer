using System.Text;
using UnityEngine;

namespace FacilityViewer.UI
{
    internal static class FacilityControlPanelLayout
    {
        internal static string BuildLevelButtonName(string levelId)
        {
            StringBuilder normalizedId = new();

            foreach (char character in levelId ?? string.Empty)
            {
                if (char.IsLetterOrDigit(character))
                {
                    normalizedId.Append(char.ToLowerInvariant(character));
                }
                else if (normalizedId.Length > 0 && normalizedId[normalizedId.Length - 1] != '-')
                {
                    normalizedId.Append('-');
                }
            }

            string stableId = normalizedId.ToString().Trim('-');
            return string.IsNullOrEmpty(stableId) ? "level-button" : $"level-{stableId}-button";
        }

        internal static bool IsCompactLandscape(Vector2 panelSize)
        {
            if (panelSize.x <= 0f || panelSize.y <= 0f)
            {
                return false;
            }

            return panelSize.x < 1200f || panelSize.x / panelSize.y < 1.5f;
        }

        internal static Rect CalculateSafeArea(Rect screenSafeArea, Vector2 screenSize, Vector2 panelSize)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f || panelSize.x <= 0f || panelSize.y <= 0f)
            {
                return new Rect(Vector2.zero, panelSize);
            }

            float horizontalScale = panelSize.x / screenSize.x;
            float verticalScale = panelSize.y / screenSize.y;
            float left = screenSafeArea.xMin * horizontalScale;
            float right = (screenSize.x - screenSafeArea.xMax) * horizontalScale;
            float bottom = screenSafeArea.yMin * verticalScale;
            float top = (screenSize.y - screenSafeArea.yMax) * verticalScale;

            return Rect.MinMaxRect(left, bottom, panelSize.x - right, panelSize.y - top);
        }
    }
}

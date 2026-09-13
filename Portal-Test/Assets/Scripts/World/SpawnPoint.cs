using UnityEngine;

namespace FacilityViewer.World
{
    [DisallowMultipleComponent]
    public sealed class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private string spawnPointId = "entrance";

        public string SpawnPointId => spawnPointId;

        private void OnValidate()
        {
            spawnPointId = spawnPointId?.Trim() ?? string.Empty;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.15f, 0.3f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.25f);
        }
    }
}

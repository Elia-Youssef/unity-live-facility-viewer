using UnityEngine;

namespace FacilityViewer.Sandbox
{
    [RequireComponent(typeof(Collider))]
    public sealed class SandboxTriggerReporter : MonoBehaviour
    {
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[Sandbox] {other.name} entered {name}.", this);
        }

        private void OnTriggerExit(Collider other)
        {
            Debug.Log($"[Sandbox] {other.name} exited {name}.", this);
        }
    }
}

namespace FacilityViewer.Core
{
    public enum FacilityTransitionPhase
    {
        Uninitialized,
        Idle,
        Validating,
        Loading,
        Teleporting,
        Unloading,
        Complete,
        Failed
    }
}

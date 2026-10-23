using UnityEngine;

[DisallowMultipleComponent]
public sealed class PitStopZone : MonoBehaviour
{
    private PitStopManager manager;
    private bool isExit;

    public void Configure(PitStopManager pitStopManager, bool exitZone)
    {
        manager = pitStopManager;
        isExit = exitZone;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager == null)
        {
            return;
        }

        ArcadeCarController car = other.GetComponentInParent<ArcadeCarController>();
        if (car != null)
        {
            manager.OnZoneEntered(car, isExit);
        }
    }
}

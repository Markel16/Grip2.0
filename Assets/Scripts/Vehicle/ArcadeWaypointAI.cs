using UnityEngine;

[DisallowMultipleComponent]
public sealed class ArcadeWaypointAI : MonoBehaviour
{
    [Header("Route")]
    [SerializeField] private ArcadeCarController carController;
    [SerializeField] private Transform[] waypoints = System.Array.Empty<Transform>();
    [SerializeField] private int startingWaypointIndex;
    [SerializeField] private bool loopRoute = true;
    [SerializeField, Min(0.1f)] private float waypointReachDistance = 5f;

    [Header("Driver Stats")]
    [SerializeField, Range(0f, 1f)] private float baseDifficulty = 0.65f;
    [SerializeField, Range(0f, 0.5f)] private float randomDifficultyVariation = 0.12f;
    [SerializeField, Min(1f)] private float cruiseSpeedKph = 100f;
    [SerializeField, Range(0f, 1f)] private float cornerSpeedReduction = 0.55f;
    [SerializeField, Min(1f)] private float maximumSteerAngle = 60f;
    [SerializeField, Range(0f, 30f)] private float steeringErrorAtLowDifficulty = 10f;
    [SerializeField, Min(0f)] private float steeringNoiseFrequency = 1.3f;

    private int targetWaypointIndex;
    private float effectiveDifficulty;
    private float steeringNoiseOffset;
    private bool pitStopHeld;
    private bool approachingPitEntry;
    private Transform pitTarget;
    private PitStopManager pitStopManager;

    public float EffectiveDifficulty => effectiveDifficulty;
    public int TargetWaypointIndex => targetWaypointIndex;

    private void Awake()
    {
        if (carController == null)
        {
            carController = GetComponent<ArcadeCarController>();
        }

        if (carController == null)
        {
            Debug.LogError("ArcadeWaypointAI needs an ArcadeCarController on the same car.", this);
            enabled = false;
            return;
        }

        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("Assign the track waypoints in the Inspector before enabling this AI.", this);
            carController.SetExternalInputMode(true);
            enabled = false;
            return;
        }

        effectiveDifficulty = Mathf.Clamp01(
            baseDifficulty + Random.Range(-randomDifficultyVariation, randomDifficultyVariation));
        steeringNoiseOffset = Random.Range(0f, 100f);
        targetWaypointIndex = Mathf.Clamp(startingWaypointIndex, 0, waypoints.Length - 1);
        carController.SetExternalInputMode(true);
    }

    private void FixedUpdate()
    {
        if (pitStopHeld)
        {
            carController.SetExternalDriveInput(0f, 0f, 1f, 1f);
            return;
        }

        if (carController == null || waypoints == null || waypoints.Length == 0)
        {
            return;
        }

        Transform target = pitTarget != null ? pitTarget : waypoints[targetWaypointIndex];
        if (target == null)
        {
            return;
        }

        Vector3 directionToTarget = target.position - transform.position;
        directionToTarget.y = 0f;

        if (directionToTarget.sqrMagnitude <= waypointReachDistance * waypointReachDistance)
        {
            if (pitTarget != null)
            {
                if (approachingPitEntry)
                {
                    approachingPitEntry = false;
                    if (pitStopManager == null
                        || !pitStopManager.OnAIReachedPit(this, carController))
                    {
                        if (pitStopManager != null)
                        {
                            pitStopManager.CancelAIPitApproach(carController);
                        }

                        pitTarget = null;
                    }
                }
                else
                {
                    pitTarget = null;
                }

                return;
            }

            AdvanceWaypoint();
            target = waypoints[targetWaypointIndex];
            if (target == null)
            {
                return;
            }

            directionToTarget = target.position - transform.position;
            directionToTarget.y = 0f;
        }

        if (directionToTarget.sqrMagnitude < 0.001f)
        {
            carController.SetExternalDriveInput(0f, 0f, 0f, 0f);
            return;
        }

        Vector3 vehicleForward = carController.WorldForward;
        vehicleForward.y = 0f;
        vehicleForward.Normalize();

        float turnAngle = Vector3.SignedAngle(vehicleForward, directionToTarget.normalized, Vector3.up);
        float steeringError = Mathf.Sin(Time.time * steeringNoiseFrequency + steeringNoiseOffset)
            * steeringErrorAtLowDifficulty
            * (1f - effectiveDifficulty);
        float steering = Mathf.Clamp((turnAngle + steeringError) / maximumSteerAngle, -1f, 1f);

        float turnAmount = Mathf.Clamp01(Mathf.Abs(turnAngle) / 90f);
        float skillSpeedFactor = Mathf.Lerp(0.6f, 1f, effectiveDifficulty);
        float cornerSpeedFactor = Mathf.Lerp(1f, 1f - cornerSpeedReduction, turnAmount);
        float targetSpeedKph = cruiseSpeedKph * skillSpeedFactor * cornerSpeedFactor;
        float currentSpeedKph = carController.CurrentSpeedKph;

        float throttle = currentSpeedKph < targetSpeedKph
            ? Mathf.Clamp01((targetSpeedKph - currentSpeedKph) / 18f)
            : 0f;
        float brake = currentSpeedKph > targetSpeedKph + 4f
            ? Mathf.Clamp01((currentSpeedKph - targetSpeedKph) / 16f)
            : 0f;

        carController.SetExternalDriveInput(steering, throttle, brake, 0f);
    }

    private void AdvanceWaypoint()
    {
        int nextIndex = targetWaypointIndex + 1;
        if (nextIndex >= waypoints.Length)
        {
            nextIndex = loopRoute ? 0 : waypoints.Length - 1;
        }

        targetWaypointIndex = nextIndex;
    }

    public void SetPitStopHold(bool held)
    {
        pitStopHeld = held;
        if (held && carController != null)
        {
            carController.SetExternalDriveInput(0f, 0f, 1f, 1f);
        }
    }

    public void DriveToPitEntry(PitStopManager manager, Transform entry)
    {
        pitStopManager = manager;
        pitTarget = entry;
        approachingPitEntry = true;
        pitStopHeld = false;
    }

    public void StopAtPitBox()
    {
        pitTarget = null;
        approachingPitEntry = false;
        SetPitStopHold(true);
    }

    public void DriveToPitExit(Transform exit)
    {
        pitTarget = exit;
        approachingPitEntry = false;
    }

    private void OnDisable()
    {
        if (carController != null)
        {
            carController.SetExternalDriveInput(0f, 0f, 0f, 0f);
        }
    }

    private void OnValidate()
    {
        waypointReachDistance = Mathf.Max(0.1f, waypointReachDistance);
        maximumSteerAngle = Mathf.Max(1f, maximumSteerAngle);
        cruiseSpeedKph = Mathf.Max(1f, cruiseSpeedKph);
    }
}
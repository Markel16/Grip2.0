using UnityEngine;

[DisallowMultipleComponent]
public sealed class ArcadeWaypointAI : MonoBehaviour
{
    [Header("Route")]
    [SerializeField] private ArcadeCarController carController;
    [Tooltip("Trazada generada con Grip > Generar trazada de la IA. Si está vacía se busca sola en la escena; si no hay ninguna se usan los waypoints.")]
    [SerializeField] private RacingLine racingLine;
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

    [Header("Racing Line Driving")]
    [Tooltip("Multiplica las velocidades de la trazada. 1 = al límite calculado.")]
    [SerializeField, Range(0.5f, 1.3f)] private float racingSpeedMultiplier = 1f;
    [Tooltip("Ritmo con dificultad 0 respecto al ritmo con dificultad 1.")]
    [SerializeField, Range(0.5f, 1f)] private float slowestPaceFactor = 0.82f;
    [SerializeField, Min(1f)] private float lookAheadMinimum = 6f;
    [SerializeField, Min(0f)] private float lookAheadPerKph = 0.08f;
    [Tooltip("Tiempo que tarda el piloto en reaccionar a la frenada que viene.")]
    [SerializeField, Min(0f)] private float reactionTime = 0.35f;
    [SerializeField, Min(0.1f)] private float wheelbase = 1.2f;
    [Tooltip("Ángulo máximo de las ruedas del coche (Maximum Steer Angle del ArcadeCarController).")]
    [SerializeField, Min(1f)] private float wheelSteerAngle = 30f;
    [SerializeField, Min(1f)] private float offTrackRecoverDistance = 15f;

    [Header("Traffic")]
    [SerializeField, Min(1f)] private float carDetectionDistance = 18f;
    [SerializeField, Min(0f)] private float overtakeOffset = 2.5f;
    [SerializeField, Min(1f)] private float followGap = 6f;

    [Header("Recovery")]
    [SerializeField, Min(0.1f)] private float stuckSeconds = 1.5f;
    [SerializeField, Min(0.1f)] private float reverseSeconds = 1.2f;

    private int targetWaypointIndex;
    private float effectiveDifficulty;
    private float steeringNoiseOffset;
    private bool pitStopHeld;
    private bool approachingPitEntry;
    private Transform pitTarget;
    private PitStopManager pitStopManager;
    private ArcadeCarController[] otherCars = System.Array.Empty<ArcadeCarController>();
    private int lineIndex;
    private bool needsLineReacquire = true;
    private float lateralOffset;
    private float lastSteering;
    private float lastThrottle;
    private float stuckTimer;
    private float reverseTimer;
    private float reverseSteering;

    public float EffectiveDifficulty => effectiveDifficulty;
    public int TargetWaypointIndex => UsesRacingLine ? lineIndex : targetWaypointIndex;

    private bool UsesRacingLine => racingLine != null && racingLine.Count >= 3;

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

        if (racingLine == null)
        {
            racingLine = FindAnyObjectByType<RacingLine>();
        }

        if (!UsesRacingLine && (waypoints == null || waypoints.Length == 0))
        {
            Debug.LogWarning("Genera la trazada (Grip > Generar trazada de la IA) o asigna waypoints antes de activar esta IA.", this);
            carController.SetExternalInputMode(true);
            enabled = false;
            return;
        }

        effectiveDifficulty = Mathf.Clamp01(
            baseDifficulty + Random.Range(-randomDifficultyVariation, randomDifficultyVariation));
        steeringNoiseOffset = Random.Range(0f, 100f);
        targetWaypointIndex = Mathf.Clamp(startingWaypointIndex, 0, Mathf.Max(0, waypoints.Length - 1));
        carController.SetExternalInputMode(true);
    }

    private void Start()
    {
        System.Collections.Generic.List<ArcadeCarController> cars = new System.Collections.Generic.List<ArcadeCarController>();
        foreach (ArcadeCarController car in FindObjectsByType<ArcadeCarController>())
        {
            if (car != carController)
            {
                cars.Add(car);
            }
        }

        otherCars = cars.ToArray();
    }

    private void FixedUpdate()
    {
        if (pitStopHeld)
        {
            carController.SetExternalDriveInput(0f, 0f, 1f, 1f);
            return;
        }

        if (carController == null || UpdateRecovery())
        {
            return;
        }

        if (pitTarget != null)
        {
            DriveToPitTarget();
        }
        else if (UsesRacingLine)
        {
            DriveRacingLine();
        }
        else
        {
            DriveWaypoints();
        }
    }

    private void DriveRacingLine()
    {
        Vector3 position = transform.position;
        if (needsLineReacquire)
        {
            lineIndex = racingLine.FindClosestIndex(position);
            needsLineReacquire = false;
        }
        else
        {
            lineIndex = racingLine.FindClosestIndex(position, lineIndex, 3, 12);
            if (FlatDistance(racingLine.GetPoint(lineIndex), position) > offTrackRecoverDistance)
            {
                lineIndex = racingLine.FindClosestIndex(position);
            }
        }

        float currentSpeedKph = carController.CurrentSpeedKph;
        float lookAhead = lookAheadMinimum + currentSpeedKph * lookAheadPerKph;
        int lookAheadIndex = lineIndex + Mathf.RoundToInt(lookAhead / racingLine.PointSpacing);

        float desiredOffset = ComputeTrafficOffset(currentSpeedKph, out float trafficSpeedCapKph);
        lateralOffset = Mathf.MoveTowards(lateralOffset, desiredOffset, 3f * Time.fixedDeltaTime);
        float clampedOffset = Mathf.Clamp(lateralOffset, -racingLine.GetLeftRoom(lookAheadIndex), racingLine.GetRightRoom(lookAheadIndex));
        Vector3 target = racingLine.GetPointAhead(lineIndex, lookAhead) + racingLine.GetRight(lookAheadIndex) * clampedOffset;

        // Mira la velocidad del tramo que tendrá delante cuando reaccione, no solo la del punto actual.
        float reactionDistance = currentSpeedKph / 3.6f * reactionTime;
        int reactionPoints = Mathf.CeilToInt(reactionDistance / racingLine.PointSpacing) + 1;
        float lineSpeedKph = float.MaxValue;
        for (int i = 0; i <= reactionPoints; i++)
        {
            lineSpeedKph = Mathf.Min(lineSpeedKph, racingLine.GetSpeedKph(lineIndex + i));
        }

        float paceFactor = Mathf.Lerp(slowestPaceFactor, 1f, effectiveDifficulty) * racingSpeedMultiplier;
        float targetSpeedKph = Mathf.Min(lineSpeedKph * paceFactor, trafficSpeedCapKph);
        ApplyDrive(PurePursuitSteering(target), targetSpeedKph);
    }

    // Si hay un coche lento delante en nuestra trazada, se abre hacia el lado con más hueco; si no cabe, se queda detrás.
    private float ComputeTrafficOffset(float currentSpeedKph, out float speedCapKph)
    {
        speedCapKph = float.MaxValue;
        Vector3 forward = Flatten(carController.WorldForward);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float offset = 0f;

        foreach (ArcadeCarController other in otherCars)
        {
            if (other == null || !other.isActiveAndEnabled)
            {
                continue;
            }

            Vector3 relative = other.transform.position - transform.position;
            relative.y = 0f;
            float ahead = Vector3.Dot(relative, forward);
            float side = Vector3.Dot(relative, right);
            if (ahead <= 0f || ahead > carDetectionDistance || Mathf.Abs(side) > 3.5f)
            {
                continue;
            }

            float otherSpeedKph = other.CurrentSpeedKph;
            bool closingIn = currentSpeedKph - otherSpeedKph > 5f || ahead < followGap * 2f;
            if (!closingIn)
            {
                continue;
            }

            float preferred = side >= 0f ? -overtakeOffset : overtakeOffset;
            if (HasRoom(preferred))
            {
                offset = preferred;
            }
            else if (HasRoom(-preferred))
            {
                offset = -preferred;
            }
            else
            {
                speedCapKph = Mathf.Min(speedCapKph, Mathf.Max(0f, otherSpeedKph - 2f));
            }

            if (ahead < followGap && Mathf.Abs(side) < 2f)
            {
                speedCapKph = Mathf.Min(speedCapKph, otherSpeedKph * 0.95f);
            }
        }

        return offset;
    }

    private bool HasRoom(float offset)
    {
        return offset >= 0f
            ? racingLine.GetRightRoom(lineIndex) >= offset
            : racingLine.GetLeftRoom(lineIndex) >= -offset;
    }

    private float PurePursuitSteering(Vector3 target)
    {
        Vector3 forward = Flatten(carController.WorldForward);
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        float alpha = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        float noise = Mathf.Sin(Time.time * steeringNoiseFrequency + steeringNoiseOffset)
            * steeringErrorAtLowDifficulty * 0.3f
            * (1f - effectiveDifficulty);

        if (Mathf.Abs(alpha) > 90f)
        {
            return Mathf.Sign(alpha);
        }

        float distance = Mathf.Max(toTarget.magnitude, 1f);
        float wheelAngle = Mathf.Atan(2f * wheelbase * Mathf.Sin(alpha * Mathf.Deg2Rad) / distance) * Mathf.Rad2Deg;
        return Mathf.Clamp((wheelAngle + noise) / wheelSteerAngle, -1f, 1f);
    }

    private void DriveToPitTarget()
    {
        Vector3 directionToTarget = pitTarget.position - transform.position;
        directionToTarget.y = 0f;
        if (directionToTarget.sqrMagnitude > waypointReachDistance * waypointReachDistance)
        {
            DriveTowards(directionToTarget);
            return;
        }

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
                needsLineReacquire = true;
            }
        }
        else
        {
            pitTarget = null;
            needsLineReacquire = true;
        }
    }

    private void DriveWaypoints()
    {
        Transform target = waypoints[targetWaypointIndex];
        if (target == null)
        {
            return;
        }

        Vector3 directionToTarget = target.position - transform.position;
        directionToTarget.y = 0f;

        if (directionToTarget.sqrMagnitude <= waypointReachDistance * waypointReachDistance)
        {
            AdvanceWaypoint();
            target = waypoints[targetWaypointIndex];
            if (target == null)
            {
                return;
            }

            directionToTarget = target.position - transform.position;
            directionToTarget.y = 0f;
        }

        DriveTowards(directionToTarget);
    }

    private void DriveTowards(Vector3 directionToTarget)
    {
        if (directionToTarget.sqrMagnitude < 0.001f)
        {
            carController.SetExternalDriveInput(0f, 0f, 0f, 0f);
            return;
        }

        Vector3 vehicleForward = Flatten(carController.WorldForward);
        float turnAngle = Vector3.SignedAngle(vehicleForward, directionToTarget.normalized, Vector3.up);
        float steeringError = Mathf.Sin(Time.time * steeringNoiseFrequency + steeringNoiseOffset)
            * steeringErrorAtLowDifficulty
            * (1f - effectiveDifficulty);
        float steering = Mathf.Clamp((turnAngle + steeringError) / maximumSteerAngle, -1f, 1f);

        float turnAmount = Mathf.Clamp01(Mathf.Abs(turnAngle) / 90f);
        float skillSpeedFactor = Mathf.Lerp(0.6f, 1f, effectiveDifficulty);
        float cornerSpeedFactor = Mathf.Lerp(1f, 1f - cornerSpeedReduction, turnAmount);
        ApplyDrive(steering, cruiseSpeedKph * skillSpeedFactor * cornerSpeedFactor);
    }

    private void ApplyDrive(float steering, float targetSpeedKph)
    {
        float currentSpeedKph = carController.CurrentSpeedKph;
        float throttle = currentSpeedKph < targetSpeedKph
            ? Mathf.Clamp01((targetSpeedKph - currentSpeedKph) / 18f)
            : 0f;
        float brake = currentSpeedKph > targetSpeedKph + 4f
            ? Mathf.Clamp01((currentSpeedKph - targetSpeedKph) / 16f)
            : 0f;

        lastSteering = steering;
        lastThrottle = throttle;
        carController.SetExternalDriveInput(steering, throttle, brake, 0f);
    }

    // Si acelera pero no avanza (contra una valla, por ejemplo), da marcha atrás con el volante al revés.
    private bool UpdateRecovery()
    {
        if (reverseTimer > 0f)
        {
            reverseTimer -= Time.fixedDeltaTime;
            carController.SetExternalDriveInput(reverseSteering, -1f, 0f, 0f);
            if (reverseTimer <= 0f)
            {
                needsLineReacquire = true;
                lastThrottle = 0f;
            }

            return true;
        }

        bool stuck = lastThrottle > 0.3f && carController.CurrentSpeedKph < 3f;
        stuckTimer = stuck ? stuckTimer + Time.fixedDeltaTime : 0f;
        if (stuckTimer < stuckSeconds)
        {
            return false;
        }

        stuckTimer = 0f;
        reverseTimer = reverseSeconds;
        reverseSteering = -Mathf.Sign(lastSteering);
        return true;
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
        stuckTimer = 0f;
        reverseTimer = 0f;
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

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
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

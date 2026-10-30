using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CarFollowCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private ArcadeCarController target;

    [Header("Offset")]
    [SerializeField, Min(0f)] private float distanceBehind = 7f;
    [SerializeField, Min(0f)] private float height = 3f;
    [SerializeField, Min(0f)] private float lookAheadDistance = 6f;
    [SerializeField] private float lookAtHeight = 0.5f;

    [Header("Heading")]
    [Tooltip("Por encima de esta velocidad la cámara se orienta según la dirección real de movimiento del coche.")]
    [SerializeField, Min(0f)] private float velocityHeadingMinSpeedKph = 5f;
    [SerializeField, Min(0f)] private float headingSmoothing = 4f;

    [Header("Smoothing")]
    [SerializeField, Min(0f)] private float positionSmoothing = 8f;
    [SerializeField, Min(0f)] private float rotationSmoothing = 10f;

    [Header("Speed Effects")]
    [Tooltip("Velocidad a la que los efectos llegan a su máximo.")]
    [SerializeField, Min(1f)] private float fullEffectSpeedKph = 160f;
    [SerializeField, Min(0f)] private float speedEffectSmoothing = 3f;
    [SerializeField, Range(1f, 179f)] private float baseFieldOfView = 60f;
    [SerializeField, Range(1f, 179f)] private float maximumFieldOfView = 78f;
    [Tooltip("Metros extra que la cámara se aleja a máxima velocidad.")]
    [SerializeField, Min(0f)] private float extraDistanceAtSpeed = 1.5f;
    [Tooltip("Metros que la cámara baja a máxima velocidad (más cerca del suelo = más sensación).")]
    [SerializeField, Min(0f)] private float heightDropAtSpeed = 0.5f;

    [Header("Shake")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.06f;
    [SerializeField, Min(0f)] private float shakeFrequency = 18f;
    [Tooltip("Por debajo de este porcentaje de velocidad no hay vibración.")]
    [SerializeField, Range(0f, 1f)] private float shakeStartSpeedFraction = 0.4f;
    [Tooltip("Vibración extra al ir por superficies rugosas como la hierba.")]
    [SerializeField, Min(0f)] private float surfaceShakeAmplitude = 0.15f;
    [SerializeField, Min(0f)] private float surfaceShakeFrequency = 28f;

    [Header("Lean")]
    [Tooltip("Grados de inclinación de la cámara por cada rad/s de giro del coche.")]
    [SerializeField, Min(0f)] private float leanPerYawRate = 3f;
    [SerializeField, Min(0f)] private float maximumLeanDegrees = 5f;
    [SerializeField, Min(0f)] private float leanSmoothing = 5f;

    private Camera followCamera;
    private Rigidbody targetBody;
    private Vector3 heading;
    private bool headingFromVelocity;
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    private float speedFactor;
    private float lean;

    private void Awake()
    {
        followCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        if (target != null && transform.IsChildOf(target.transform))
        {
            Debug.LogError("CarFollowCamera debe ir en la cámara, no en el coche al que sigue.", this);
            enabled = false;
            return;
        }

        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        UpdateHeading(Blend(headingSmoothing, deltaTime));
        UpdateSpeedEffects(deltaTime);

        GetDesiredPose(out Vector3 desiredPosition, out Quaternion desiredRotation);
        smoothedPosition = Vector3.Lerp(smoothedPosition, desiredPosition, Blend(positionSmoothing, deltaTime));
        smoothedRotation = Quaternion.Slerp(smoothedRotation, desiredRotation, Blend(rotationSmoothing, deltaTime));

        // La vibración y la inclinación se aplican después del suavizado para que no queden filtradas.
        transform.SetPositionAndRotation(
            smoothedPosition + GetShakeOffset(),
            smoothedRotation * Quaternion.Euler(0f, 0f, lean));
        followCamera.fieldOfView = Mathf.Lerp(baseFieldOfView, maximumFieldOfView, speedFactor);
    }

    public void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        targetBody = target.GetComponent<Rigidbody>();
        heading = Flatten(target.WorldForward, Vector3.forward);
        headingFromVelocity = false;
        speedFactor = 0f;
        lean = 0f;
        GetDesiredPose(out smoothedPosition, out smoothedRotation);
        transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
        if (followCamera != null)
        {
            followCamera.fieldOfView = baseFieldOfView;
        }
    }

    private void UpdateHeading(float blend)
    {
        Vector3 velocity = targetBody != null ? Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up) : Vector3.zero;
        if (velocity.magnitude * 3.6f < velocityHeadingMinSpeedKph)
        {
            return;
        }

        Vector3 desired = velocity.normalized;
        // La primera vez que el coche se mueve se toma su dirección tal cual; después, al ir marcha atrás, la cámara no se da la vuelta.
        if (headingFromVelocity && Vector3.Dot(desired, heading) < 0f)
        {
            desired = -desired;
        }

        heading = headingFromVelocity ? Vector3.Slerp(heading, desired, blend).normalized : desired;
        headingFromVelocity = true;
    }

    private void UpdateSpeedEffects(float deltaTime)
    {
        float targetSpeedFactor = Mathf.Clamp01(target.CurrentSpeedKph / fullEffectSpeedKph);
        speedFactor = Mathf.Lerp(speedFactor, targetSpeedFactor, Blend(speedEffectSmoothing, deltaTime));

        float yawRate = targetBody != null ? targetBody.angularVelocity.y : 0f;
        float targetLean = Mathf.Clamp(-yawRate * leanPerYawRate * speedFactor, -maximumLeanDegrees, maximumLeanDegrees);
        lean = Mathf.Lerp(lean, targetLean, Blend(leanSmoothing, deltaTime));
    }

    private Vector3 GetShakeOffset()
    {
        float intensity = Mathf.InverseLerp(shakeStartSpeedFraction, 1f, speedFactor);
        float speedShake = shakeAmplitude * intensity * intensity;
        float surfaceShake = surfaceShakeAmplitude * target.SurfaceRoughness;

        Vector3 offset = Vector3.zero;
        if (speedShake > 0f)
        {
            offset += Noise(Time.time * shakeFrequency, 0f) * speedShake;
        }

        if (surfaceShake > 0f)
        {
            offset += Noise(Time.time * surfaceShakeFrequency, 50f) * surfaceShake;
        }

        return transform.rotation * offset;
    }

    private static Vector3 Noise(float time, float seed)
    {
        return new Vector3(
            Mathf.PerlinNoise(time, seed) * 2f - 1f,
            Mathf.PerlinNoise(seed, time) * 2f - 1f,
            0f);
    }

    private void GetDesiredPose(out Vector3 position, out Quaternion rotation)
    {
        Vector3 carPosition = target.transform.position;
        float distance = distanceBehind + extraDistanceAtSpeed * speedFactor;
        float currentHeight = height - heightDropAtSpeed * speedFactor;
        position = carPosition - heading * distance + Vector3.up * currentHeight;
        Vector3 lookPoint = carPosition + heading * lookAheadDistance + Vector3.up * lookAtHeight;
        rotation = Quaternion.LookRotation(lookPoint - position, Vector3.up);
    }

    private static float Blend(float smoothing, float deltaTime)
    {
        return 1f - Mathf.Exp(-smoothing * deltaTime);
    }

    private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
    {
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        return flat.sqrMagnitude < 0.0001f ? fallback : flat.normalized;
    }
}

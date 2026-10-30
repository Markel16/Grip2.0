using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class ArcadeCarController : MonoBehaviour
{
    [System.Serializable]
    private sealed class WheelSetup
    {
        [SerializeField] private WheelTireWear tire;
        [SerializeField] private Transform visual;
        [SerializeField] private bool steers;
        [SerializeField] private bool isDriven;
        [SerializeField] private bool usesHandbrake;
        [SerializeField, Min(0f)] private float brakeMultiplier = 1f;

        public WheelTireWear Tire => tire;
        public Transform Visual => visual;
        public bool Steers => steers;
        public bool IsDriven => isDriven;
        public bool UsesHandbrake => usesHandbrake;
        public float BrakeMultiplier => brakeMultiplier;
    }

    [Header("Vehicle")]
    [SerializeField] private Rigidbody vehicleBody;
    [SerializeField] private WheelSetup[] wheels = System.Array.Empty<WheelSetup>();
    [SerializeField] private Vector3 localForwardDirection = Vector3.left;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference brakeAction;
    [SerializeField] private InputActionReference handbrakeAction;

    [Header("Handling")]
    [SerializeField, Min(0f)] private float maximumSteerAngle = 30f;
    [SerializeField, Range(0f, 1f)] private float highSpeedSteeringFactor = 0.55f;
    [SerializeField, Min(0f)] private float maximumMotorTorque = 1200f;
    [SerializeField, Range(0f, 1f)] private float reverseTorqueMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float maximumBrakeTorque = 2500f;
    [SerializeField, Min(0f)] private float maximumHandbrakeTorque = 3500f;
    [SerializeField, Min(1f)] private float maximumSpeedKph = 160f;

    [Header("Fuel")]
    [SerializeField, Min(0.1f)] private float fuelCapacityLiters = 40f;
    [SerializeField, Min(0f)] private float fuelConsumptionPerSecond = 0.15f;
    [SerializeField, Min(0f)] private float fuelRemainingLiters = 40f;

    [Header("Surface Effects")]
    [Tooltip("Aceleración vertical máxima de los baches en una superficie con rugosidad 1.")]
    [SerializeField, Min(0f)] private float bumpAcceleration = 8f;
    [SerializeField, Min(0f)] private float bumpFrequency = 6f;
    [Tooltip("Velocidad a partir de la cual los baches se notan al máximo.")]
    [SerializeField, Min(1f)] private float fullBumpSpeedKph = 80f;

    private readonly List<InputAction> actionsEnabledByThisController = new List<InputAction>();
    private float steeringInput;
    private float throttleInput;
    private float reverseInput;
    private float brakeInput;
    private float handbrakeInput;
    private bool externalInputMode;
    private bool pitStopHeld;

    public float FuelCapacityLiters => fuelCapacityLiters;
    public float FuelRemainingLiters => fuelRemainingLiters;
    public float CurrentSpeedKph => vehicleBody != null ? vehicleBody.linearVelocity.magnitude * 3.6f : 0f;
    public Vector3 WorldForward => transform.TransformDirection(localForwardDirection.normalized);
    public float SurfaceRoughness { get; private set; }
    public float AverageTireWearPercent
    {
        get
        {
            if (wheels == null || wheels.Length == 0)
            {
                return 0f;
            }

            float totalWear = 0f;
            int tireCount = 0;
            foreach (WheelSetup wheel in wheels)
            {
                if (wheel == null || wheel.Tire == null)
                {
                    continue;
                }

                totalWear += wheel.Tire.WearPercent;
                tireCount++;
            }

            return tireCount > 0 ? totalWear / tireCount : 0f;
        }
    }

    private void Awake()
    {
        fuelCapacityLiters = Mathf.Max(0.1f, fuelCapacityLiters);
        fuelRemainingLiters = Mathf.Clamp(fuelRemainingLiters, 0f, fuelCapacityLiters);
    }

    private void OnEnable()
    {
        EnableActionIfNeeded(moveAction);
        EnableActionIfNeeded(brakeAction);
        EnableActionIfNeeded(handbrakeAction);
    }

    private void OnDisable()
    {
        foreach (InputAction action in actionsEnabledByThisController)
        {
            action.Disable();
        }

        actionsEnabledByThisController.Clear();
    }

    private void Update()
    {
        if (externalInputMode)
        {
            return;
        }

        Vector2 moveInput = ReadVector2Action(moveAction);
        steeringInput = Mathf.Clamp(moveInput.x, -1f, 1f);
        throttleInput = Mathf.Clamp01(moveInput.y);
        reverseInput = Mathf.Clamp01(-moveInput.y);
        brakeInput = Mathf.Clamp01(ReadAction(brakeAction));
        handbrakeInput = Mathf.Clamp01(ReadAction(handbrakeAction));
    }

    private void FixedUpdate()
    {
        if (pitStopHeld)
        {
            steeringInput = 0f;
            throttleInput = 0f;
            reverseInput = 0f;
            brakeInput = 1f;
            handbrakeInput = 1f;
        }

        Vector3 vehicleForward = transform.TransformDirection(localForwardDirection.normalized);
        float forwardSpeedKph = vehicleBody == null
            ? 0f
            : Vector3.Dot(vehicleBody.linearVelocity, vehicleForward) * 3.6f;

        float motorTorque = (throttleInput - reverseInput * reverseTorqueMultiplier) * maximumMotorTorque;
        float engineDemand = Mathf.Max(throttleInput, reverseInput);
        fuelRemainingLiters = Mathf.Max(
            0f,
            fuelRemainingLiters - engineDemand * fuelConsumptionPerSecond * Time.fixedDeltaTime);

        if ((forwardSpeedKph >= maximumSpeedKph && motorTorque > 0f)
            || (forwardSpeedKph <= -maximumSpeedKph && motorTorque < 0f)
            || brakeInput > 0f
            || fuelRemainingLiters <= 0f)
        {
            motorTorque = 0f;
        }

        float speedFactor = vehicleBody == null
            ? 0f
            : Mathf.Clamp01(Mathf.Abs(forwardSpeedKph) / maximumSpeedKph);
        float steerAngle = steeringInput
            * maximumSteerAngle
            * Mathf.Lerp(1f, highSpeedSteeringFactor, speedFactor);

        if (wheels == null)
        {
            return;
        }

        foreach (WheelSetup wheel in wheels)
        {
            WheelCollider wheelCollider = wheel != null && wheel.Tire != null ? wheel.Tire.Collider : null;
            if (wheelCollider == null)
            {
                continue;
            }

            wheelCollider.steerAngle = wheel.Steers ? steerAngle : 0f;
            wheelCollider.motorTorque = wheel.IsDriven ? motorTorque : 0f;

            float appliedBrake = brakeInput * maximumBrakeTorque;
            if (wheel.UsesHandbrake)
            {
                appliedBrake = Mathf.Max(appliedBrake, handbrakeInput * maximumHandbrakeTorque);
            }

            wheelCollider.brakeTorque = appliedBrake * wheel.BrakeMultiplier;
        }

        ApplySurfaceEffects();
    }

    private void ApplySurfaceEffects()
    {
        SurfaceRoughness = 0f;
        if (vehicleBody == null || wheels.Length == 0)
        {
            return;
        }

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(vehicleBody.linearVelocity, Vector3.up);
        float bumpSpeedFactor = Mathf.Clamp01(horizontalVelocity.magnitude * 3.6f / fullBumpSpeedKph);
        float roughnessSum = 0f;

        for (int i = 0; i < wheels.Length; i++)
        {
            WheelTireWear tire = wheels[i] != null ? wheels[i].Tire : null;
            WheelSurface surface = tire != null && tire.IsGrounded ? tire.CurrentSurface : null;
            if (surface == null)
            {
                continue;
            }

            // Cada rueda aporta su parte: con dos ruedas en la hierba se frena la mitad.
            vehicleBody.AddForce(-horizontalVelocity * (surface.RollingResistance / wheels.Length), ForceMode.Acceleration);

            if (surface.Roughness <= 0f)
            {
                continue;
            }

            float noise = Mathf.PerlinNoise(Time.time * bumpFrequency, i * 17.3f) * 2f - 1f;
            float bump = noise * bumpAcceleration * surface.Roughness * bumpSpeedFactor;
            vehicleBody.AddForceAtPosition(Vector3.up * (bump * vehicleBody.mass / wheels.Length), tire.Collider.transform.position);
            roughnessSum += surface.Roughness * bumpSpeedFactor;
        }

        SurfaceRoughness = roughnessSum / wheels.Length;
    }

    private void LateUpdate()
    {
        if (wheels == null)
        {
            return;
        }

        foreach (WheelSetup wheel in wheels)
        {
            if (wheel == null || wheel.Tire == null || wheel.Tire.Collider == null || wheel.Visual == null)
            {
                continue;
            }

            wheel.Tire.Collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            wheel.Visual.SetPositionAndRotation(position, rotation);
        }
    }

    private void OnValidate()
    {
        maximumSpeedKph = Mathf.Max(1f, maximumSpeedKph);
        fuelCapacityLiters = Mathf.Max(0.1f, fuelCapacityLiters);
        fuelRemainingLiters = Mathf.Clamp(fuelRemainingLiters, 0f, fuelCapacityLiters);
    }

    public void Refuel(float liters)
    {
        fuelRemainingLiters = Mathf.Clamp(fuelRemainingLiters + Mathf.Max(0f, liters), 0f, fuelCapacityLiters);
    }

    public void ReplaceTires()
    {
        if (wheels == null)
        {
            return;
        }

        foreach (WheelSetup wheel in wheels)
        {
            if (wheel != null && wheel.Tire != null)
            {
                wheel.Tire.ResetWear();
            }
        }
    }

    public void SetPitStopHold(bool held)
    {
        pitStopHeld = held;
        if (!held)
        {
            return;
        }

        steeringInput = 0f;
        throttleInput = 0f;
        reverseInput = 0f;
        brakeInput = 1f;
        handbrakeInput = 1f;
    }

    public void SetExternalInputMode(bool enabled)
    {
        externalInputMode = enabled;
    }

    public void SetExternalDriveInput(float steering, float throttle, float brake, float handbrake)
    {
        externalInputMode = true;
        steeringInput = Mathf.Clamp(steering, -1f, 1f);
        // Un acelerador negativo es marcha atrás.
        throttleInput = Mathf.Clamp01(throttle);
        reverseInput = Mathf.Clamp01(-throttle);
        brakeInput = Mathf.Clamp01(brake);
        handbrakeInput = Mathf.Clamp01(handbrake);
    }

    private void EnableActionIfNeeded(InputActionReference actionReference)
    {
        InputAction action = actionReference != null ? actionReference.action : null;
        if (action == null || action.enabled)
        {
            return;
        }

        action.Enable();
        actionsEnabledByThisController.Add(action);
    }

    private static float ReadAction(InputActionReference actionReference)
    {
        InputAction action = actionReference != null ? actionReference.action : null;
        return action != null && action.enabled ? action.ReadValue<float>() : 0f;
    }

    private static Vector2 ReadVector2Action(InputActionReference actionReference)
    {
        InputAction action = actionReference != null ? actionReference.action : null;
        return action != null && action.enabled ? action.ReadValue<Vector2>() : Vector2.zero;
    }
}
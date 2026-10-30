using UnityEngine;

[DisallowMultipleComponent]
public sealed class WheelTireWear : MonoBehaviour
{
    [SerializeField] private WheelCollider wheelCollider;
    [SerializeField, Range(0f, 100f)] private float wearPercent;
    [SerializeField, Min(0f)] private float wearPerSecond = 4f;
    [SerializeField, Min(0f)] private float slipThreshold = 0.25f;
    [SerializeField, Min(0.01f)] private float fullWearSlip = 1.5f;
    [SerializeField, Range(0.01f, 1f)] private float minimumGripMultiplier = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float currentGripMultiplier = 1f;
    [SerializeField] private WheelSurface currentSurface;
    [SerializeField, Range(0.1f, 2f)] private float currentSurfaceGripMultiplier = 1f;

    [Header("Dirt")]
    [SerializeField, Range(0f, 1f)] private float dirtLevel;
    [Tooltip("Agarre que queda con el neumático totalmente sucio.")]
    [SerializeField, Range(0.1f, 1f)] private float dirtyGripMultiplier = 0.7f;
    [Tooltip("Lo rápido que se limpia el neumático al rodar por una superficie limpia (por segundo).")]
    [SerializeField, Min(0f)] private float dirtCleanPerSecond = 0.2f;

    private WheelFrictionCurve originalForwardFriction;
    private WheelFrictionCurve originalSidewaysFriction;
    private bool originalFrictionCaptured;

    public float WearPercent => wearPercent;
    public float GripMultiplier => currentGripMultiplier;
    public WheelCollider Collider => wheelCollider;
    public WheelSurface CurrentSurface => currentSurface;
    public bool IsGrounded { get; private set; }

    private void Awake()
    {
        CaptureOriginalFriction();
        UpdateGrip();
    }

    private void FixedUpdate()
    {
        if (wheelCollider == null)
        {
            return;
        }

        IsGrounded = wheelCollider.GetGroundHit(out WheelHit hit);
        if (IsGrounded)
        {
            currentSurface = hit.collider != null ? hit.collider.GetComponentInParent<WheelSurface>() : null;
            currentSurfaceGripMultiplier = currentSurface != null ? currentSurface.GripMultiplier : 1f;

            float slip = Mathf.Max(Mathf.Abs(hit.forwardSlip), Mathf.Abs(hit.sidewaysSlip));
            float wearFactor = Mathf.InverseLerp(slipThreshold, Mathf.Max(slipThreshold + 0.01f, fullWearSlip), slip);
            float surfaceWearMultiplier = currentSurface != null ? currentSurface.WearMultiplier : 1f;
            wearPercent = Mathf.Clamp(wearPercent + wearFactor * wearPerSecond * surfaceWearMultiplier * Time.fixedDeltaTime, 0f, 100f);

            // Solo se limpia rodando; parado el neumático sigue sucio.
            float dirtGain = currentSurface != null ? currentSurface.DirtPerSecond : 0f;
            float rolling = Mathf.Clamp01(Mathf.Abs(wheelCollider.rpm) / 100f);
            float dirtChange = dirtGain > 0f ? dirtGain * rolling : -dirtCleanPerSecond * rolling;
            dirtLevel = Mathf.Clamp01(dirtLevel + dirtChange * Time.fixedDeltaTime);
        }
        else
        {
            currentSurface = null;
            currentSurfaceGripMultiplier = 1f;
        }

        UpdateGrip();
    }

    private void OnValidate()
    {
        wearPercent = Mathf.Clamp(wearPercent, 0f, 100f);
        fullWearSlip = Mathf.Max(slipThreshold + 0.01f, fullWearSlip);

        if (Application.isPlaying && originalFrictionCaptured)
        {
            UpdateGrip();
        }
    }

    public void ResetWear()
    {
        wearPercent = 0f;
        dirtLevel = 0f;
        UpdateGrip();
    }

    private void CaptureOriginalFriction()
    {
        if (wheelCollider == null)
        {
            return;
        }

        originalForwardFriction = wheelCollider.forwardFriction;
        originalSidewaysFriction = wheelCollider.sidewaysFriction;
        originalFrictionCaptured = true;
    }

    private void UpdateGrip()
    {
        if (wheelCollider == null || !originalFrictionCaptured)
        {
            return;
        }

        float wearFactor = wearPercent / 100f;
        currentGripMultiplier = Mathf.Lerp(1f, minimumGripMultiplier, wearFactor)
            * Mathf.Lerp(1f, dirtyGripMultiplier, dirtLevel)
            * currentSurfaceGripMultiplier;

        WheelFrictionCurve forwardFriction = originalForwardFriction;
        forwardFriction.stiffness = originalForwardFriction.stiffness * currentGripMultiplier;
        wheelCollider.forwardFriction = forwardFriction;

        WheelFrictionCurve sidewaysFriction = originalSidewaysFriction;
        sidewaysFriction.stiffness = originalSidewaysFriction.stiffness * currentGripMultiplier;
        wheelCollider.sidewaysFriction = sidewaysFriction;
    }
}
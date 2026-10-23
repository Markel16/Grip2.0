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

    private WheelFrictionCurve originalForwardFriction;
    private WheelFrictionCurve originalSidewaysFriction;
    private bool originalFrictionCaptured;

    public float WearPercent => wearPercent;
    public float GripMultiplier => currentGripMultiplier;
    public WheelCollider Collider => wheelCollider;

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

        if (wheelCollider.GetGroundHit(out WheelHit hit))
        {
            currentSurface = hit.collider != null ? hit.collider.GetComponentInParent<WheelSurface>() : null;
            currentSurfaceGripMultiplier = currentSurface != null ? currentSurface.GripMultiplier : 1f;

            float slip = Mathf.Max(Mathf.Abs(hit.forwardSlip), Mathf.Abs(hit.sidewaysSlip));
            float wearFactor = Mathf.InverseLerp(slipThreshold, Mathf.Max(slipThreshold + 0.01f, fullWearSlip), slip);
            float surfaceWearMultiplier = currentSurface != null ? currentSurface.WearMultiplier : 1f;
            wearPercent = Mathf.Clamp(wearPercent + wearFactor * wearPerSecond * surfaceWearMultiplier * Time.fixedDeltaTime, 0f, 100f);
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
        currentGripMultiplier = Mathf.Lerp(1f, minimumGripMultiplier, wearFactor) * currentSurfaceGripMultiplier;

        WheelFrictionCurve forwardFriction = originalForwardFriction;
        forwardFriction.stiffness = originalForwardFriction.stiffness * currentGripMultiplier;
        wheelCollider.forwardFriction = forwardFriction;

        WheelFrictionCurve sidewaysFriction = originalSidewaysFriction;
        sidewaysFriction.stiffness = originalSidewaysFriction.stiffness * currentGripMultiplier;
        wheelCollider.sidewaysFriction = sidewaysFriction;
    }
}
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LapFinishTrigger : MonoBehaviour
{
    [SerializeField] private RaceHudController raceHud;
    [SerializeField] private Collider finishLineCollider;
    [SerializeField, Min(0f)] private float minimumForwardSpeed = 1f;

    private void Awake()
    {
        if (finishLineCollider == null)
        {
            finishLineCollider = GetComponent<Collider>();
        }

        if (finishLineCollider == null)
        {
            Debug.LogError("LapFinishTrigger requires a Collider on the same GameObject.", this);
        }
        else if (!finishLineCollider.isTrigger)
        {
            Debug.LogError("The finish-line Collider must have Is Trigger enabled.", this);
        }

        if (raceHud == null)
        {
            Debug.LogError("Assign the active RaceHudController in the Inspector.", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (raceHud == null || finishLineCollider == null || !finishLineCollider.isTrigger)
        {
            return;
        }

        ArcadeCarController car = other.GetComponentInParent<ArcadeCarController>();
        if (car == null)
        {
            return;
        }

        Rigidbody carBody = other.attachedRigidbody != null
            ? other.attachedRigidbody
            : car.GetComponent<Rigidbody>();
        if (carBody == null)
        {
            return;
        }

        float forwardSpeed = Vector3.Dot(carBody.linearVelocity, transform.forward);
        if (forwardSpeed < minimumForwardSpeed)
        {
            return;
        }

        raceHud.RegisterLap(car);
    }

    private void OnValidate()
    {
        minimumForwardSpeed = Mathf.Max(0f, minimumForwardSpeed);
    }
}
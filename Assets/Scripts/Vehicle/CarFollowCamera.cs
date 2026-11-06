using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CarFollowCamera : MonoBehaviour
{
    private enum ViewKind
    {
        // Sigue al coche desde detrás con suavizado, como la cámara de TV.
        Chase,
        // Fija al coche (T-Cam, cockpit, morro): se mueve y se inclina con él.
        Mounted
    }

    [System.Serializable]
    private sealed class CameraView
    {
        public string name = "TV";
        public ViewKind kind = ViewKind.Chase;
        [Tooltip("x = derecha, y = arriba, z = delante, respecto al coche. En Chase, z negativo = metros por detrás.")]
        public Vector3 offset = new Vector3(0f, 3f, -7f);
        [Min(0f)] public float lookAheadDistance = 6f;
        public float lookAtHeight = 0.5f;
        [Range(1f, 179f)] public float fieldOfView = 60f;
        [Tooltip("Cuánto le afectan los efectos de velocidad (campo de visión, vibración). 0 = nada.")]
        [Range(0f, 1f)] public float speedEffectScale = 1f;

        public CameraView(string name, ViewKind kind, Vector3 offset, float lookAheadDistance, float lookAtHeight, float fieldOfView, float speedEffectScale)
        {
            this.name = name;
            this.kind = kind;
            this.offset = offset;
            this.lookAheadDistance = lookAheadDistance;
            this.lookAtHeight = lookAtHeight;
            this.fieldOfView = fieldOfView;
            this.speedEffectScale = speedEffectScale;
        }
    }

    [Header("Target")]
    [SerializeField] private ArcadeCarController target;

    [Header("Views")]
    [SerializeField] private CameraView[] views =
    {
        new CameraView("TV", ViewKind.Chase, new Vector3(0f, 3f, -7f), 6f, 0.5f, 60f, 1f),
        new CameraView("TV lejana", ViewKind.Chase, new Vector3(0f, 4.5f, -11f), 10f, 0.5f, 55f, 1f),
        new CameraView("T-Cam", ViewKind.Mounted, new Vector3(0f, 1.05f, -0.35f), 20f, 0.6f, 65f, 0.6f),
        new CameraView("Cockpit", ViewKind.Mounted, new Vector3(0f, 0.55f, 0.05f), 20f, 0.45f, 72f, 0.6f),
        new CameraView("Morro", ViewKind.Mounted, new Vector3(0f, 0.1f, 1.15f), 20f, 0.1f, 70f, 0.8f)
    };
    [SerializeField, Min(0)] private int startingView;
    [SerializeField] private Key changeViewKey = Key.C;
    [SerializeField, Min(0f)] private float viewNameSeconds = 1.5f;
    [Tooltip("Plano de recorte cercano en las cámaras montadas, para que no se corte el coche al estar tan cerca.")]
    [SerializeField, Min(0.01f)] private float mountedNearClipPlane = 0.05f;

    [Header("Heading")]
    [Tooltip("Por encima de esta velocidad la cámara se orienta según la dirección real de movimiento del coche.")]
    [SerializeField, Min(0f)] private float velocityHeadingMinSpeedKph = 5f;
    [SerializeField, Min(0f)] private float headingSmoothing = 4f;

    [Header("Smoothing")]
    [SerializeField, Min(0f)] private float positionSmoothing = 8f;
    [SerializeField, Min(0f)] private float rotationSmoothing = 10f;
    [Tooltip("Suavizado de giro en las cámaras montadas. Más alto = más pegada al coche.")]
    [SerializeField, Min(0f)] private float mountedRotationSmoothing = 25f;

    [Header("Speed Effects")]
    [Tooltip("Velocidad a la que los efectos llegan a su máximo.")]
    [SerializeField, Min(1f)] private float fullEffectSpeedKph = 160f;
    [SerializeField, Min(0f)] private float speedEffectSmoothing = 3f;
    [Tooltip("Grados que se abre el campo de visión a máxima velocidad.")]
    [SerializeField, Min(0f)] private float extraFieldOfViewAtSpeed = 18f;
    [Tooltip("Metros extra que la cámara se aleja a máxima velocidad (solo Chase).")]
    [SerializeField, Min(0f)] private float extraDistanceAtSpeed = 1.5f;
    [Tooltip("Metros que la cámara baja a máxima velocidad (solo Chase).")]
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
    [Tooltip("Grados de inclinación de la cámara por cada rad/s de giro del coche (solo Chase).")]
    [SerializeField, Min(0f)] private float leanPerYawRate = 3f;
    [SerializeField, Min(0f)] private float maximumLeanDegrees = 5f;
    [SerializeField, Min(0f)] private float leanSmoothing = 5f;

    private Camera followCamera;
    private Rigidbody targetBody;
    private float defaultNearClipPlane;
    private int currentViewIndex;
    private float viewNameTimer;
    private Vector3 heading;
    private bool headingFromVelocity;
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    private float speedFactor;
    private float lean;
    private GUIStyle viewNameStyle;

    private CameraView CurrentView => views[currentViewIndex];

    private void Awake()
    {
        followCamera = GetComponent<Camera>();
        defaultNearClipPlane = followCamera.nearClipPlane;
    }

    private void Start()
    {
        if (target == null)
        {
            target = FindPlayerCar();
        }

        if (target == null)
        {
            Debug.LogError("CarFollowCamera no tiene coche asignado en Target y no encontró el del jugador.", this);
            enabled = false;
            return;
        }

        if (transform == target.transform)
        {
            Debug.LogError("CarFollowCamera debe ir en la cámara, no en el coche al que sigue.", this);
            enabled = false;
            return;
        }

        // Si la cámara es hija del coche heredaría sus giros encima de los suyos; se suelta para que se mueva sola.
        if (transform.IsChildOf(target.transform))
        {
            transform.SetParent(null, true);
            Debug.LogWarning($"'{name}' estaba dentro de '{target.name}'. Se ha sacado al empezar; sácala también en la jerarquía para que no vuelva a pasar.", this);
        }

        if (views == null || views.Length == 0)
        {
            Debug.LogError("CarFollowCamera necesita al menos una vista.", this);
            enabled = false;
            return;
        }

        foreach (Camera other in FindObjectsByType<Camera>())
        {
            if (other != followCamera && other.enabled && other.targetTexture == null && other.depth >= followCamera.depth)
            {
                Debug.LogWarning($"La cámara '{other.name}' se dibuja encima de '{name}' y tapa el cambio de vistas. Quítala o baja su Depth.", other);
            }
        }

        currentViewIndex = Mathf.Clamp(startingView, 0, views.Length - 1);
        SnapToTarget();
    }

    private void Update()
    {
        bool keyPressed = Keyboard.current != null && Keyboard.current[changeViewKey].wasPressedThisFrame;
        bool buttonPressed = Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame;
        if (keyPressed || buttonPressed)
        {
            NextView();
        }

        viewNameTimer = Mathf.Max(0f, viewNameTimer - Time.deltaTime);
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

        CameraView view = CurrentView;
        GetDesiredPose(view, out Vector3 desiredPosition, out Quaternion desiredRotation);
        if (view.kind == ViewKind.Mounted)
        {
            // Pegada al coche: la posición no se suaviza para que no parezca que flota.
            smoothedPosition = desiredPosition;
            smoothedRotation = Quaternion.Slerp(smoothedRotation, desiredRotation, Blend(mountedRotationSmoothing, deltaTime));
        }
        else
        {
            smoothedPosition = Vector3.Lerp(smoothedPosition, desiredPosition, Blend(positionSmoothing, deltaTime));
            smoothedRotation = Quaternion.Slerp(smoothedRotation, desiredRotation, Blend(rotationSmoothing, deltaTime));
        }

        // La vibración y la inclinación se aplican después del suavizado para que no queden filtradas.
        float viewLean = view.kind == ViewKind.Chase ? lean : 0f;
        transform.SetPositionAndRotation(
            smoothedPosition + GetShakeOffset(view.speedEffectScale),
            smoothedRotation * Quaternion.Euler(0f, 0f, viewLean));
        followCamera.fieldOfView = view.fieldOfView + extraFieldOfViewAtSpeed * speedFactor * view.speedEffectScale;
    }

    public void NextView()
    {
        currentViewIndex = (currentViewIndex + 1) % views.Length;
        viewNameTimer = viewNameSeconds;
        SnapToView();
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
        SnapToView();
    }

    // Al cambiar de cámara se corta directamente, como en la tele, en vez de hacer un barrido.
    private void SnapToView()
    {
        if (target == null)
        {
            return;
        }

        CameraView view = CurrentView;
        GetDesiredPose(view, out smoothedPosition, out smoothedRotation);
        transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
        if (followCamera != null)
        {
            followCamera.fieldOfView = view.fieldOfView;
            followCamera.nearClipPlane = view.kind == ViewKind.Mounted ? mountedNearClipPlane : defaultNearClipPlane;
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

    private Vector3 GetShakeOffset(float scale)
    {
        float intensity = Mathf.InverseLerp(shakeStartSpeedFraction, 1f, speedFactor);
        float speedShake = shakeAmplitude * intensity * intensity * scale;
        float surfaceShake = surfaceShakeAmplitude * target.SurfaceRoughness * scale;

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

    private void GetDesiredPose(CameraView view, out Vector3 position, out Quaternion rotation)
    {
        Vector3 carPosition = target.transform.position;
        if (view.kind == ViewKind.Mounted)
        {
            // Usa la orientación real del coche (incluida la inclinación en rampas y derrapes), como una cámara atornillada.
            Vector3 forward = target.WorldForward.normalized;
            Vector3 up = target.transform.up;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            position = carPosition + right * view.offset.x + up * view.offset.y + forward * view.offset.z;
            Vector3 mountedLookPoint = carPosition + forward * view.lookAheadDistance + up * view.lookAtHeight;
            rotation = Quaternion.LookRotation(mountedLookPoint - position, up);
            return;
        }

        Vector3 side = Vector3.Cross(Vector3.up, heading);
        float distance = -view.offset.z + extraDistanceAtSpeed * speedFactor * view.speedEffectScale;
        float height = view.offset.y - heightDropAtSpeed * speedFactor * view.speedEffectScale;
        position = carPosition - heading * distance + Vector3.up * height + side * view.offset.x;
        Vector3 lookPoint = carPosition + heading * view.lookAheadDistance + Vector3.up * view.lookAtHeight;
        rotation = Quaternion.LookRotation(lookPoint - position, Vector3.up);
    }

    private void OnGUI()
    {
        if (viewNameTimer <= 0f || views == null || views.Length == 0)
        {
            return;
        }

        if (viewNameStyle == null)
        {
            viewNameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        viewNameStyle.fontSize = Mathf.Max(18, Mathf.RoundToInt(Screen.height * 0.035f));
        float alpha = Mathf.Clamp01(viewNameTimer / 0.4f);
        Rect area = new Rect(0f, Screen.height * 0.08f, Screen.width, viewNameStyle.fontSize * 1.6f);

        GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
        GUI.Label(new Rect(area.x + 2f, area.y + 2f, area.width, area.height), CurrentView.name, viewNameStyle);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(area, CurrentView.name, viewNameStyle);
        GUI.color = Color.white;
    }

    private static ArcadeCarController FindPlayerCar()
    {
        foreach (ArcadeCarController car in FindObjectsByType<ArcadeCarController>())
        {
            if (car.GetComponentInParent<ArcadeWaypointAI>() == null)
            {
                return car;
            }
        }

        return null;
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

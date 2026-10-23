using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PitStopManager : MonoBehaviour
{
    private enum StopState
    {
        None,
        Playing,
        Servicing
    }

    private static readonly string[] WheelNames =
    {
        "Delantera izquierda",
        "Delantera derecha",
        "Trasera izquierda",
        "Trasera derecha"
    };

    [Header("Pit lane zones (local to this object)")]
    [SerializeField] private Vector3 entryZoneLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 exitZoneLocalPosition = new Vector3(0f, 0f, 12f);
    [SerializeField] private Vector3 zoneSize = new Vector3(8f, 4f, 6f);

    [Header("Player pit stop")]
    [SerializeField, Min(0.1f)] private float stopSpeedKph = 1f;
    [SerializeField, Min(0.1f)] private float serviceSeconds = 3f;
    [SerializeField, Min(0f)] private float missPenaltySeconds = 0.8f;
    [SerializeField, Min(0.01f)] private float timingPulseSpeed = 1.6f;
    [SerializeField, Range(0.01f, 0.2f)] private float perfectTimingTolerance = 0.045f;

    [Header("AI strategy")]
    [SerializeField, Range(0f, 100f)] private float aiWearPitThreshold = 45f;
    [SerializeField, Min(1)] private int aiStrategyLap = 2;
    [SerializeField, Min(0.1f)] private float aiServiceSeconds = 5f;

    private readonly HashSet<ArcadeCarController> aiCarsAlreadyServiced = new HashSet<ArcadeCarController>();
    private readonly HashSet<ArcadeCarController> aiCarsHeadingToPit = new HashSet<ArcadeCarController>();
    private ArcadeWaypointAI[] aiDrivers = System.Array.Empty<ArcadeWaypointAI>();
    private ArcadeCarController playerCar;
    private ArcadeCarController activeAICar;
    private ArcadeWaypointAI activeAI;
    private RaceHudController raceHud;
    private StopState stopState;
    private bool playerInsidePitLane;
    private bool playerServiceCompletedInLane;
    private int currentWheelIndex;
    private int misses;
    private float pulseTime;
    private float serviceTimer;
    private string statusMessage;
    private Texture2D solidTexture;
    private Texture2D circleTexture;
    private GUIStyle invisibleButtonStyle;

    private void Awake()
    {
        CreateZone("PitEntryZone", entryZoneLocalPosition, false);
        CreateZone("PitExitZone", exitZoneLocalPosition, true);
    }

    private void Start()
    {
        raceHud = FindAnyObjectByType<RaceHudController>();
        ArcadeCarController[] cars = FindObjectsByType<ArcadeCarController>();
        List<ArcadeWaypointAI> drivers = new List<ArcadeWaypointAI>();
        foreach (ArcadeCarController car in cars)
        {
            ArcadeWaypointAI ai = car.GetComponentInParent<ArcadeWaypointAI>();
            if (ai == null)
            {
                playerCar = car;
            }
            else if (!drivers.Contains(ai))
            {
                drivers.Add(ai);
            }
        }

        aiDrivers = drivers.ToArray();
        if (playerCar == null)
        {
            Debug.LogError("PitStopManager could not find the player's ArcadeCarController.", this);
        }
    }

    private void Update()
    {
        if (stopState == StopState.None)
        {
            TryStartAIStrategy();
        }

        if (stopState == StopState.Playing)
        {
            pulseTime += Time.deltaTime;
        }

        if (stopState == StopState.None && playerInsidePitLane && !playerServiceCompletedInLane
            && playerCar != null
            && playerCar.CurrentSpeedKph <= stopSpeedKph)
        {
            BeginPlayerMinigame();
        }
        else if (stopState == StopState.Servicing)
        {
            serviceTimer -= Time.deltaTime;
            if (serviceTimer <= 0f)
            {
                CompleteService();
            }
        }
    }

    private void OnDestroy()
    {
        if (solidTexture != null)
        {
            Destroy(solidTexture);
        }

        if (circleTexture != null)
        {
            Destroy(circleTexture);
        }
    }

    public void OnZoneEntered(ArcadeCarController car, bool isExit)
    {
        if (isExit)
        {
            if (car == playerCar)
            {
                playerInsidePitLane = false;
                playerServiceCompletedInLane = false;
                if (stopState != StopState.None && activeAI == null)
                {
                    stopState = StopState.None;
                    playerCar.SetPitStopHold(false);
                }
            }

            return;
        }

        if (car == playerCar)
        {
            if (!playerInsidePitLane)
            {
                playerInsidePitLane = true;
                playerServiceCompletedInLane = false;
            }
        }
    }

    public bool OnAIReachedPit(ArcadeWaypointAI ai, ArcadeCarController car)
    {
        if (ai == null || car == null || stopState != StopState.None
            || !aiCarsHeadingToPit.Contains(car) || aiCarsAlreadyServiced.Contains(car))
        {
            return false;
        }

        activeAI = ai;
        activeAICar = car;
        activeAI.StopAtPitBox();
        serviceTimer = aiServiceSeconds;
        stopState = StopState.Servicing;
        statusMessage = "La IA está cambiando los neumáticos";
        return true;
    }

    public void CancelAIPitApproach(ArcadeCarController car)
    {
        if (car != null)
        {
            aiCarsHeadingToPit.Remove(car);
        }
    }

    private void TryStartAIStrategy()
    {
        if (aiCarsHeadingToPit.Count > 0)
        {
            return;
        }

        Transform entry = transform.Find("PitEntryZone");
        if (entry == null)
        {
            return;
        }

        foreach (ArcadeWaypointAI ai in aiDrivers)
        {
            ArcadeCarController car = ai != null ? ai.GetComponent<ArcadeCarController>() : null;
            if (car == null || aiCarsAlreadyServiced.Contains(car) || !ShouldAIStop(car))
            {
                continue;
            }

            aiCarsHeadingToPit.Add(car);
            ai.DriveToPitEntry(this, entry);
            break;
        }
    }

    private bool ShouldAIStop(ArcadeCarController car)
    {
        return car.AverageTireWearPercent >= aiWearPitThreshold
            || (raceHud != null && raceHud.CurrentLap >= aiStrategyLap);
    }

    private void BeginPlayerMinigame()
    {
        playerCar.SetPitStopHold(true);
        currentWheelIndex = 0;
        misses = 0;
        pulseTime = 0f;
        stopState = StopState.Playing;
        statusMessage = "¡Buen timing!";
    }

    private void RegisterPlayerHit()
    {
        float pulse = (Mathf.Sin(pulseTime * timingPulseSpeed) + 1f) * 0.5f;
        float timingError = Mathf.Abs(pulse - 0.72f);
        if (timingError > perfectTimingTolerance)
        {
            misses++;
            statusMessage = "Fallaste el timing. ¡Inténtalo otra vez!";
            return;
        }

        currentWheelIndex++;
        statusMessage = "¡Perfecto!";
        if (currentWheelIndex >= WheelNames.Length)
        {
            serviceTimer = serviceSeconds + misses * missPenaltySeconds;
            stopState = StopState.Servicing;
            statusMessage = "Cambio de ruedas en curso...";
        }
    }

    private void CompleteService()
    {
        if (activeAI != null)
        {
            activeAICar.ReplaceTires();
            aiCarsAlreadyServiced.Add(activeAICar);
            aiCarsHeadingToPit.Remove(activeAICar);
            Transform exit = transform.Find("PitExitZone");
            if (exit != null)
            {
                activeAI.DriveToPitExit(exit);
            }

            activeAI.SetPitStopHold(false);
            activeAI = null;
            activeAICar = null;
        }
        else if (playerCar != null)
        {
            playerCar.ReplaceTires();
            playerCar.SetPitStopHold(false);
            playerServiceCompletedInLane = true;
        }

        stopState = StopState.None;
        statusMessage = string.Empty;
    }

    private void CreateZone(string zoneName, Vector3 localPosition, bool isExit)
    {
        GameObject zone = new GameObject(zoneName);
        zone.transform.SetParent(transform, false);
        zone.transform.localPosition = localPosition;

        BoxCollider box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = zoneSize;

        zone.AddComponent<PitStopZone>().Configure(this, isExit);
    }

    private void OnGUI()
    {
        if (stopState == StopState.None)
        {
            return;
        }

        EnsureGuiResources();
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
        GUI.color = Color.white;

        float panelWidth = Mathf.Min(560f, Screen.width - 32f);
        float panelHeight = stopState == StopState.Playing ? 390f : 220f;
        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth,
            panelHeight);
        GUI.Box(panel, GUIContent.none);

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.027f)),
            wordWrap = true
        };
        GUI.Label(new Rect(panel.x + 20f, panel.y + 18f, panel.width - 40f, 42f),
            stopState == StopState.Playing ? "PIT STOP — CAMBIO DE NEUMÁTICOS" : statusMessage, labelStyle);

        if (stopState == StopState.Playing)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown
                && (currentEvent.keyCode == KeyCode.Space || currentEvent.keyCode == KeyCode.Return))
            {
                RegisterPlayerHit();
                currentEvent.Use();
                if (stopState != StopState.Playing)
                {
                    return;
                }
            }

            GUI.Label(new Rect(panel.x + 20f, panel.y + 68f, panel.width - 40f, 32f),
                $"Rueda {currentWheelIndex + 1}/4: {WheelNames[currentWheelIndex]}", labelStyle);

            float diameter = Mathf.Min(180f, panel.width * 0.36f);
            Rect target = new Rect(
                panel.center.x - diameter * 0.5f,
                panel.y + 112f,
                diameter,
                diameter);
            DrawTimingTarget(target);
            if (GUI.Button(target, GUIContent.none, invisibleButtonStyle))
            {
                RegisterPlayerHit();
            }

            GUI.Label(new Rect(panel.x + 20f, panel.y + panel.height - 62f, panel.width - 40f, 32f),
                statusMessage + "  Pulsa ESPACIO o toca el círculo cuando coincida.", labelStyle);
        }
        else
        {
            GUI.Label(new Rect(panel.x + 20f, panel.y + 76f, panel.width - 40f, 46f),
                $"Tiempo restante: {Mathf.Max(0f, serviceTimer):F1}s", labelStyle);
        }
    }

    private void DrawTimingTarget(Rect rect)
    {
        float center = rect.width * 0.5f;
        float targetRadius = rect.width * 0.354f;
        float pulse = (Mathf.Sin(pulseTime * timingPulseSpeed) + 1f) * 0.5f;
        float pulseRadius = Mathf.Lerp(rect.width * 0.08f, rect.width * 0.46f, pulse);

        GUI.color = new Color(0.15f, 0.85f, 0.45f, 0.95f);
        GUI.DrawTexture(new Rect(rect.x + center - targetRadius, rect.y + center - targetRadius,
            targetRadius * 2f, targetRadius * 2f), circleTexture);
        GUI.color = new Color(0.06f, 0.08f, 0.1f, 1f);
        float innerRadius = targetRadius * 0.72f;
        GUI.DrawTexture(new Rect(rect.x + center - innerRadius, rect.y + center - innerRadius,
            innerRadius * 2f, innerRadius * 2f), circleTexture);
        GUI.color = new Color(1f, 0.75f, 0.12f, 0.8f);
        GUI.DrawTexture(new Rect(rect.x + center - pulseRadius, rect.y + center - pulseRadius,
            pulseRadius * 2f, pulseRadius * 2f), circleTexture);
        GUI.color = Color.white;
    }

    private void EnsureGuiResources()
    {
        if (solidTexture != null)
        {
            return;
        }

        solidTexture = new Texture2D(1, 1);
        solidTexture.SetPixel(0, 0, Color.clear);
        solidTexture.Apply();

        const int textureSize = 64;
        circleTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float dx = (x + 0.5f - textureSize * 0.5f) / (textureSize * 0.5f);
                float dy = (y + 0.5f - textureSize * 0.5f) / (textureSize * 0.5f);
                circleTexture.SetPixel(x, y, dx * dx + dy * dy <= 1f ? Color.white : Color.clear);
            }
        }

        circleTexture.Apply();
        invisibleButtonStyle = new GUIStyle(GUIStyle.none);
        invisibleButtonStyle.normal.background = solidTexture;
        invisibleButtonStyle.hover.background = solidTexture;
        invisibleButtonStyle.active.background = solidTexture;
        invisibleButtonStyle.normal.textColor = new Color(1f, 1f, 1f, 0f);
        invisibleButtonStyle.hover.textColor = invisibleButtonStyle.normal.textColor;
        invisibleButtonStyle.active.textColor = invisibleButtonStyle.normal.textColor;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.TransformPoint(entryZoneLocalPosition), zoneSize);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.TransformPoint(exitZoneLocalPosition), zoneSize);
    }

    private void OnValidate()
    {
        zoneSize.x = Mathf.Max(0.1f, zoneSize.x);
        zoneSize.y = Mathf.Max(0.1f, zoneSize.y);
        zoneSize.z = Mathf.Max(0.1f, zoneSize.z);
        aiStrategyLap = Mathf.Max(1, aiStrategyLap);
    }
}

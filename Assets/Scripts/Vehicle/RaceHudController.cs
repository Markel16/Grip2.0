using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RaceHudController : MonoBehaviour
{
    [Header("Race Data")]
    [SerializeField] private ArcadeCarController playerCar;
    [SerializeField] private WheelTireWear frontLeftTire;
    [SerializeField] private WheelTireWear frontRightTire;
    [SerializeField] private WheelTireWear rearLeftTire;
    [SerializeField] private WheelTireWear rearRightTire;
    [SerializeField, Min(1)] private int totalLaps = 3;
    [SerializeField, Min(0f)] private float minimumLapTimeSeconds = 2f;

    [Header("HUD References")]
    [SerializeField] private TMP_Text lapsText;
    [SerializeField] private TMP_Text lapTimeText;
    [SerializeField] private TMP_Text positionText;
    [SerializeField] private TMP_Text fuelText;
    [SerializeField] private TMP_Text tireWearText;
    [SerializeField] private Slider fuelSlider;
    [SerializeField] private Image frontLeftIndicator;
    [SerializeField] private Image frontRightIndicator;
    [SerializeField] private Image rearLeftIndicator;
    [SerializeField] private Image rearRightIndicator;

    [Header("Wear Colors")]
    [SerializeField] private Color freshTireColor = new Color(0.2f, 0.8f, 0.45f);
    [SerializeField] private Color wornTireColor = new Color(1f, 0.72f, 0.15f);
    [SerializeField] private Color criticalTireColor = new Color(0.9f, 0.2f, 0.18f);
    [SerializeField, Range(0f, 1f)] private float wornThreshold = 0.5f;
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.8f;

    private int currentLap = 1;
    private int currentPosition;
    private int competitorCount;
    private float currentLapTime;
    private bool raceFinished;

    private void Awake()
    {
        totalLaps = Mathf.Max(1, totalLaps);
        currentLap = Mathf.Clamp(currentLap, 1, totalLaps);
        RefreshHud();
    }

    private void Update()
    {
        if (!raceFinished)
        {
            currentLapTime += Time.deltaTime;
        }

        RefreshHud();
    }

    public bool RegisterLap(ArcadeCarController car)
    {
        if (car == null || car != playerCar || raceFinished || currentLapTime < minimumLapTimeSeconds)
        {
            return false;
        }

        if (currentLap >= totalLaps)
        {
            raceFinished = true;
            if (lapsText != null)
            {
                lapsText.text = "Carrera terminada";
            }

            RefreshHud();
            return true;
        }

        currentLap++;
        currentLapTime = 0f;
        RefreshHud();
        return true;
    }

    public void SetClassification(int position, int totalCompetitors)
    {
        competitorCount = Mathf.Max(0, totalCompetitors);
        currentPosition = competitorCount > 0 ? Mathf.Clamp(position, 1, competitorCount) : 0;
        RefreshPosition();
    }

    private void RefreshHud()
    {
        if (lapsText != null && !raceFinished)
        {
            lapsText.text = lapTimeText == null || lapTimeText == lapsText
                ? $"V{currentLap}/{totalLaps} {FormatLapTime(currentLapTime)}"
                : $"Vuelta {currentLap}/{totalLaps}";
        }

        if (lapTimeText != null && lapTimeText != lapsText)
        {
            lapTimeText.text = FormatLapTime(currentLapTime);
        }

        RefreshPosition();
        RefreshFuel();
        RefreshTireWear();
    }

    private void RefreshPosition()
    {
        if (positionText == null)
        {
            return;
        }

        positionText.text = currentPosition > 0
            ? $"Posicion: {currentPosition}/{competitorCount}"
            : "Posicion: --";
    }

    private void RefreshFuel()
    {
        if (playerCar == null)
        {
            return;
        }

        float capacity = Mathf.Max(0.1f, playerCar.FuelCapacityLiters);
        float remaining = Mathf.Clamp(playerCar.FuelRemainingLiters, 0f, capacity);

        if (fuelSlider != null)
        {
            fuelSlider.minValue = 0f;
            fuelSlider.maxValue = capacity;
            fuelSlider.value = remaining;
            fuelSlider.interactable = false;
        }

        if (fuelText != null)
        {
            fuelText.text = $"Gas {remaining:F1}/{capacity:F1}L";
        }
    }

    private void RefreshTireWear()
    {
        float frontLeftWear = GetWear(frontLeftTire);
        float frontRightWear = GetWear(frontRightTire);
        float rearLeftWear = GetWear(rearLeftTire);
        float rearRightWear = GetWear(rearRightTire);

        SetIndicator(frontLeftIndicator, frontLeftWear);
        SetIndicator(frontRightIndicator, frontRightWear);
        SetIndicator(rearLeftIndicator, rearLeftWear);
        SetIndicator(rearRightIndicator, rearRightWear);

        if (tireWearText != null)
        {
            float averageWear = (frontLeftWear + frontRightWear + rearLeftWear + rearRightWear) * 0.25f;
            tireWearText.text = $"Desgaste {averageWear:F0}%";
        }
    }

    private void SetIndicator(Image indicator, float wearPercent)
    {
        if (indicator == null)
        {
            return;
        }

        float wearRatio = Mathf.Clamp01(wearPercent / 100f);
        indicator.color = wearRatio >= criticalThreshold
            ? criticalTireColor
            : wearRatio >= wornThreshold
                ? wornTireColor
                : freshTireColor;
    }

    private static float GetWear(WheelTireWear tire)
    {
        return tire != null ? tire.WearPercent : 0f;
    }

    private static string FormatLapTime(float timeSeconds)
    {
        int totalHundredths = Mathf.FloorToInt(Mathf.Max(0f, timeSeconds) * 100f);
        int minutes = totalHundredths / 6000;
        int seconds = totalHundredths / 100 % 60;
        int hundredths = totalHundredths % 100;
        return $"{minutes:00}:{seconds:00}.{hundredths:00}";
    }
}
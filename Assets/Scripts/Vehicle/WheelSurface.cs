using UnityEngine;

[DisallowMultipleComponent]
public sealed class WheelSurface : MonoBehaviour
{
    [SerializeField] private string surfaceName = "Asphalt";
    [SerializeField, Range(0.1f, 2f)] private float gripMultiplier = 1f;
    [SerializeField, Min(0f)] private float wearMultiplier = 1f;

    [Header("Off-track")]
    [Tooltip("Fracción de la velocidad que se pierde por segundo con las cuatro ruedas en esta superficie.")]
    [SerializeField, Range(0f, 2f)] private float rollingResistance;
    [Tooltip("Baches: hace botar el coche y vibrar la cámara. 0 = liso.")]
    [SerializeField, Range(0f, 1f)] private float roughness;
    [Tooltip("Lo rápido que se ensucian los neumáticos (por segundo). Sucios agarran menos hasta que se limpian.")]
    [SerializeField, Min(0f)] private float dirtPerSecond;

    public string SurfaceName => surfaceName;
    public float GripMultiplier => gripMultiplier;
    public float WearMultiplier => wearMultiplier;
    public float RollingResistance => rollingResistance;
    public float Roughness => roughness;
    public float DirtPerSecond => dirtPerSecond;
}

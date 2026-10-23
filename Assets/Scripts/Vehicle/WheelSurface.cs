using UnityEngine;

[DisallowMultipleComponent]
public sealed class WheelSurface : MonoBehaviour
{
    [SerializeField] private string surfaceName = "Asphalt";
    [SerializeField, Range(0.1f, 2f)] private float gripMultiplier = 1f;
    [SerializeField, Min(0f)] private float wearMultiplier = 1f;

    public string SurfaceName => surfaceName;
    public float GripMultiplier => gripMultiplier;
    public float WearMultiplier => wearMultiplier;
}
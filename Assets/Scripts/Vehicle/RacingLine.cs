using System.Collections.Generic;
using UnityEngine;

// Trazada cerrada que sigue la IA: puntos de la línea ideal, velocidad objetivo en cada uno
// y espacio libre a cada lado para adelantar. Se genera desde el menú Grip > Generar trazada de la IA.
[DisallowMultipleComponent]
public sealed class RacingLine : MonoBehaviour
{
    [Header("Detección del asfalto")]
    [Tooltip("Materiales por los que se puede circular (asfalto y pianos).")]
    [SerializeField] private string[] drivableMaterialKeywords = { "road", "red", "white" };
    [SerializeField, Min(1f)] private float scanHalfWidth = 25f;
    [SerializeField, Min(0.05f)] private float scanResolution = 0.25f;
    [SerializeField, Min(0.25f)] private float walkStep = 1f;
    [SerializeField, Min(100)] private int maximumWalkSteps = 6000;

    [Header("Trazada")]
    [SerializeField, Min(0.5f)] private float pointSpacing = 3f;
    [Tooltip("Distancia mínima al borde del asfalto que respeta la trazada.")]
    [SerializeField, Min(0f)] private float edgeMargin = 1.2f;
    [Tooltip("Más iteraciones = trazada más abierta (más 'de carreras'), menos = más pegada al centro.")]
    [SerializeField, Range(0, 2000)] private int racingLineIterations = 600;

    [Header("Velocidades")]
    [Tooltip("Aceleración lateral máxima en curva (m/s²). Sube para que la IA entre más rápido en las curvas.")]
    [SerializeField, Min(1f)] private float maximumLateralAcceleration = 9f;
    [SerializeField, Min(1f)] private float brakingDeceleration = 8f;
    [SerializeField, Min(1f)] private float minimumSpeedKph = 35f;
    [SerializeField, Min(1f)] private float maximumSpeedKph = 160f;

    [Header("Datos generados")]
    [SerializeField] private Vector3[] points = System.Array.Empty<Vector3>();
    [SerializeField] private Vector3[] rights = System.Array.Empty<Vector3>();
    [SerializeField] private float[] speedsKph = System.Array.Empty<float>();
    [SerializeField] private float[] leftRoom = System.Array.Empty<float>();
    [SerializeField] private float[] rightRoom = System.Array.Empty<float>();

    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    public int Count => points.Length;
    public float PointSpacing => pointSpacing;

    public Vector3 GetPoint(int index) => points[Wrap(index)];
    public Vector3 GetRight(int index) => rights[Wrap(index)];
    public float GetSpeedKph(int index) => speedsKph[Wrap(index)];
    public float GetLeftRoom(int index) => leftRoom[Wrap(index)];
    public float GetRightRoom(int index) => rightRoom[Wrap(index)];

    public int Wrap(int index)
    {
        int count = points.Length;
        return ((index % count) + count) % count;
    }

    public int FindClosestIndex(Vector3 position)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            float distance = (points[i] - position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    // Busca solo cerca del índice anterior para no saltar a otra parte del circuito que pase cerca.
    public int FindClosestIndex(Vector3 position, int hint, int searchBehind, int searchAhead)
    {
        int best = Wrap(hint);
        float bestDistance = float.MaxValue;
        for (int offset = -searchBehind; offset <= searchAhead; offset++)
        {
            int i = Wrap(hint + offset);
            float distance = (points[i] - position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    public Vector3 GetPointAhead(int index, float distance)
    {
        float steps = Mathf.Max(0f, distance) / pointSpacing;
        int whole = Mathf.FloorToInt(steps);
        return Vector3.Lerp(GetPoint(index + whole), GetPoint(index + whole + 1), steps - whole);
    }

    // directionHints: direcciones que indican el sentido de carrera, por orden de preferencia (coche, línea de meta...).
    public bool Generate(Vector3 startPosition, Vector3[] directionHints, out string error)
    {
        Physics.SyncTransforms();
        if (!FindTrackDirection(startPosition, directionHints, out Vector3 startForward))
        {
            error = "No hay asfalto debajo del coche del jugador. Colócalo sobre la pista.";
            return false;
        }

        if (!WalkCenterLine(startPosition, startForward, out List<Vector3> rawCenters, out List<float> rawHalfWidths, out error))
        {
            return false;
        }

        Resample(rawCenters, rawHalfWidths, out Vector3[] centers, out float[] halfWidths);
        if (centers.Length < 8)
        {
            error = "La vuelta encontrada es demasiado corta.";
            return false;
        }

        Smooth(centers, 4);
        Vector3[] normals = ComputeRights(centers);
        float[] offsets = BuildRacingLineOffsets(centers, normals, halfWidths);

        int count = centers.Length;
        points = new Vector3[count];
        leftRoom = new float[count];
        rightRoom = new float[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = centers[i] + normals[i] * offsets[i];
            float usable = Mathf.Max(0f, halfWidths[i] - edgeMargin);
            rightRoom[i] = usable - offsets[i];
            leftRoom[i] = usable + offsets[i];
        }

        rights = ComputeRights(points);
        speedsKph = ComputeSpeeds(points);
        error = null;
        return true;
    }

    public void Reverse()
    {
        System.Array.Reverse(points);
        System.Array.Reverse(leftRoom);
        System.Array.Reverse(rightRoom);
        // Al ir al revés, la izquierda pasa a ser la derecha.
        (leftRoom, rightRoom) = (rightRoom, leftRoom);
        rights = ComputeRights(points);
        speedsKph = ComputeSpeeds(points);
    }

    // La pista va en la dirección en la que el asfalto es más estrecho de lado a lado.
    private bool FindTrackDirection(Vector3 start, Vector3[] hints, out Vector3 direction)
    {
        direction = Vector3.forward;
        float narrowest = float.MaxValue;
        for (int angle = 0; angle < 180; angle += 3)
        {
            Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (ScanSection(start, candidate, start.y, out _, out float halfWidth) && halfWidth < narrowest)
            {
                narrowest = halfWidth;
                direction = candidate;
            }
        }

        if (narrowest == float.MaxValue)
        {
            return false;
        }

        if (hints != null)
        {
            foreach (Vector3 hint in hints)
            {
                float alignment = Vector3.Dot(direction, Flatten(hint));
                if (Mathf.Abs(alignment) > 0.5f)
                {
                    direction *= Mathf.Sign(alignment);
                    break;
                }
            }
        }

        return true;
    }

    private bool WalkCenterLine(Vector3 start, Vector3 forward, out List<Vector3> centers, out List<float> halfWidths, out string error)
    {
        centers = new List<Vector3>();
        halfWidths = new List<float>();
        Vector3 heading = Flatten(forward);

        if (!ScanSection(start, heading, start.y, out Vector3 center, out float halfWidth))
        {
            error = "No hay asfalto debajo del coche del jugador. Colócalo sobre la pista.";
            return false;
        }

        Vector3 origin = center;
        centers.Add(center);
        halfWidths.Add(halfWidth);
        float averageWidth = halfWidth;
        float travelled = 0f;

        for (int step = 0; step < maximumWalkSteps; step++)
        {
            Vector3 probe = center + heading * walkStep;
            if (!ScanSection(probe, heading, center.y, out Vector3 nextCenter, out float nextHalfWidth))
            {
                error = $"Se perdió el asfalto cerca de {probe}. Revisa que esa pieza tenga collider.";
                return false;
            }

            // En cruces o en la entrada del pit lane el asfalto se ensancha: seguir recto en vez de desviarse al centro del ensanche.
            if (step > 20 && nextHalfWidth > averageWidth * 1.5f)
            {
                nextCenter = new Vector3(probe.x, nextCenter.y, probe.z);
                nextHalfWidth = averageWidth;
            }
            else
            {
                averageWidth = Mathf.Lerp(averageWidth, nextHalfWidth, 0.05f);
            }

            Vector3 newHeading = Flatten(nextCenter - center);
            heading = Vector3.Slerp(heading, newHeading, 0.7f).normalized;
            travelled += Vector3.Distance(center, nextCenter);
            center = nextCenter;

            if (travelled > 50f && Vector3.Distance(center, origin) < walkStep * 1.5f)
            {
                error = null;
                return true;
            }

            centers.Add(center);
            halfWidths.Add(nextHalfWidth);
        }

        error = "No se pudo cerrar la vuelta. Revisa que el circuito sea un circuito cerrado.";
        return false;
    }

    private bool ScanSection(Vector3 position, Vector3 heading, float referenceHeight, out Vector3 center, out float halfWidth)
    {
        Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
        int sampleCount = Mathf.CeilToInt(scanHalfWidth * 2f / scanResolution) + 1;
        bool[] drivable = new bool[sampleCount];
        float[] heights = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float offset = -scanHalfWidth + i * scanResolution;
            Vector3 origin = position + right * offset;
            origin.y = referenceHeight + 3f;
            drivable[i] = SampleSurface(origin, out heights[i]);
        }

        int middle = Mathf.RoundToInt(scanHalfWidth / scanResolution);
        int seed = -1;
        for (int distance = 0; distance <= Mathf.RoundToInt(3f / scanResolution) && seed < 0; distance++)
        {
            if (middle - distance >= 0 && drivable[middle - distance])
            {
                seed = middle - distance;
            }
            else if (middle + distance < sampleCount && drivable[middle + distance])
            {
                seed = middle + distance;
            }
        }

        if (seed < 0)
        {
            center = position;
            halfWidth = 0f;
            return false;
        }

        const int allowedGap = 2;
        int left = ExpandEdge(drivable, seed, -1, allowedGap);
        int rightEdge = ExpandEdge(drivable, seed, 1, allowedGap);
        float leftOffset = -scanHalfWidth + left * scanResolution;
        float rightOffset = -scanHalfWidth + rightEdge * scanResolution;
        int centerSample = (left + rightEdge) / 2;

        center = position + right * ((leftOffset + rightOffset) * 0.5f);
        center.y = drivable[centerSample] ? heights[centerSample] : heights[seed];
        halfWidth = (rightOffset - leftOffset) * 0.5f;
        return true;
    }

    private static int ExpandEdge(bool[] drivable, int seed, int direction, int allowedGap)
    {
        int edge = seed;
        int gap = 0;
        for (int i = seed + direction; i >= 0 && i < drivable.Length; i += direction)
        {
            if (drivable[i])
            {
                edge = i;
                gap = 0;
            }
            else if (++gap > allowedGap)
            {
                break;
            }
        }

        return edge;
    }

    private bool SampleSurface(Vector3 origin, out float height)
    {
        height = origin.y;
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < hitCount; i++)
        {
            // Los coches tienen Rigidbody: se ignoran para que no tapen la pista.
            if (hitBuffer[i].collider.attachedRigidbody != null)
            {
                continue;
            }

            if (nearest < 0 || hitBuffer[i].distance < hitBuffer[nearest].distance)
            {
                nearest = i;
            }
        }

        if (nearest < 0)
        {
            return false;
        }

        height = hitBuffer[nearest].point.y;
        return IsDrivable(hitBuffer[nearest]);
    }

    private bool IsDrivable(RaycastHit hit)
    {
        MeshCollider meshCollider = hit.collider as MeshCollider;
        Renderer surfaceRenderer = hit.collider.GetComponent<Renderer>();
        if (meshCollider == null || meshCollider.sharedMesh == null || surfaceRenderer == null || hit.triangleIndex < 0)
        {
            return false;
        }

        Mesh mesh = meshCollider.sharedMesh;
        int indexPosition = hit.triangleIndex * 3;
        Material[] materials = surfaceRenderer.sharedMaterials;
        for (int subMesh = 0; subMesh < mesh.subMeshCount && subMesh < materials.Length; subMesh++)
        {
            UnityEngine.Rendering.SubMeshDescriptor descriptor = mesh.GetSubMesh(subMesh);
            if (indexPosition < descriptor.indexStart || indexPosition >= descriptor.indexStart + descriptor.indexCount)
            {
                continue;
            }

            if (materials[subMesh] == null)
            {
                return false;
            }

            string materialName = materials[subMesh].name.ToLowerInvariant();
            foreach (string keyword in drivableMaterialKeywords)
            {
                if (!string.IsNullOrEmpty(keyword) && materialName.Contains(keyword.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private void Resample(List<Vector3> rawCenters, List<float> rawHalfWidths, out Vector3[] centers, out float[] halfWidths)
    {
        int rawCount = rawCenters.Count;
        float[] cumulative = new float[rawCount + 1];
        for (int i = 0; i < rawCount; i++)
        {
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(rawCenters[i], rawCenters[(i + 1) % rawCount]);
        }

        float length = cumulative[rawCount];
        int count = Mathf.Max(3, Mathf.RoundToInt(length / pointSpacing));
        float spacing = length / count;
        centers = new Vector3[count];
        halfWidths = new float[count];

        int segment = 0;
        for (int i = 0; i < count; i++)
        {
            float distance = i * spacing;
            while (segment < rawCount - 1 && cumulative[segment + 1] < distance)
            {
                segment++;
            }

            float segmentLength = cumulative[segment + 1] - cumulative[segment];
            float t = segmentLength > 0f ? (distance - cumulative[segment]) / segmentLength : 0f;
            int next = (segment + 1) % rawCount;
            centers[i] = Vector3.Lerp(rawCenters[segment], rawCenters[next], t);
            halfWidths[i] = Mathf.Lerp(rawHalfWidths[segment], rawHalfWidths[next], t);
        }

        pointSpacing = spacing;
    }

    private static void Smooth(Vector3[] values, int passes)
    {
        int count = values.Length;
        Vector3[] buffer = new Vector3[count];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[i] = (values[(i - 1 + count) % count] + values[i] * 2f + values[(i + 1) % count]) * 0.25f;
            }

            System.Array.Copy(buffer, values, count);
        }
    }

    private static Vector3[] ComputeRights(Vector3[] line)
    {
        int count = line.Length;
        Vector3[] result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = Flatten(line[(i + 1) % count] - line[(i - 1 + count) % count]);
            result[i] = Vector3.Cross(Vector3.up, tangent).normalized;
        }

        return result;
    }

    // Banda elástica: cada punto se acerca al punto medio de sus vecinos sin salirse del asfalto.
    // El resultado abre las curvas (exterior-interior-exterior) como una trazada real.
    private float[] BuildRacingLineOffsets(Vector3[] centers, Vector3[] normals, float[] halfWidths)
    {
        int count = centers.Length;
        float[] offsets = new float[count];
        int reach = Mathf.Clamp(Mathf.RoundToInt(9f / pointSpacing), 1, count / 4);

        for (int iteration = 0; iteration < racingLineIterations; iteration++)
        {
            for (int i = 0; i < count; i++)
            {
                int previous = (i - reach + count) % count;
                int next = (i + reach) % count;
                Vector3 previousPoint = centers[previous] + normals[previous] * offsets[previous];
                Vector3 nextPoint = centers[next] + normals[next] * offsets[next];
                Vector3 target = (previousPoint + nextPoint) * 0.5f;
                float limit = Mathf.Max(0f, halfWidths[i] - edgeMargin);
                offsets[i] = Mathf.Clamp(Vector3.Dot(target - centers[i], normals[i]), -limit, limit);
            }
        }

        return offsets;
    }

    private float[] ComputeSpeeds(Vector3[] line)
    {
        int count = line.Length;
        float[] speeds = new float[count];
        float maximumSpeed = maximumSpeedKph / 3.6f;
        float minimumSpeed = Mathf.Min(minimumSpeedKph, maximumSpeedKph) / 3.6f;
        int reach = Mathf.Clamp(Mathf.RoundToInt(6f / pointSpacing), 1, count / 4);

        for (int i = 0; i < count; i++)
        {
            float radius = CircumRadius(line[(i - reach + count) % count], line[i], line[(i + reach) % count]);
            speeds[i] = Mathf.Clamp(Mathf.Sqrt(maximumLateralAcceleration * radius), minimumSpeed, maximumSpeed);
        }

        // Frenada: cada punto no puede ir más rápido de lo que permite frenar a tiempo para el siguiente.
        for (int lap = 0; lap < 2; lap++)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                int next = (i + 1) % count;
                float distance = Vector3.Distance(line[i], line[next]);
                speeds[i] = Mathf.Min(speeds[i], Mathf.Sqrt(speeds[next] * speeds[next] + 2f * brakingDeceleration * distance));
            }
        }

        for (int i = 0; i < count; i++)
        {
            speeds[i] *= 3.6f;
        }

        return speeds;
    }

    private static float CircumRadius(Vector3 a, Vector3 b, Vector3 c)
    {
        a.y = b.y = c.y = 0f;
        float ab = Vector3.Distance(a, b);
        float bc = Vector3.Distance(b, c);
        float ca = Vector3.Distance(c, a);
        float doubleArea = Vector3.Cross(b - a, c - a).magnitude;
        return doubleArea < 0.0001f ? float.MaxValue : ab * bc * ca / (2f * doubleArea);
    }

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
    }

    private void OnDrawGizmos()
    {
        if (points == null || points.Length < 2 || speedsKph.Length != points.Length)
        {
            return;
        }

        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            float speed01 = Mathf.InverseLerp(minimumSpeedKph, maximumSpeedKph, speedsKph[i]);
            Gizmos.color = Color.Lerp(Color.red, Color.green, speed01);
            Gizmos.DrawLine(points[i] + Vector3.up * 0.3f, points[next] + Vector3.up * 0.3f);
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            Gizmos.DrawLine(points[i] - rights[i] * leftRoom[i], points[next] - rights[next] * leftRoom[next]);
            Gizmos.DrawLine(points[i] + rights[i] * rightRoom[i], points[next] + rights[next] * rightRoom[next]);
        }
    }
}

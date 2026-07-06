using System.Collections.Generic;
using DualPantoToolkit;
using UnityEngine;
using System.Threading.Tasks;
using System.Linq;

public class DrawPen : MonoBehaviour
{
    public GameObject rootObject;

    public SpeechRecognizer speechRecognizer;

    [SerializeField] private float minPointDistance = 0.01f;
    [SerializeField] private float lineWidth = 0.01f;
    [SerializeField] private Color lineColor = Color.black;

    [Header("Obstacle Generation")]
    [Tooltip("Vertical size (world units) of the spawned obstacle cubes.")]
    [SerializeField] private float obstacleHeight = 0.1f;
    [Tooltip("Number of box segments used to approximate a recognized circle.")]
    [SerializeField] private int circleSegments = 24;
    [Tooltip("Thickness (world units) of each box edge forming a circle ring or a triangle outline.")]
    [SerializeField] private float polygonEdgeThickness = 0.05f;
    [Tooltip("Thickness (world units) of the stretched cube spawned for a recognized straight line.")]
    [SerializeField] private float lineThickness = 0.05f;
    [Tooltip("Side length (world units) of the cube spawned at a recognized circle's center point.")]
    [SerializeField] private float circleCenterSize = 0.05f;

    [Header("Sound")]
    [SerializeField] private AudioClip drawSound;

    [Header("Obstacles")]
    public List<Shape> shapes = new List<Shape>();

    private bool drawing = false;
    private readonly List<Vector2> samplePoints = new List<Vector2>();
    private readonly Dictionary<string, int> shapeTypeCounts = new Dictionary<string, int>();

    private LowerHandle pen;
    private DollarRecognizer recognizer;
    private Transform shapesRoot;
    private LineRenderer previewLine;
    private GameObject previewObject;

    private AudioSource audioSource;

    void Start()
    {
        pen = GetComponent<LowerHandle>();
        if (pen == null)
        {
            pen = FindAnyObjectByType<LowerHandle>();
        }

        recognizer = new DollarRecognizer();
        RegisterDefaultShapes();
        EnsureShapesRoot();
        audioSource = gameObject.AddComponent<AudioSource>();
        if(drawSound != null)
        {
            audioSource.clip = drawSound;
            audioSource.loop = true;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.B))
        {
            BeginDrawing();
            Debug.Log("Started drawing");
        }

        if (drawing && (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.B)))
        {
            CaptureSamplePoint();
            Debug.Log($"Captured sample point, total points: {samplePoints.Count}");
        }

        if (Input.GetKeyUp(KeyCode.Space) || Input.GetKeyUp(KeyCode.B))
        {
            FinishDrawing();
            Debug.Log("Finished drawing");
        }
    }

    private void BeginDrawing()
    {
        drawing = true;
        samplePoints.Clear();
        CaptureSamplePoint();
        EnsureShapesRoot();
        CreatePreviewLine();

        if (audioSource != null)
        {
            audioSource.Play();
        }
    }

    private void CaptureSamplePoint()
    {
        if (pen == null)
        {
            return;
        }

        Vector3 handlePosition = pen.GetPosition();
        Vector2 samplePoint = new Vector2(handlePosition.x, handlePosition.z);

        if (samplePoints.Count > 0 && Vector2.Distance(samplePoints[samplePoints.Count - 1], samplePoint) < minPointDistance)
        {
            return;
        }

        samplePoints.Add(samplePoint);
        UpdatePreviewLine();
    }

    private async void FinishDrawing()
    {
        if(drawSound != null)
        {
            audioSource.Stop();
        }

        if (!drawing)
        {
            return;
        }

        drawing = false;
        CaptureSamplePoint();
        DestroyPreviewLine();

        if (samplePoints.Count < 8)
        {
            Debug.Log($"[DrawPen] Not enough sample points to recognize a shape ({samplePoints.Count}/8).");
            samplePoints.Clear();
            return;
        }

        DollarRecognizer.Result result = recognizer.Recognize(samplePoints);
        if (result.Match == null)
        {
            samplePoints.Clear();
            return;
        }

        Debug.Log($"[DrawPen] Recognized '{result.Match.Name}'.");
        string drawnShapeName = DrawRecognizedShape(result.Match.Name, samplePoints);
        samplePoints.Clear();

        // Update the speech recognizer with the new list of shapes
        // if (speechRecognizer != null)
        // {
        //     speechRecognizer.UpdateShapeList(shapes);
        // }
        
        SpeechManager.Instance.Speak("draw [" + drawnShapeName + "]");
        await Task.Delay(100); // Wait a short time to ensure the new shapes are fully initialized
        rootObject.GetComponent<PantoMeshCollider>().Remove();
        await Task.Delay(100); // Wait a short time to ensure the previous obstacles are fully removed
        rootObject.GetComponent<PantoMeshCollider>().CreateObstacle();
        rootObject.GetComponent<PantoMeshCollider>().Enable();
    }

    private void CreatePreviewLine()
    {
        DestroyPreviewLine();

        previewObject = new GameObject("PreviewLine");
        previewObject.transform.SetParent(shapesRoot, true);
        previewLine = previewObject.AddComponent<LineRenderer>();
        previewLine.useWorldSpace = true;
        previewLine.loop = false;
        previewLine.startWidth = lineWidth;
        previewLine.endWidth = lineWidth;
        previewLine.startColor = lineColor;
        previewLine.endColor = lineColor;
        previewLine.numCapVertices = 4;
        previewLine.numCornerVertices = 4;
        previewLine.alignment = LineAlignment.View;
        previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewLine.receiveShadows = false;
        previewLine.allowOcclusionWhenDynamic = false;
        Material pmat = CreateLineMaterial();
        if (pmat != null) pmat.color = lineColor;
        previewLine.material = pmat;
        previewLine.positionCount = 0;
    }

    private void UpdatePreviewLine()
    {
        if (previewLine == null) return;
        int count = samplePoints.Count;
        previewLine.positionCount = count;
        if (count == 0) return;
        Vector3[] positions = new Vector3[count];
        float y = GetShapeHeight();
        for (int i = 0; i < count; i++)
        {
            Vector2 p = samplePoints[i];
            positions[i] = new Vector3(p.x, y, p.y);
        }
        previewLine.SetPositions(positions);
    }

    private void DestroyPreviewLine()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
            previewLine = null;
        }
    }

    private string DrawRecognizedShape(string shapeName, List<Vector2> strokePoints)
    {
        EnsureShapesRoot();

        string displayName = GetNextShapeDisplayName(shapeName);
        GameObject shapeObject = new GameObject(displayName);
        shapeObject.transform.SetParent(shapesRoot, true);

        Shape shapeComponent = shapeObject.AddComponent<Shape>();
        shapeComponent.shapeName = displayName;
        shapes.Add(shapeComponent);

        float y = GetShapeHeight();

        if (shapeName == "line")
        {
            Vector2 start = strokePoints[0];
            Vector2 end = strokePoints[strokePoints.Count - 1];
            CreateSpawnPoint(shapeComponent, shapeObject.transform, (start + end) * 0.5f, y);
            SpawnLineObstacle(shapeObject.transform, start, end, y);
            return displayName;
        }

        if (shapeName == "circle")
        {
            Vector2 center = GetCentroid(strokePoints);
            Vector2 size = GetBoundsSize(strokePoints);
            float radius = Mathf.Max(Mathf.Max(size.x, size.y) * 0.5f, 0.005f);
            // A point on the circumference (the circle's "wall"), not its empty centre.
            CreateSpawnPoint(shapeComponent, shapeObject.transform, center + new Vector2(radius-(polygonEdgeThickness*0.5f), 0f), y);
            SpawnCircleObstacle(shapeObject.transform, center, radius, y);
            CreateCircleCenterShape(center, y);
        }
        else if (shapeName == "triangle")
        {
            // Like the rectangle's bounding box, the inscribed triangle is derived from stroke
            // geometry (max-area triangle inscribed in the convex hull) rather than the
            // recognizer's normalization angle, so any triangle shape/orientation comes out right.
            List<Vector2> hull = ConvexHull(strokePoints);
            if (hull.Count >= 3)
            {
                Vector2 a, b, c;
                ComputeMaxAreaTriangle(hull, out a, out b, out c);
                // Midpoint of one edge (a-b) — a point on the triangle's wall.
                CreateSpawnPoint(shapeComponent, shapeObject.transform, (a + b) * 0.5f, y);
                SpawnTriangleObstacle(shapeObject.transform, a, b, c, y);
            }
        }
        else
        {
            // Use the true minimum-area oriented bounding box of the stroke rather than the
            // recognizer's normalization angle, which reflects how $1 aligned the stroke to its
            // template (e.g. which corner you started from) and not the rectangle's real tilt.
            // Relying on it swaps width/height for non-square rectangles whenever it picks a
            // 90°/270° offset; a square hides the bug because swapping width and height is a no-op.
            Vector2 obbCenter, obbSize;
            float obbAngleDeg;
            ComputeOrientedBoundingBox(strokePoints, out obbCenter, out obbSize, out obbAngleDeg);
            Vector2 clampedSize = new Vector2(Mathf.Max(obbSize.x, 0.01f), Mathf.Max(obbSize.y, 0.01f));

            // Midpoint of one wall (the box's local -Z edge), not the empty centre.
            Vector3 edgeOffsetWorld = Quaternion.Euler(0f, obbAngleDeg, 0f) * new Vector3(0f, 0f, -clampedSize.y * 0.5f);
            Vector2 wallPoint = obbCenter + new Vector2(edgeOffsetWorld.x, edgeOffsetWorld.z);
            CreateSpawnPoint(shapeComponent, shapeObject.transform, wallPoint, y);

            SpawnRectangleObstacle(shapeObject.transform, obbCenter, clampedSize, obbAngleDeg, y);
        }

        return displayName;
    }

    /// <summary>Registers a recognized circle's center point as its own Shape entry (cube obstacle + spawn point), not just a child of the circle.</summary>
    private void CreateCircleCenterShape(Vector2 center, float y)
    {
        string displayName = GetNextShapeDisplayName("circle center");
        GameObject centerObject = new GameObject(displayName);
        centerObject.transform.SetParent(shapesRoot, true);

        Shape centerShapeComponent = centerObject.AddComponent<Shape>();
        centerShapeComponent.shapeName = displayName;
        shapes.Add(centerShapeComponent);

        CreateSpawnPoint(centerShapeComponent, centerObject.transform, center, y);
        SpawnCircleCenterObstacle(centerObject.transform, center, y);
    }

    private static void CreateSpawnPoint(Shape shapeComponent, Transform parent, Vector2 worldXZ, float y)
    {
        GameObject spawnPointObject = new GameObject("SpawnPoint");
        spawnPointObject.transform.SetParent(parent, worldPositionStays: true);
        spawnPointObject.transform.position = new Vector3(worldXZ.x, y, worldXZ.y);
        shapeComponent.spawnPoint = spawnPointObject.transform;
    }

    private void SpawnLineObstacle(Transform parent, Vector2 start, Vector2 end, float y)
    {
        float length = Mathf.Max(Vector2.Distance(start, end), 0.01f);
        Vector2 mid = (start + end) * 0.5f;
        Vector2 dir = (end - start).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "LineBlock";
        block.transform.SetParent(parent, worldPositionStays: true);
        block.transform.position = new Vector3(mid.x, y, mid.y);
        block.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
        block.transform.localScale = new Vector3(lineThickness, obstacleHeight, length);
    }

    private void SpawnRectangleObstacle(Transform parent, Vector2 center, Vector2 size, float rotationAngleDeg, float y)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Block";
        block.transform.SetParent(parent, worldPositionStays: true);
        block.transform.position = new Vector3(center.x, y, center.y);
        block.transform.rotation = Quaternion.Euler(0f, rotationAngleDeg, 0f);
        block.transform.localScale = new Vector3(size.x, obstacleHeight, size.y);
    }

    private void SpawnCircleObstacle(Transform parent, Vector2 center, float radius, float y)
    {
        int segments = Mathf.Max(8, circleSegments);

        for (int i = 0; i < segments; i++)
        {
            float angleA = (Mathf.PI * 2f * i) / segments;
            float angleB = (Mathf.PI * 2f * (i + 1)) / segments;

            Vector2 a = center + new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA)) * radius;
            Vector2 b = center + new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB)) * radius;
            SpawnCircleEdgeBox(parent, center, a, b, y, $"CircleSegment_{i:D2}");
        }
    }

    /// <summary>
    /// Like SpawnEdgeBox, but shifts the box fully inward (toward the circle's center) instead
    /// of centering its thickness on the chord. Neighboring segments then share their outer edge
    /// exactly at the sampled circle points, so their outer corners no longer overlap/pinch and
    /// split the generated obstacle mesh; only the inner ("bottom") edge fans in slightly.
    /// </summary>
    private void SpawnCircleEdgeBox(Transform parent, Vector2 center, Vector2 a, Vector2 b, float y, string name)
    {
        float length = Vector2.Distance(a, b);
        if (length < 0.001f) return;

        Vector2 mid = (a + b) * 0.5f;
        Vector2 dir = (b - a).normalized;
        Vector2 outward = (mid - center).normalized;
        Vector2 boxCenter = mid - (outward * (polygonEdgeThickness * 0.5f));

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        segment.name = name;
        segment.transform.SetParent(parent, worldPositionStays: true);
        segment.transform.position = new Vector3(boxCenter.x, y, boxCenter.y);
        segment.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
        segment.transform.localScale = new Vector3(polygonEdgeThickness, obstacleHeight, length);
    }

    private void SpawnCircleCenterObstacle(Transform parent, Vector2 center, float y)
    {
        GameObject centerBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        centerBlock.name = "CircleCenter";
        centerBlock.transform.SetParent(parent, worldPositionStays: true);
        centerBlock.transform.position = new Vector3(center.x, y, center.y);
        centerBlock.transform.localScale = new Vector3(circleCenterSize, obstacleHeight, circleCenterSize);
    }

    private void SpawnTriangleObstacle(Transform parent, Vector2 a, Vector2 b, Vector2 c, float y)
    {
        SpawnEdgeBox(parent, a, b, y, "TriangleEdge_0");
        SpawnEdgeBox(parent, b, c, y, "TriangleEdge_1");
        SpawnEdgeBox(parent, c, a, y, "TriangleEdge_2");
    }

    private void SpawnEdgeBox(Transform parent, Vector2 a, Vector2 b, float y, string name)
    {
        float length = Vector2.Distance(a, b);
        if (length < 0.001f) return;

        Vector2 mid = (a + b) * 0.5f;
        Vector2 dir = (b - a).normalized;

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        segment.name = name;
        segment.transform.SetParent(parent, worldPositionStays: true);
        segment.transform.position = new Vector3(mid.x, y, mid.y);
        segment.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
        segment.transform.localScale = new Vector3(polygonEdgeThickness, obstacleHeight, length);
    }

    /// <summary>Brute-force maximum-area triangle inscribed in a convex polygon (hull is small for hand-drawn strokes).</summary>
    private static void ComputeMaxAreaTriangle(List<Vector2> hull, out Vector2 a, out Vector2 b, out Vector2 c)
    {
        int n = hull.Count;
        float bestArea = -1f;
        a = hull[0];
        b = hull[1 % n];
        c = hull[2 % n];

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                for (int k = j + 1; k < n; k++)
                {
                    float area = Mathf.Abs(Cross(hull[i], hull[j], hull[k])) * 0.5f;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        a = hull[i]; b = hull[j]; c = hull[k];
                    }
                }
            }
        }
    }

    private void RegisterDefaultShapes()
    {
        recognizer.SavePattern("rectangle", BuildRectangleTemplate());
        recognizer.SavePattern("circle", BuildCircleTemplate());
        recognizer.SavePattern("line", BuildLineTemplate());
        recognizer.SavePattern("triangle", BuildTriangleTemplate());
    }

    private static List<Vector2> BuildLineTemplate()
    {
        List<Vector2> points = new List<Vector2>();
        // line from left to right
        AddEdge(points, new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f), 32);
        return points;
    }

    private static List<Vector2> BuildRectangleTemplate()
    {
        List<Vector2> points = new List<Vector2>();
        AddEdge(points, new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), 16);
        AddEdge(points, new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), 16);
        AddEdge(points, new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f), 16);
        AddEdge(points, new Vector2(-0.5f, 0.5f), new Vector2(-0.5f, -0.5f), 16);
        return points;
    }

    private static List<Vector2> BuildTriangleTemplate()
    {
        Vector2 top = new Vector2(0.0f, 0.57735f);
        Vector2 left = new Vector2(-0.5f, -0.288675f);
        Vector2 right = new Vector2(0.5f, -0.288675f);

        List<Vector2> points = new List<Vector2>();
        AddEdge(points, left, top, 21);
        AddEdge(points, top, right, 21);
        AddEdge(points, right, left, 22);
        return points;
    }

    private static List<Vector2> BuildCircleTemplate()
    {
        const int pointCount = 64;
        const float radius = 0.5f;
        List<Vector2> points = new List<Vector2>(pointCount);

        for (int i = 0; i < pointCount; i++)
        {
            float angle = (Mathf.PI * 2.0f * i) / pointCount;
            points.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
        }

        return points;
    }

    private static void AddEdge(List<Vector2> points, Vector2 start, Vector2 end, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            float t = (float)i / steps;
            points.Add(Vector2.Lerp(start, end, t));
        }
    }

    private static Vector2 GetCentroid(List<Vector2> points)
    {
        Vector2 sum = Vector2.zero;

        for (int i = 0; i < points.Count; i++)
        {
            sum += points[i];
        }

        return sum / points.Count;
    }

    private static Vector2 GetBoundsSize(List<Vector2> points)
    {
        Vector2 min = points[0];
        Vector2 max = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        return max - min;
    }

    /// <summary>
    /// Finds the true minimum-area rectangle enclosing the stroke (rotating calipers over the
    /// convex hull), so non-square and arbitrarily rotated rectangles come out with the correct
    /// width, height and tilt — independent of where on the shape the user started drawing.
    /// </summary>
    private static void ComputeOrientedBoundingBox(List<Vector2> points, out Vector2 center, out Vector2 size, out float angleDeg)
    {
        List<Vector2> hull = ConvexHull(points);

        if (hull.Count < 3)
        {
            center = GetCentroid(points);
            size = GetBoundsSize(points);
            angleDeg = 0f;
            return;
        }

        float bestArea = float.PositiveInfinity;
        Vector2 bestCenter = Vector2.zero;
        Vector2 bestSize = Vector2.one;
        float bestAngle = 0f;

        int n = hull.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 edge = hull[(i + 1) % n] - hull[i];
            if (edge.sqrMagnitude < 1e-10f) continue;
            float angle = Mathf.Atan2(edge.y, edge.x);
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);

            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int j = 0; j < n; j++)
            {
                Vector2 p = hull[j];
                // Rotate into the edge's local frame (rotate by -angle)
                float rx = (p.x * c) + (p.y * s);
                float ry = (-p.x * s) + (p.y * c);
                min.x = Mathf.Min(min.x, rx); max.x = Mathf.Max(max.x, rx);
                min.y = Mathf.Min(min.y, ry); max.y = Mathf.Max(max.y, ry);
            }

            float width = max.x - min.x;
            float height = max.y - min.y;
            float area = width * height;

            if (area < bestArea)
            {
                bestArea = area;
                bestSize = new Vector2(width, height);
                bestAngle = angle;

                Vector2 localCenter = (min + max) * 0.5f;
                // Rotate the local-frame centre back into world space (rotate by +angle)
                bestCenter = new Vector2(
                    (localCenter.x * c) - (localCenter.y * s),
                    (localCenter.x * s) + (localCenter.y * c));
            }
        }

        center = bestCenter;
        size = bestSize;
        angleDeg = bestAngle * Mathf.Rad2Deg;
    }

    /// <summary>Andrew's monotone chain convex hull, CCW, no duplicate closing point.</summary>
    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        var pts = new List<Vector2>(points);
        pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        int n = pts.Count;
        if (n < 3) return pts;

        var hull = new List<Vector2>();

        for (int i = 0; i < n; i++)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pts[i]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }

        int lowerCount = hull.Count + 1;
        for (int i = n - 2; i >= 0; i--)
        {
            while (hull.Count >= lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pts[i]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }

        hull.RemoveAt(hull.Count - 1); // last point duplicates the first
        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return ((a.x - o.x) * (b.y - o.y)) - ((a.y - o.y) * (b.x - o.x));
    }

    /// <summary>Produces sequential per-type display names ("Rectangle 1", "Rectangle 2", "Circle 1", ...).</summary>
    private string GetNextShapeDisplayName(string shapeName)
    {
        int count = shapeTypeCounts.TryGetValue(shapeName, out int existing) ? existing + 1 : 1;
        shapeTypeCounts[shapeName] = count;

        string capitalized = char.ToUpperInvariant(shapeName[0]) + shapeName.Substring(1);
        return $"{capitalized} {count}";
    }

    private void EnsureShapesRoot()
    {
        // if (shapesRoot != null)
        // {
        //     return;
        // }

        // GameObject rootObject = GameObject.Find("Drawing");
        // if (rootObject == null)
        // {
        //     rootObject = new GameObject("RecognizedShapes");
        // }
        if(rootObject == null)
        {
            rootObject = new GameObject("RecognizedShapes");
        }

        shapesRoot = rootObject.transform;
    }

    private float GetShapeHeight()
    {
        if (pen == null)
        {
            return 0f;
        }

        return pen.GetPosition().y;
    }

    private static Material CreateLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        return new Material(shader);
    }
}

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DualPantoToolkit
{
    /// <summary>
    /// Scans all child BoxColliders (including arbitrarily rotated ones), computes the correct
    /// outer outline (and optionally inner holes) using a boundary-edge classification approach,
    /// and spawns Rail cubes along every border edge.
    ///
    /// Algorithm:
    ///   1. Extract oriented world-space quads from each child BoxCollider (XZ-plane).
    ///   2. For every polygon edge, split it at all intersection points with all other polygons.
    ///   3. Keep only sub-edges whose midpoint lies OUTSIDE every other polygon → outer boundary.
    ///   4. Chain boundary segments into closed outlines.
    ///   5. CCW outlines = outer shells; CW outlines = inner holes.
    ///   6. Spawn a thin Rail cube for each edge.
    ///
    /// Works correctly for: rotated boxes, touching boxes, rings of cubes (with holes),
    /// partially overlapping at any angle, fully separated groups.
    /// </summary>
    [ExecuteAlways]
    public class RailOutlineGenerator : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Rail Settings")]
        [Tooltip("Thickness (world units) of each spawned Rail cube — the narrow side.")]
        public float railThickness = 0.05f;

        [Tooltip("Height (world units) of each spawned Rail cube.")]
        public float railHeight = 0.1f;

        [Tooltip("Y position (world space) at which Rail cubes are placed.")]
        public float railY = 0f;

        [Tooltip("Parent transform for the generated Rails. Defaults to this transform.")]
        public Transform railParent;

        [Tooltip("If true, also spawn Rails along inner hole boundaries " +
                 "(e.g. the hollow centre of a ring of cubes).")]
        public bool generateInline = false;

        [Tooltip("Optional text applied to every spawned Rail component.")]
        public string railText = "";

        [Header("Corner Cleanup")]
        [Tooltip("Segments shorter than this (world units) are removed by Douglas-Peucker simplification. " +
                 "Eliminates the tiny stub segments that appear at corners when cubes don't sit flush. " +
                 "0 = disabled.")]
        public float simplifyTolerance = 0.05f;

        [Tooltip("After simplification, adjacent outline edges are extended to their true geometric " +
                 "intersection so every corner is a clean, exact point. 0 = disabled.")]
        public float cornerCleanupRadius = 0.15f;

        [Header("Debug")]
        public bool drawGizmos = true;

        // ── Runtime state ─────────────────────────────────────────────────────
        private readonly List<List<Vector2>> _inputPolygons = new List<List<Vector2>>();
        private readonly List<List<Vector2>> _outerOutlines = new List<List<Vector2>>();
        private readonly List<List<Vector2>> _innerHoles    = new List<List<Vector2>>();
        private readonly List<GameObject>    _spawnedRails  = new List<GameObject>();

        // ═════════════════════════════════════════════════════════════════════
        // Public API
        // ═════════════════════════════════════════════════════════════════════

        [ContextMenu("Generate Rails")]
        public void Generate()
        {
            Clear();
            CollectPolygons();
            BuildOutlines();
            SpawnRails();
        }

        [ContextMenu("Clear Rails")]
        public void Clear()
        {
            foreach (var go in _spawnedRails)
                if (go != null) DestroyImmediate(go);
            _spawnedRails.Clear();
            _inputPolygons.Clear();
            _outerOutlines.Clear();
            _innerHoles.Clear();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Step 1 – extract oriented quads in XZ from child BoxColliders
        // ═════════════════════════════════════════════════════════════════════

        private void CollectPolygons()
        {
            _inputPolygons.Clear();
            BoxCollider[] cols = GetComponentsInChildren<BoxCollider>(includeInactive: true);

            foreach (var col in cols)
            {
                if (col.gameObject == gameObject) continue; // skip self

                Transform t = col.transform;
                Vector3   c = col.center;
                Vector3   s = col.size * 0.5f;

                // Four XZ corners in local space (use Y=0 mid-plane of box)
                var localCorners = new[]
                {
                    c + new Vector3(-s.x, 0,  s.z),
                    c + new Vector3( s.x, 0,  s.z),
                    c + new Vector3( s.x, 0, -s.z),
                    c + new Vector3(-s.x, 0, -s.z),
                };

                var poly = new List<Vector2>(4);
                foreach (var lc in localCorners)
                {
                    Vector3 w = t.TransformPoint(lc);
                    poly.Add(new Vector2(w.x, w.z));
                }

                _inputPolygons.Add(EnsureCCW(poly));
            }

            Debug.Log($"[RailOutlineGenerator] Collected {_inputPolygons.Count} polygon(s).");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Step 2 – boundary edge classification
        // ═════════════════════════════════════════════════════════════════════

        private void BuildOutlines()
        {
            _outerOutlines.Clear();
            _innerHoles.Clear();

            if (_inputPolygons.Count == 0) return;

            // Collect all boundary segments
            var boundarySegments = new List<(Vector2 a, Vector2 b)>();

            for (int pi = 0; pi < _inputPolygons.Count; pi++)
            {
                var poly = _inputPolygons[pi];
                int  n   = poly.Count;

                for (int ei = 0; ei < n; ei++)
                {
                    Vector2 edgeA = poly[ei];
                    Vector2 edgeB = poly[(ei + 1) % n];

                    // Collect all t-values where other polygons' edges cross this edge
                    var splitTs = new List<float> { 0f, 1f };

                    for (int pj = 0; pj < _inputPolygons.Count; pj++)
                    {
                        if (pj == pi) continue;
                        var other = _inputPolygons[pj];
                        int m     = other.Count;

                        for (int ej = 0; ej < m; ej++)
                        {
                            float t, u;
                            Vector2 pt;
                            if (SegIntersect(edgeA, edgeB, other[ej], other[(ej + 1) % m],
                                             out t, out u, out pt))
                            {
                                splitTs.Add(t);
                            }
                        }
                    }

                    splitTs.Sort();
                    RemoveDuplicates(splitTs, 1e-7f);

                    // Keep sub-edges whose midpoint is outside all other polygons
                    for (int k = 0; k < splitTs.Count - 1; k++)
                    {
                        float   t0  = splitTs[k];
                        float   t1  = splitTs[k + 1];
                        float   tm  = (t0 + t1) * 0.5f;
                        Vector2 mid = Vector2.Lerp(edgeA, edgeB, tm);

                        bool insideOther = false;
                        for (int pj = 0; pj < _inputPolygons.Count && !insideOther; pj++)
                        {
                            if (pj == pi) continue;
                            if (PointInPoly(mid, _inputPolygons[pj]))
                                insideOther = true;
                        }

                        if (!insideOther)
                        {
                            boundarySegments.Add((
                                Vector2.Lerp(edgeA, edgeB, t0),
                                Vector2.Lerp(edgeA, edgeB, t1)
                            ));
                        }
                    }
                }
            }

            // Chain segments into closed outlines
            var outlines = ChainSegments(boundarySegments, snapEps: 1e-5f);

            // Step A: Douglas-Peucker simplification -- removes tiny stub segments at corners
            if (simplifyTolerance > 0f)
            {
                var simplified = new List<List<Vector2>>(outlines.Count);
                foreach (var outline in outlines)
                {
                    var s = DouglasPeucker(outline, simplifyTolerance);
                    if (s.Count >= 3) simplified.Add(s);
                }
                outlines = simplified;
            }

            // Step B: Corner cleanup -- extend/trim adjacent edges to their exact intersection
            if (cornerCleanupRadius > 0f)
            {
                var cleaned = new List<List<Vector2>>(outlines.Count);
                foreach (var outline in outlines)
                {
                    var c = CleanupCorners(outline, cornerCleanupRadius);
                    if (c.Count >= 3) cleaned.Add(c);
                }
                outlines = cleaned;
            }

            foreach (var outline in outlines)
            {
                float area = SignedArea(outline);
                if (area > 0f)
                    _outerOutlines.Add(outline);   // CCW = outer shell
                else
                    _innerHoles.Add(outline);      // CW  = inner hole
            }

            Debug.Log($"[RailOutlineGenerator] {_outerOutlines.Count} outer outline(s), " +
                      $"{_innerHoles.Count} inner hole(s).");
        }

        // ─── Douglas-Peucker simplification ─────────────────────────────────
        /// <summary>
        /// Removes vertices from a closed polygon whose removal causes less than
        /// <paramref name="tolerance"/> deviation. Eliminates the tiny stub segments
        /// that form at corners when cube edges don't sit pixel-perfectly flush.
        /// Operates on the closed polygon (wraps around).
        /// </summary>
        private static List<Vector2> DouglasPeucker(List<Vector2> poly, float tolerance)
        {
            if (poly.Count <= 3) return poly;

            // Work on an open polyline: duplicate first vertex at end so we process the
            // closing edge too, then drop the duplicate from the result.
            var pts = new List<Vector2>(poly) { poly[0] };
            var keep = DPReduce(pts, 0, pts.Count - 1, tolerance * tolerance);

            var result = new List<Vector2>();
            for (int i = 0; i < pts.Count - 1; i++) // exclude the duplicated closing vertex
                if (keep.Contains(i))
                    result.Add(pts[i]);

            return result;
        }

        private static HashSet<int> DPReduce(List<Vector2> pts, int start, int end, float tolSq)
        {
            var keep = new HashSet<int> { start, end };
            if (end - start <= 1) return keep;

            float maxDistSq = 0f;
            int   maxIdx    = start;
            Vector2 a = pts[start], b = pts[end];

            for (int i = start + 1; i < end; i++)
            {
                float dSq = PointToSegmentDistSq(pts[i], a, b);
                if (dSq > maxDistSq) { maxDistSq = dSq; maxIdx = i; }
            }

            if (maxDistSq > tolSq)
            {
                foreach (var idx in DPReduce(pts, start, maxIdx, tolSq)) keep.Add(idx);
                foreach (var idx in DPReduce(pts, maxIdx, end,   tolSq)) keep.Add(idx);
            }
            return keep;
        }

        private static float PointToSegmentDistSq(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float   lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-12f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return (p - (a + t * ab)).sqrMagnitude;
        }

        // ─── corner cleanup ───────────────────────────────────────────────────
        /// <summary>
        /// For every vertex in the outline, checks whether the two adjacent edge
        /// lines (extended infinitely) intersect within <paramref name="radius"/>
        /// of the current vertex. If they do, the vertex is moved to that true
        /// intersection point, giving a geometrically clean, gap-free corner.
        /// </summary>
        private static List<Vector2> CleanupCorners(List<Vector2> poly, float radius)
        {
            int n = poly.Count;
            var result = new List<Vector2>(n);
            float radiusSq = radius * radius;

            for (int i = 0; i < n; i++)
            {
                // Previous edge: prev -> cur
                Vector2 prev = poly[(i - 1 + n) % n];
                Vector2 cur  = poly[i];
                Vector2 next = poly[(i + 1)     % n];

                // Extend the two lines to their intersection
                float t, u;
                Vector2 isect;
                if (LineLineIntersect(prev, cur, cur, next, out t, out u, out isect))
                {
                    // Only snap if the intersection is close to the existing vertex
                    // (guards against near-parallel edges producing a faraway point)
                    if ((isect - cur).sqrMagnitude < radiusSq)
                    {
                        result.Add(isect);
                        continue;
                    }
                }
                result.Add(cur);
            }
            return result;
        }

        /// <summary>
        /// Intersects infinite lines through (p1,p2) and (p3,p4).
        /// Returns false when lines are (nearly) parallel.
        /// </summary>
        private static bool LineLineIntersect(
            Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
            out float t, out float u, out Vector2 pt)
        {
            t = 0f; u = 0f; pt = Vector2.zero;
            Vector2 d1    = p2 - p1;
            Vector2 d2    = p4 - p3;
            float   cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < GEO_EPS) return false;

            Vector2 diff = p3 - p1;
            t  = (diff.x * d2.y - diff.y * d2.x) / cross;
            u  = (diff.x * d1.y - diff.y * d1.x) / cross;
            pt = p1 + t * d1;
            return true;
        }

        // ─── segment chaining ────────────────────────────────────────────────

        private static List<List<Vector2>> ChainSegments(
            List<(Vector2 a, Vector2 b)> segments, float snapEps)
        {
            // Build adjacency: quantised key → list of neighbours
            var adj = new Dictionary<Vector2Int, List<(Vector2Int nbKey, Vector2 nbPos)>>();

            Vector2Int Quantise(Vector2 p) =>
                new Vector2Int(
                    Mathf.RoundToInt(p.x / snapEps),
                    Mathf.RoundToInt(p.y / snapEps));

            foreach (var (a, b) in segments)
            {
                var ka = Quantise(a);
                var kb = Quantise(b);
                if (!adj.ContainsKey(ka)) adj[ka] = new List<(Vector2Int, Vector2)>();
                if (!adj.ContainsKey(kb)) adj[kb] = new List<(Vector2Int, Vector2)>();
                adj[ka].Add((kb, b));
                adj[kb].Add((ka, a));
            }

            var visitedEdges = new HashSet<(Vector2Int, Vector2Int)>();
            var result       = new List<List<Vector2>>();

            foreach (var startKey in new List<Vector2Int>(adj.Keys))
            {
                foreach (var (nbKey, nbPos) in adj[startKey])
                {
                    var edgeKey = EdgeKey(startKey, nbKey);
                    if (visitedEdges.Contains(edgeKey)) continue;
                    visitedEdges.Add(edgeKey);

                    var chain    = new List<Vector2> { nbPos };
                    var curKey   = nbKey;

                    for (int guard = 0; guard < 10000; guard++)
                    {
                        if (curKey == startKey) break;

                        bool moved = false;
                        foreach (var (nextKey, nextPos) in adj[curKey])
                        {
                            var ek = EdgeKey(curKey, nextKey);
                            if (visitedEdges.Contains(ek)) continue;
                            visitedEdges.Add(ek);
                            chain.Add(nextPos);
                            curKey = nextKey;
                            moved  = true;
                            break;
                        }
                        if (!moved) break;
                    }

                    if (curKey == startKey && chain.Count >= 3)
                        result.Add(chain);

                    break; // only try the first unvisited neighbour per startKey per pass
                }
            }

            return result;
        }

        private static (Vector2Int, Vector2Int) EdgeKey(Vector2Int a, Vector2Int b) =>
            a.x < b.x || (a.x == b.x && a.y < b.y) ? (a, b) : (b, a);

        // ═════════════════════════════════════════════════════════════════════
        // Step 3 – spawn Rail cubes
        // ═════════════════════════════════════════════════════════════════════

        private void SpawnRails()
        {
            Transform parent = railParent != null ? railParent : transform;

            foreach (var outline in _outerOutlines)
                SpawnRailsForOutline(outline, parent, "Rail_Outer");

            if (generateInline)
                foreach (var hole in _innerHoles)
                    SpawnRailsForOutline(hole, parent, "Rail_Inner");

            Debug.Log($"[RailOutlineGenerator] Spawned {_spawnedRails.Count} Rail(s).");
        }

        private void SpawnRailsForOutline(List<Vector2> poly, Transform parent, string prefix)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a2 = poly[i];
                Vector2 b2 = poly[(i + 1) % n];
                SpawnEdgeRail(a2, b2, parent, prefix);
            }
        }

        private void SpawnEdgeRail(Vector2 a2, Vector2 b2, Transform parent, string namePrefix)
        {
            float length = Vector2.Distance(a2, b2);
            if (length < 0.001f) return;

            Vector3 a   = new Vector3(a2.x, railY, a2.y);
            Vector3 b   = new Vector3(b2.x, railY, b2.y);
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = (b - a).normalized;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"{namePrefix}_{_spawnedRails.Count:D3}";
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = mid;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            // Z = along edge (length), X = thickness, Y = height
            go.transform.localScale = new Vector3(railThickness, railHeight, length);

            var rail       = go.AddComponent<Rail>();
            rail.text      = railText;
            rail.isPassable = true;

            var bc = go.GetComponent<BoxCollider>();
            if (bc != null) bc.isTrigger = true;

            _spawnedRails.Add(go);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Geometry helpers
        // ═════════════════════════════════════════════════════════════════════

        private const float GEO_EPS = 1e-7f;

        private static bool SegIntersect(
            Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
            out float t, out float u, out Vector2 pt)
        {
            t = 0f; u = 0f; pt = Vector2.zero;
            Vector2 d1   = p2 - p1;
            Vector2 d2   = p4 - p3;
            float   cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < GEO_EPS) return false;

            Vector2 diff = p3 - p1;
            t = (diff.x * d2.y - diff.y * d2.x) / cross;
            u = (diff.x * d1.y - diff.y * d1.x) / cross;

            if (t > GEO_EPS && t < 1f - GEO_EPS && u > GEO_EPS && u < 1f - GEO_EPS)
            {
                pt = p1 + t * d1;
                return true;
            }
            return false;
        }

        private static bool PointInPoly(Vector2 pt, List<Vector2> poly)
        {
            int  n      = poly.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 vi = poly[i], vj = poly[j];
                if (((vi.y > pt.y) != (vj.y > pt.y)) &&
                    pt.x < (vj.x - vi.x) * (pt.y - vi.y) / (vj.y - vi.y) + vi.x)
                    inside = !inside;
            }
            return inside;
        }

        private static float SignedArea(List<Vector2> poly)
        {
            float area = 0f;
            int   n    = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static List<Vector2> EnsureCCW(List<Vector2> poly)
        {
            if (SignedArea(poly) < 0f)
            {
                var copy = new List<Vector2>(poly);
                copy.Reverse();
                return copy;
            }
            return poly;
        }

        private static void RemoveDuplicates(List<float> sorted, float eps)
        {
            for (int i = sorted.Count - 1; i > 0; i--)
                if (Mathf.Abs(sorted[i] - sorted[i - 1]) < eps)
                    sorted.RemoveAt(i);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Gizmos
        // ═════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            float y = railY + 0.05f;

            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            foreach (var poly in _inputPolygons)
                DrawPolyGizmo(poly, y, new Color(0f, 1f, 1f, 0.4f));

            int idx = 0;
            foreach (var poly in _outerOutlines)
                DrawPolyGizmo(poly, y, idx++ % 2 == 0 ? Color.yellow : Color.green);

            foreach (var hole in _innerHoles)
                DrawPolyGizmo(hole, y, Color.magenta);
        }

        private static void DrawPolyGizmo(List<Vector2> poly, float y, Color color)
        {
            Gizmos.color = color;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                Gizmos.DrawLine(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y));
            }
        }
#endif
    }

// ═════════════════════════════════════════════════════════════════════════════
// Custom Inspector
// ═════════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR
    [CustomEditor(typeof(RailOutlineGenerator))]
    public class RailOutlineGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(6);
            var gen = (RailOutlineGenerator)target;
            using (new EditorGUI.DisabledScope(!Application.isPlaying && false)) // always enabled
            {
                if (GUILayout.Button("▶  Generate Rails", GUILayout.Height(28))) gen.Generate();
                if (GUILayout.Button("✕  Clear Rails",    GUILayout.Height(24))) gen.Clear();
            }
        }
    }
#endif
}
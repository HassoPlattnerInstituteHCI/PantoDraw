using System.Collections.Generic;
using DualPantoToolkit;
using UnityEngine;

[ExecuteAlways]
public class CubeTrenchGraphNetwork : MonoBehaviour
{
    [Header("Collection")]
    [SerializeField] private bool collectFromChildren = true;
    [SerializeField] private bool includeInactiveChildren = true;
    [SerializeField] private bool rebuildOnStart = true;

    [Header("Haptics")]
    [SerializeField] private bool addDitchForceToCubes = true;
    [SerializeField] private bool ensureCubeTriggers = false;

    [Header("Graph")]
    [SerializeField] private float nodeRadius = 0.03f;
    [SerializeField] private float overlapMergeDistance = 0.02f;
    [SerializeField] private Color cubeNodeColor = new Color(0.2f, 0.65f, 1f, 1f);
    [SerializeField] private Color overlapNodeColor = new Color(1f, 0.55f, 0.15f, 1f);
    [SerializeField] private Color edgeColor = new Color(0.95f, 0.95f, 0.95f, 1f);

    private readonly List<BoxCollider> cubes = new List<BoxCollider>();
    private readonly List<GraphNode> nodes = new List<GraphNode>();
    private readonly List<GraphEdge> edges = new List<GraphEdge>();

    [System.Serializable]
    private class GraphNode
    {
        public string id;
        public Vector3 position;
        public NodeKind kind;
        public BoxCollider sourceA;
        public BoxCollider sourceB;
    }

    [System.Serializable]
    private class GraphEdge
    {
        public int fromIndex;
        public int toIndex;
    }

    private enum NodeKind
    {
        Cube,
        Overlap
    }

    private void Start()
    {
        if (rebuildOnStart)
        {
            RebuildGraph();
        }
    }

    private void OnEnable()
    {
        if (Application.isPlaying && rebuildOnStart)
        {
            RebuildGraph();
        }
    }

    [ContextMenu("Rebuild Graph")]
    public void RebuildGraph()
    {
        cubes.Clear();
        nodes.Clear();
        edges.Clear();

        CollectCubes();

        for (int i = 0; i < cubes.Count; i++)
        {
            BoxCollider cube = cubes[i];
            if (cube == null)
            {
                continue;
            }

            if (ensureCubeTriggers)
            {
                cube.isTrigger = true;
            }

            if (addDitchForceToCubes && cube.GetComponent<DitchCenterForce>() == null)
            {
                cube.gameObject.AddComponent<DitchCenterForce>();
            }

            nodes.Add(new GraphNode
            {
                id = cube.name,
                position = cube.bounds.center,
                kind = NodeKind.Cube,
                sourceA = cube
            });
        }

        for (int i = 0; i < cubes.Count; i++)
        {
            BoxCollider first = cubes[i];
            if (first == null)
            {
                continue;
            }

            for (int j = i + 1; j < cubes.Count; j++)
            {
                BoxCollider second = cubes[j];
                if (second == null)
                {
                    continue;
                }

                if (!TryGetOverlapNodePosition(first.bounds, second.bounds, out Vector3 overlapPosition))
                {
                    continue;
                }

                int overlapNodeIndex = GetOrCreateOverlapNode(overlapPosition, first, second);
                int firstNodeIndex = FindCubeNodeIndex(first);
                int secondNodeIndex = FindCubeNodeIndex(second);

                if (firstNodeIndex >= 0)
                {
                    edges.Add(new GraphEdge { fromIndex = firstNodeIndex, toIndex = overlapNodeIndex });
                }

                if (secondNodeIndex >= 0)
                {
                    edges.Add(new GraphEdge { fromIndex = secondNodeIndex, toIndex = overlapNodeIndex });
                }
            }
        }
    }

    private void CollectCubes()
    {
        if (collectFromChildren)
        {
            BoxCollider[] childColliders = GetComponentsInChildren<BoxCollider>(includeInactiveChildren);
            for (int i = 0; i < childColliders.Length; i++)
            {
                BoxCollider cube = childColliders[i];
                if (cube == null)
                {
                    continue;
                }

                if (cube.gameObject == gameObject)
                {
                    continue;
                }

                cubes.Add(cube);
            }
        }
    }

    private int FindCubeNodeIndex(BoxCollider cube)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNode node = nodes[i];
            if (node.kind == NodeKind.Cube && node.sourceA == cube)
            {
                return i;
            }
        }

        return -1;
    }

    private int GetOrCreateOverlapNode(Vector3 position, BoxCollider first, BoxCollider second)
    {
        float mergeDistanceSqr = overlapMergeDistance * overlapMergeDistance;

        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNode node = nodes[i];
            if (node.kind != NodeKind.Overlap)
            {
                continue;
            }

            if ((node.position - position).sqrMagnitude <= mergeDistanceSqr)
            {
                return i;
            }
        }

        nodes.Add(new GraphNode
        {
            id = $"Overlap_{first.name}_{second.name}",
            position = position,
            kind = NodeKind.Overlap,
            sourceA = first,
            sourceB = second
        });

        return nodes.Count - 1;
    }

    private static bool TryGetOverlapNodePosition(Bounds first, Bounds second, out Vector3 position)
    {
        Vector3 min = Vector3.Max(first.min, second.min);
        Vector3 max = Vector3.Min(first.max, second.max);

        if (min.x > max.x || min.y > max.y || min.z > max.z)
        {
            position = default;
            return false;
        }

        position = (min + max) * 0.5f;
        position.y = 0f;
        return true;
    }

    private void OnDrawGizmos()
    {
        if (nodes.Count == 0)
        {
            return;
        }

        for (int i = 0; i < edges.Count; i++)
        {
            GraphEdge edge = edges[i];
            if (edge.fromIndex < 0 || edge.fromIndex >= nodes.Count || edge.toIndex < 0 || edge.toIndex >= nodes.Count)
            {
                continue;
            }

            Gizmos.color = edgeColor;
            Gizmos.DrawLine(nodes[edge.fromIndex].position, nodes[edge.toIndex].position);
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNode node = nodes[i];
            Gizmos.color = node.kind == NodeKind.Cube ? cubeNodeColor : overlapNodeColor;
            Gizmos.DrawSphere(node.position, nodeRadius);
        }
    }
}
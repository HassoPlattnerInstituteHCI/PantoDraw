using System;
using DualPantoToolkit;
using Meta.WitAi;
using UnityEngine;

/// <summary>
/// Voice command handler for the "gotoshape" Wit.ai intent. Matches the spoken "shape" and
/// "number" entities (e.g. "rectangle" + "1") against the shapes spawned by DrawPen and moves
/// the upper ("Me") handle to that shape's spawn point.
/// </summary>
public class GoToShapeCommand : MonoBehaviour
{
    [SerializeField] private DrawPen drawPen;
    [SerializeField] private UpperHandle upperHandle;
    [SerializeField] private float moveSpeed = 1.0f;

    private void Awake()
    {
        if (drawPen == null)
        {
            drawPen = FindAnyObjectByType<DrawPen>();
        }

        if (upperHandle == null)
        {
            upperHandle = FindAnyObjectByType<UpperHandle>();
        }
    }

    /// <summary>
    /// Gets called automatically by Conduit when the "gotoshape" intent is recognized.
    /// </summary>
    /// <param name="shape">The shape type entity, e.g. "rectangle", "circle", "triangle", "line".</param>
    /// <param name="number">The shape's index entity, e.g. 1 for "Rectangle 1".</param>
    [MatchIntent("gotoshape")]
    private void GoToShape(string shape, int number)
    {
        if (drawPen == null || upperHandle == null)
        {
            Debug.LogWarning("[GoToShapeCommand] Missing DrawPen or UpperHandle reference.");
            return;
        }

        string targetName = $"{Capitalize(shape)} {number}";
        Shape target = drawPen.shapes.Find(s =>
            s != null && string.Equals(s.shapeName, targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            Debug.LogWarning($"[GoToShapeCommand] No shape named '{targetName}' found.");
            return;
        }

        if (target.spawnPoint == null)
        {
            Debug.LogWarning($"[GoToShapeCommand] Shape '{targetName}' has no spawn point.");
            return;
        }

        Debug.Log($"[GoToShapeCommand] Moving to '{targetName}'.");
        _ = upperHandle.MoveToPosition(target.spawnPoint.position, moveSpeed);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }
}

using System.Collections.Generic;
using DualPantoToolkit;
using UnityEngine;

/// <summary>
/// Parent-Komponente für ein Netz aus Gräben (DitchSegment-Kinder).
///
/// Kraft-Modell: PD-Regler
///   F = kP * Abstand  −  kD * (Geschwindigkeit zur Mittellinie)
///
/// Das eliminiert Oszillation: der D-Anteil bremst den Handle
/// bevor er die Linie überschießt.
/// </summary>
public class TrenchNetwork : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UpperHandle meHandle;

    [Header("PD-Regler")]
    [Tooltip("Proportional-Anteil: Grundstärke pro Meter Abstand zur Mittellinie.")]
    [SerializeField] private float kP = 8.0f;

    [Tooltip("Dämpfungs-Anteil: Bremst Bewegung QUER zur Mittellinie. " +
             "Höher = weniger Oszillation, aber weicherer Graben.")]
    [SerializeField] private float kD = 0.4f;

    [Tooltip("Maximale Kraft, die ausgegeben wird (Clamp).")]
    [SerializeField] private float maxForce = 1.0f;

    [Tooltip("Unterhalb dieser Distanz wird keine Kraft mehr ausgegeben (Totzone).")]
    [SerializeField] private float cutoffDistance = 0.005f;

    [Header("Intersection Blend")]
    [Tooltip("1 = immer nur nächster Graben dominiert, 0 = gleichmäßiger Blend.")]
    [SerializeField, Range(0f, 1f)] private float nearestBias = 0.6f;

    // ── Zustand für D-Anteil ──────────────────────────────────────────────────
    private Vector3 _prevHandlePos;
    private bool    _hadPrevPos;

    // Alle Kind-Gräben, die der Handle gerade berührt
    private readonly HashSet<DitchSegment> _activeSegments = new();

    // ── API für DitchSegment-Kinder ───────────────────────────────────────────

    internal void OnSegmentEnter(DitchSegment seg) => _activeSegments.Add(seg);
    internal void OnSegmentExit (DitchSegment seg)
    {
        _activeSegments.Remove(seg);
        if (_activeSegments.Count == 0)
        {
            _hadPrevPos = false;          // D-Zustand zurücksetzen
            meHandle?.StopApplyingForce();
        }
    }

    internal UpperHandle GetHandle() => meHandle;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Start()
    {
        if (meHandle == null)
            meHandle = FindAnyObjectByType<UpperHandle>();
    }

    void FixedUpdate()
    {
        if (meHandle == null || _activeSegments.Count == 0)
        {
            meHandle?.StopApplyingForce();
            _hadPrevPos = false;
            return;
        }

        Vector3 handlePos = meHandle.GetPosition();
        handlePos.y = 0f;

        // ── Zielposition bestimmen ────────────────────────────────────────────
        var candidates = new List<(Vector3 target, float dist)>();
        foreach (DitchSegment seg in _activeSegments)
        {
            Vector3 t = seg.GetCenterLineTarget(handlePos);
            float   d = Vector3.Distance(handlePos, t);
            candidates.Add((t, d));
        }

        Vector3 finalTarget = ComputeBlendedTarget(candidates, handlePos);
        Vector3 toTarget    = finalTarget - handlePos;
        float   distance    = toTarget.magnitude;

        if (distance <= cutoffDistance)
        {
            meHandle.StopApplyingForce();
            _hadPrevPos = false;
            return;
        }

        Vector3 direction = toTarget / distance;   // normiert

        // ── P-Anteil ─────────────────────────────────────────────────────────
        float fP = kP * distance;

        // ── D-Anteil: Geschwindigkeit *quer* zur Linie bremsen ────────────────
        float fD = 0f;
        if (_hadPrevPos)
        {
            Vector3 velocity       = (handlePos - _prevHandlePos) / Time.fixedDeltaTime;
            // Nur die Komponente der Geschwindigkeit, die zur Mittellinie zeigt,
            // wird gedämpft (Längskomponente bleibt unangetastet → Zeichnen möglich)
            float   velTowardsLine = Vector3.Dot(velocity, direction);
            // Negativ weil wir die Bewegung bremsen wollen
            fD = -kD * velTowardsLine;
        }

        _prevHandlePos = handlePos;
        _hadPrevPos    = true;

        // ── Kraft zusammensetzen & begrenzen ─────────────────────────────────
        float totalStrength = Mathf.Clamp(fP + fD, 0f, maxForce);

        if (totalStrength <= 0.0001f)
        {
            meHandle.StopApplyingForce();
            return;
        }

        meHandle.ApplyForce(direction, totalStrength);
    }

    // ── Blend-Logik ───────────────────────────────────────────────────────────

    private Vector3 ComputeBlendedTarget(List<(Vector3 target, float dist)> candidates,
                                         Vector3 handlePos)
    {
        if (candidates.Count == 1)
            return candidates[0].target;

        float   totalWeight = 0f;
        Vector3 weighted    = Vector3.zero;

        foreach (var (target, dist) in candidates)
        {
            float w      = 1f / Mathf.Max(dist, 0.001f);
            weighted    += target * w;
            totalWeight += w;
        }

        Vector3 idw = weighted / totalWeight;

        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
        return Vector3.Lerp(idw, candidates[0].target, nearestBias);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.yellow;
        foreach (DitchSegment seg in _activeSegments)
        {
            if (seg != null)
                Gizmos.DrawWireCube(seg.transform.position, Vector3.one * 0.05f);
        }
    }
}
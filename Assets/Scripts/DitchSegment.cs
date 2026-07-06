using DualPantoToolkit;
using UnityEngine;

/// <summary>
/// Ersetzt DitchCenterForce als Kind-Komponente.
///
/// Diese Klasse:
///  • erkennt per Trigger, ob der Handle drinnen ist
///  • berechnet den Zielpunkt auf der Mittellinie (identische Logik wie vorher)
///  • RUFT ABER KEINE FORCES auf – das übernimmt TrenchNetwork im Parent.
///
/// Setup:
///  • GameObject mit BoxCollider (IsTrigger = true) + diesem Script
///  • Parent-GameObject trägt TrenchNetwork
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DitchSegment : MonoBehaviour
{
    [Header("Center Line")]
    [SerializeField] private float endMargin = 0.02f;

    [Header("Gizmos")]
    [SerializeField] private bool  showGizmos     = true;
    [SerializeField] private Color centerLineColor = Color.red;
    [SerializeField] private Color cutoffColor     = Color.green;
    [SerializeField] private float cutoffSphereR   = 0.01f;

    private BoxCollider   _box;
    private TrenchNetwork _network;

    // ── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        _box     = GetComponent<BoxCollider>();
        _network = GetComponentInParent<TrenchNetwork>();

        if (_network == null)
            Debug.LogWarning($"[DitchSegment] {name}: Kein TrenchNetwork im Parent gefunden!", this);

        // Sicherstellen, dass der Collider als Trigger konfiguriert ist
        _box.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsHandle(other)) _network?.OnSegmentEnter(this);
    }

    void OnTriggerStay(Collider other)
    {
        // OnTriggerStay ist hier nur nötig, um sicherzustellen, dass
        // beim Start innerhalb des Colliders registriert wird.
        // TrenchNetwork pollt in FixedUpdate – kein Handlungsbedarf.
    }

    void OnTriggerExit(Collider other)
    {
        if (IsHandle(other)) _network?.OnSegmentExit(this);
    }

    // ── Öffentliche API ──────────────────────────────────────────────────────

    /// <summary>
    /// Gibt den nächsten Punkt auf der Mittellinie zurück (in Weltkoordinaten, y=0).
    /// Wird von TrenchNetwork in jedem FixedUpdate aufgerufen.
    /// </summary>
    public Vector3 GetCenterLineTarget(Vector3 handlePos)
    {
        if (_box == null) _box = GetComponent<BoxCollider>();
        return ComputeCenterLineTarget(_box, handlePos);
    }

    // ── Interne Logik ─────────────────────────────────────────────────────────

    private bool IsHandle(Collider other)
    {
        if (other == null || _network == null) return false;
        // Prüft über TrenchNetwork, welcher Handle referenziert wird
        UpperHandle handle = _network.GetHandle();
        if (handle == null) return false;
        return other.GetComponentInParent<UpperHandle>() == handle
            || other.CompareTag("MeHandle");
    }

    /// <summary>
    /// Identische Mittellinie-Berechnung wie in DitchCenterForce,
    /// aber als statische Hilfsmethode.
    /// </summary>
    private Vector3 ComputeCenterLineTarget(BoxCollider box, Vector3 handlePos)
    {
        Vector3 scale = box.transform.lossyScale;

        float halfX = box.size.x * scale.x * 0.5f;
        float halfZ = box.size.z * scale.z * 0.5f;

        bool  useXAxis  = halfX >= halfZ;
        float halfLong  = useXAxis ? halfX : halfZ;
        float inset     = Mathf.Clamp(endMargin, 0f, halfLong - 0.001f);

        Vector3 localHandle = box.transform.InverseTransformPoint(handlePos) - box.center;

        float rawLong = useXAxis
            ? localHandle.x * scale.x
            : localHandle.z * scale.z;

        float clampedLong = Mathf.Clamp(rawLong, -halfLong + inset, halfLong - inset);

        float localLong = useXAxis
            ? clampedLong / scale.x
            : clampedLong / scale.z;

        Vector3 localTarget = useXAxis
            ? new Vector3(localLong, 0f, 0f)
            : new Vector3(0f, 0f, localLong);

        Vector3 world = box.transform.TransformPoint(localTarget + box.center);
        world.y = 0f;
        return world;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        BoxCollider box = _box != null ? _box : GetComponent<BoxCollider>();
        if (box == null) return;

        Vector3 scale  = box.transform.lossyScale;
        float   halfX  = box.size.x * scale.x * 0.5f;
        float   halfZ  = box.size.z * scale.z * 0.5f;
        bool    useX   = halfX >= halfZ;
        float   hLong  = useX ? halfX : halfZ;
        float   inset  = Mathf.Clamp(endMargin, 0f, hLong - 0.001f);

        Vector3 wc      = box.transform.TransformPoint(box.center);
        wc.y = 0f;

        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Gizmos.DrawWireCube(wc, new Vector3(halfX * 2f, 0.01f, halfZ * 2f));

        Vector3 longDir = useX ? box.transform.right : box.transform.forward;
        longDir.y = 0f;
        longDir.Normalize();

        Vector3 p0 = wc - longDir * (hLong - inset);
        Vector3 p1 = wc + longDir * (hLong - inset);

        Gizmos.color = centerLineColor;
        Gizmos.DrawLine(p0, p1);

        Gizmos.color = cutoffColor;
        Gizmos.DrawSphere(p0, cutoffSphereR);
        Gizmos.DrawSphere(p1, cutoffSphereR);
    }
}
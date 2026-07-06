using DualPantoToolkit;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DitchCenterForce : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UpperHandle meHandle;

    [Header("Pull")]
    [SerializeField] private float maxForceStrength = 1.0f;
    [SerializeField] private float pullRange = 0.2f;
    [SerializeField] private float cutoffDistance = 0.01f;
    [SerializeField] private float endMargin = 0.02f;

    private Collider trenchCollider;

    [Header("Gizmos")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color centerLineColor = Color.red;
    [SerializeField] private Color cutoffLineColor = Color.green;

    void Start()
    {
        trenchCollider = GetComponent<Collider>();

        if (meHandle == null)
            meHandle = FindAnyObjectByType<UpperHandle>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsHandleCollider(other))
            ApplyTrenchForce();
    }

    void OnTriggerStay(Collider other)
    {
        if (IsHandleCollider(other))
            ApplyTrenchForce();
    }

    void OnTriggerExit(Collider other)
    {
        if (IsHandleCollider(other) && meHandle != null)
            meHandle.StopApplyingForce();
    }

    private void ApplyTrenchForce()
    {
        if (meHandle == null || trenchCollider == null)
            return;

        BoxCollider box = trenchCollider as BoxCollider;
        if (box == null)
            return;

        Vector3 handlePosition = meHandle.GetPosition();
        handlePosition.y = 0f;
        Vector3 targetPoint = GetTargetPointOnCenterLine(box, handlePosition);

        Vector3 delta = targetPoint - handlePosition;
        float distance = delta.magnitude;

        if (distance <= cutoffDistance)
        {
            meHandle.StopApplyingForce();
            return;
        }

        float normalized = Mathf.Clamp01(distance / Mathf.Max(pullRange, 0.0001f));
        float strength = normalized * maxForceStrength;

        if (strength <= 0.0001f)
        {
            meHandle.StopApplyingForce();
            return;
        }

        Vector3 direction = delta.normalized;
        meHandle.ApplyForce(direction, strength);
    }

    private bool IsHandleCollider(Collider other)
    {
        if (other == null || meHandle == null)
            return false;

        return other.GetComponentInParent<UpperHandle>() == meHandle
            || other.CompareTag("MeHandle");
    }

    /// <summary>
    /// Berechnet den Zielpunkt auf der Mittellinie des Box-Colliders.
    /// Die Mittellinie verläuft entlang der langen Achse.
    /// endMargin hält den Zielpunkt von den kurzen Enden fern –
    /// dadurch entsteht auch longitudinal eine Rückzugskraft.
    /// </summary>
    private Vector3 GetTargetPointOnCenterLine(BoxCollider box, Vector3 handlePosition)
    {
        Vector3 scale = box.transform.lossyScale;

        // Skalierte halbe Ausdehnungen (box.size ist unscaliert)
        float halfX = box.size.x * scale.x * 0.5f;
        float halfZ = box.size.z * scale.z * 0.5f;

        // Lange Achse in Weltkoordinaten
        bool useXAxis = halfX >= halfZ;
        float halfLong = useXAxis ? halfX : halfZ;

        // endMargin in lokalen skalierten Einheiten
        float inset = Mathf.Clamp(endMargin, 0f, halfLong - 0.001f);

        // Handle in lokale Box-Koordinaten (InverseTransformPoint berücksichtigt Scale)
        Vector3 localHandle = box.transform.InverseTransformPoint(handlePosition) - box.center;

        // rawLong ist jetzt in lokalen unskalierten Einheiten → mit Scale normalisieren
        float rawLong = useXAxis
            ? localHandle.x * scale.x
            : localHandle.z * scale.z;

        float clampedLong = Mathf.Clamp(rawLong, -halfLong + inset, halfLong - inset);

        // Zurück in lokale unskalierte Einheiten für TransformPoint
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

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        if (trenchCollider == null) trenchCollider = GetComponent<Collider>();
        if (trenchCollider == null) return;

        BoxCollider box = trenchCollider as BoxCollider;
        if (box == null) return;

        Vector3 scale = box.transform.lossyScale;
        float halfX = box.size.x * scale.x * 0.5f;
        float halfZ = box.size.z * scale.z * 0.5f;
        bool useXAxis = halfX >= halfZ;
        float halfLong = useXAxis ? halfX : halfZ;
        float inset    = Mathf.Clamp(endMargin, 0f, halfLong - 0.001f);

        // Box-Umriss (flach, in Weltgröße)
        Vector3 worldCenter = box.transform.TransformPoint(box.center);
        worldCenter.y = 0f;
        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Gizmos.DrawWireCube(worldCenter, new Vector3(halfX * 2f, 0.01f, halfZ * 2f));

        // Mittellinie
        Vector3 longDir = useXAxis ? box.transform.right : box.transform.forward;
        longDir.y = 0f;
        longDir.Normalize();

        Vector3 p0 = worldCenter - longDir * (halfLong - inset);
        Vector3 p1 = worldCenter + longDir * (halfLong - inset);

        Gizmos.color = centerLineColor;
        Gizmos.DrawLine(p0, p1);

        Gizmos.DrawCube(worldCenter, p0 + cutoffDistance * longDir - worldCenter);

        Gizmos.color = cutoffLineColor;
        Gizmos.DrawSphere(p0, cutoffDistance);
        Gizmos.DrawSphere(p1, cutoffDistance);
    }
}
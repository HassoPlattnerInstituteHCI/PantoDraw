using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DualPantoToolkit;
using System.Threading.Tasks;

public class VisualizePen : MonoBehaviour
{
    public PantoMeshCollider pantoMeshCollider; // Reference to the PantoMeshCollider component
    public bool inViewMode = false;

    public int currentShapeViewIndex = 0;

    public float maxPenSpeed = 5.0f; // Speed at which the pen moves to the next shape

    public float toleranceToSpawnPoint = 0.05f; // Tolerance distance to consider the pen has reached the spawn point

    [Header("PID gains for MoveToPosition")]
    public float pidP = 3.0f;
    public float pidI = 0.0f;
    public float pidD = 0.6f;

    private DrawPen drawPen;

    private UpperHandle visulizationPen;

    private MeshCollider obstacleManager;

    private bool isMoving = false;

    public void Start()
    {
        drawPen = FindObjectOfType<DrawPen>();
        visulizationPen = FindObjectOfType<UpperHandle>();
        //obstacleManager = FindObjectOfType<ObstacleManager>();
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.C) && !isMoving)
        {
            NextShapeView();
        }
        else if (Input.GetKeyDown(KeyCode.A) && !isMoving)
        {
            PreviousShapeView();
        }
    }

    private async void NextShapeView()
    {
        //obstacleManager = FindObjectOfType<ObstacleManager>();
        currentShapeViewIndex++;
        if (currentShapeViewIndex >= drawPen.shapes.Count)
        {
            currentShapeViewIndex = 0;
        }
        Transform spawnPoint = drawPen.shapes[currentShapeViewIndex].spawnPoint;
        MovePen(spawnPoint.position);
    }

    private async void PreviousShapeView()
    {
        currentShapeViewIndex--;
        if (currentShapeViewIndex < 0)
        {
            currentShapeViewIndex = drawPen.shapes.Count - 1;
        }
        Transform spawnPoint = drawPen.shapes[currentShapeViewIndex].spawnPoint;
        MovePen(spawnPoint.position);
    }

    private async void MovePen(Vector3 targetPosition)
    {
        SpeechManager.Instance.Speak(drawPen.shapes[currentShapeViewIndex].shapeName);
        pantoMeshCollider.Disable();
        isMoving = true;
        await MoveToPosition(targetPosition);
        isMoving = false;
        pantoMeshCollider.Enable();
    }

    // private async Task MoveToPosition(Vector3 targetPosition)
    // {
    //     // Drive the pen to the target with a PID controller on the position error so the
    //     // applied force ramps down on approach instead of slamming full strength (which
    //     // caused overshoot/oscillation around the target).
    //     Vector3 integral = Vector3.zero;
    //     Vector3 previousError = targetPosition - this.transform.position;
    //     float lastTime = Time.realtimeSinceStartup;

    //     while (Vector3.Distance(this.transform.position, targetPosition) > toleranceToSpawnPoint)
    //     {
    //         float now = Time.realtimeSinceStartup;
    //         float deltaTime = Mathf.Max(now - lastTime, 0.0001f);
    //         lastTime = now;

    //         Vector3 error = targetPosition - this.transform.position;
    //         integral += error * deltaTime;
    //         // Anti-windup: keep the integral term from growing past what a full-strength force could counter.
    //         if (pidI > 0f)
    //         {
    //             integral = Vector3.ClampMagnitude(integral, 1f / pidI);
    //         }
    //         Vector3 derivative = (error - previousError) / deltaTime;
    //         previousError = error;

    //         Vector3 pidOutput = pidP * error + pidI * integral + pidD * derivative;
    //         //float strength = Mathf.Clamp01(pidOutput.magnitude / maxPenSpeed);
    //         Vector3 direction = pidOutput.sqrMagnitude > 0.0001f ? pidOutput.normalized : Vector3.zero;

    //         visulizationPen.ApplyForce(direction, pidOutput.magnitude);

    //         await Task.Delay(100); // wait for a short time before checking the distance again
    //     }

    //     visulizationPen.StopApplyingForce(); // cut the motor cleanly once inside tolerance
    // }

    private async Task MoveToPosition(Vector3 targetPosition)
    {
        while (Vector3.Distance(this.transform.position, targetPosition) > toleranceToSpawnPoint)
        {
           
            visulizationPen.MoveToPosition(targetPosition, maxPenSpeed);
            await Task.Delay(100); // wait for a short time before checking the distance again
        }

        visulizationPen.StopApplyingForce(); // cut the motor cleanly once inside tolerance
    }
}
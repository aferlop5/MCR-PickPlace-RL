using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class FrankaLiftAgent : Agent
{
    [Header("Articulaciones (link1 a link7)")]
    public ArticulationBody[] jointBodies;

    [Header("Pinza")]
    public ArticulationBody leftFinger;
    public ArticulationBody rightFinger;

    [Header("Referencias de Escena")]
    public Transform cubeTransform;
    public Rigidbody cubeRigidbody;
    public Transform tcpTransform; // fr3_hand_tcp

    [Header("Parámetros")]
    public float jointSpeed = 35f;
    public float targetHeight = 0.15f; // Altura sobre la mesa para considerar éxito

    private const float GRIPPER_ABIERTO = -0.04f;
    private const float GRIPPER_CERRADO_CUBO = -0.014f;

    // Postura Home calibrada
    private readonly float[] poseHome = new float[] { 75f, 10f, 20f, -75f, 0f, 90f, 48.92f };
    private Vector3 cubeInitialPosition;

    public override void Initialize()
    {
        if (cubeTransform != null)
        {
            cubeInitialPosition = cubeTransform.position;
        }
    }

    public override void OnEpisodeBegin()
    {
        // 1. Resetear el cubo a su posicion y detener velocidades
        if (cubeRigidbody != null)
        {
            cubeRigidbody.linearVelocity = Vector3.zero;
            cubeRigidbody.angularVelocity = Vector3.zero;
        }
        cubeTransform.position = cubeInitialPosition;
        cubeTransform.rotation = Quaternion.identity;

        // 2. Resetear articulaciones a la postura Home
        for (int i = 0; i < jointBodies.Length; i++)
        {
            if (jointBodies[i] == null) continue;
            var drive = jointBodies[i].xDrive;
            drive.target = poseHome[i];
            jointBodies[i].xDrive = drive;
        }

        // 3. Abrir la pinza
        SetGripper(GRIPPER_ABIERTO);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Observación de articulaciones (7 valores)
        for (int i = 0; i < jointBodies.Length; i++)
        {
            sensor.AddObservation(jointBodies[i].jointPosition[0]);
        }

        // Distancia relativa del efector al cubo (Vector3 = 3 valores)
        Vector3 toCube = cubeTransform.position - tcpTransform.position;
        sensor.AddObservation(toCube);

        // Altura actual del cubo (1 valor)
        sensor.AddObservation(cubeTransform.position.y);

        // Apertura actual de la pinza (1 valor)
        sensor.AddObservation(leftFinger.jointPosition[0]);
        
        // Total observaciones = 7 + 3 + 1 + 1 = 12
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var continuousActions = actions.ContinuousActions;

        // Modificar ángulo objetivo de las 7 articulaciones
        for (int i = 0; i < jointBodies.Length; i++)
        {
            if (jointBodies[i] == null) continue;
            var drive = jointBodies[i].xDrive;
            drive.target += continuousActions[i] * jointSpeed * Time.fixedDeltaTime;
            jointBodies[i].xDrive = drive;
        }

        // Control de pinza continua (acción 7: si es negativa cierra, si es positiva abre)
        float gripperAction = continuousActions[7];
        float targetGripper = gripperAction < 0f ? GRIPPER_CERRADO_CUBO : GRIPPER_ABIERTO;
        SetGripper(targetGripper);

        // --- SISTEMA DE RECOMPENSAS ---
        float distanceToCube = Vector3.Distance(tcpTransform.position, cubeTransform.position);

        // Recompensa densa: incentivar acercar la pinza al cubo
        AddReward(-0.01f * distanceToCube);

        // Éxito: levantar el cubo por encima del umbral
        if (cubeTransform.position.y > cubeInitialPosition.y + targetHeight)
        {
            AddReward(10f);
            EndEpisode();
        }

        // Caída: si el cubo se cae de la mesa
        if (cubeTransform.position.y < cubeInitialPosition.y - 0.1f)
        {
            SetReward(-1f);
            EndEpisode();
        }
    }

    private void SetGripper(float targetMetros)
    {
        if (leftFinger != null)
        {
            var dLeft = leftFinger.xDrive;
            dLeft.target = targetMetros;
            leftFinger.xDrive = dLeft;
        }

        if (rightFinger != null)
        {
            var dRight = rightFinger.xDrive;
            dRight.target = targetMetros;
            rightFinger.xDrive = dRight;
        }
    }
}
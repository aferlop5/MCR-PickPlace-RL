using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

public class FrankaLiftAgent : Agent
{
    public enum TrainingStage { Reach, Lift }

    [Header("Articulaciones: link1 a link7, en orden")]
    public ArticulationBody[] jointBodies;
    public ArticulationBody leftFinger;
    public ArticulationBody rightFinger;

    [Header("Referencias de esta arena")]
    public Transform arenaRoot;
    public Transform cubeTransform;
    public Rigidbody cubeRigidbody;
    public Transform tcpTransform;
    public Collider tableCollider;
    [HideInInspector] public int arenaId;

    [Header("Curriculum manual")]
    public TrainingStage stage = TrainingStage.Reach;
    public bool randomizeCube;
    [Min(0f)] public float cubeRandomXZ = 0.03f;

    [Header("Control y objetivo")]
    [Min(1f)] public float jointSpeed = 35f;
    [Min(0.01f)] public float reachDistance = 0.07f;
    [Min(0.01f)] public float graspDistance = 0.09f;
    [Min(0.01f)] public float targetHeight = 0.15f;

    private const float GripperOpen = -0.04f;
    private const float GripperClosedOnCube = -0.014f;
    private const float ProgressRewardScale = 2f;
    private const float HeightRewardScale = 5f;
    private const float StepCost = 0.0005f;
    private const int ResetSettlingActions = 5;
    private static readonly float[] Home = { 75f, 10f, 20f, -75f, 0f, 90f, 48.92f };
    private readonly List<float> jointPositions = new List<float>();
    private readonly List<float> zeroVelocities = new List<float>();
    private readonly List<int> dofStarts = new List<int>();
    private ArticulationBody articulationRoot;
    private Vector3 cubeSpawnLocalPosition;
    private Quaternion cubeSpawnLocalRotation;
    private Vector3 robotRootLocalPosition;
    private Quaternion robotRootLocalRotation;
    private float previousDistance;
    private float previousHeight;
    private bool reachedOnce;
    private bool graspRewardGiven;
    private bool previousGripperClosed;
    private bool episodeHasStarted;
    private bool episodeSucceeded;
    private float bestDistance;
    private float bestHeight;
    private float initialDistance;
    private float episodeReward;
    private float cubeRandomX;
    private float cubeRandomZ;
    private int arenaEpisode;
    private int episodeSteps;
    private int decisionCount;
    private int gripperCloseAttempts;
    private bool decisionPending;
    private int totalActionCount;
    private int completedEpisodeCount;
    private int settlingActionsRemaining;

    public int TotalActionCount { get { return totalActionCount; } }
    public int CompletedEpisodeCount { get { return completedEpisodeCount; } }

    public override void Initialize()
    {
        if (arenaRoot == null) arenaRoot = transform.parent != null ? transform.parent : transform;
        if (jointBodies == null || jointBodies.Length != 7 || cubeTransform == null ||
            cubeRigidbody == null || tcpTransform == null || leftFinger == null || rightFinger == null)
        {
            Debug.LogError("FrankaLiftAgent: faltan referencias o no hay siete articulaciones.", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < jointBodies.Length; i++)
        {
            if (jointBodies[i] != null && jointBodies[i].dofCount == 1) continue;
            Debug.LogError("FrankaLiftAgent: jointBodies debe contener link1..link7 con un grado de libertad.", this);
            enabled = false;
            return;
        }
        foreach (ArticulationBody body in jointBodies[0].GetComponentsInParent<ArticulationBody>())
        {
            if (body.isRoot) articulationRoot = body;
        }
        if (articulationRoot == null)
        {
            Debug.LogError("FrankaLiftAgent: no se encontró la raíz de la articulación.", this);
            enabled = false;
            return;
        }
        cubeSpawnLocalPosition = arenaRoot.InverseTransformPoint(cubeTransform.position);
        cubeSpawnLocalRotation = Quaternion.Inverse(arenaRoot.rotation) * cubeTransform.rotation;
        robotRootLocalPosition = arenaRoot.InverseTransformPoint(articulationRoot.transform.position);
        robotRootLocalRotation = Quaternion.Inverse(arenaRoot.rotation) * articulationRoot.transform.rotation;
        articulationRoot.GetDofStartIndices(dofStarts);
        TrainingCsvLogger.Register(this);
    }

    public override void OnEpisodeBegin()
    {
        if (articulationRoot == null) return;
        if (episodeHasStarted)
            FinishEpisode("timeout");
        episodeHasStarted = true;
        episodeSucceeded = false;
        arenaEpisode++;
        episodeSteps = 0;
        decisionCount = 0;
        gripperCloseAttempts = 0;
        episodeReward = 0f;
        decisionPending = false;
        settlingActionsRemaining = ResetSettlingActions;
        articulationRoot.TeleportRoot(arenaRoot.TransformPoint(robotRootLocalPosition),
            arenaRoot.rotation * robotRootLocalRotation);
        articulationRoot.GetJointPositions(jointPositions);
        for (int i = 0; i < jointBodies.Length; i++)
        {
            ArticulationBody body = jointBodies[i];
            float home = Mathf.Clamp(Home[i], body.xDrive.lowerLimit, body.xDrive.upperLimit);
            jointPositions[dofStarts[body.index]] = home * Mathf.Deg2Rad;
            SetDriveTarget(body, home);
        }
        SetGripper(GripperOpen);
        jointPositions[dofStarts[leftFinger.index]] = GripperOpen;
        jointPositions[dofStarts[rightFinger.index]] = GripperOpen;
        articulationRoot.SetJointPositions(jointPositions);
        zeroVelocities.Clear();
        for (int i = 0; i < jointPositions.Count; i++) zeroVelocities.Add(0f);
        articulationRoot.SetJointVelocities(zeroVelocities);
        articulationRoot.linearVelocity = Vector3.zero;
        articulationRoot.angularVelocity = Vector3.zero;

        Vector3 spawn = cubeSpawnLocalPosition;
        cubeRandomX = 0f;
        cubeRandomZ = 0f;
        if (randomizeCube)
        {
            cubeRandomX = Random.Range(-cubeRandomXZ, cubeRandomXZ);
            cubeRandomZ = Random.Range(-cubeRandomXZ, cubeRandomXZ);
            spawn.x += cubeRandomX;
            spawn.z += cubeRandomZ;
        }
        cubeRigidbody.position = arenaRoot.TransformPoint(spawn);
        cubeRigidbody.rotation = arenaRoot.rotation * cubeSpawnLocalRotation;
        cubeRigidbody.linearVelocity = Vector3.zero;
        cubeRigidbody.angularVelocity = Vector3.zero;

        previousDistance = Vector3.Distance(tcpTransform.position, cubeTransform.position);
        previousHeight = CubeHeight();
        bestDistance = previousDistance;
        initialDistance = previousDistance;
        bestHeight = previousHeight;
        reachedOnce = false;
        graspRewardGiven = false;
        previousGripperClosed = false;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        decisionPending = true;
        // 0..6: ángulos; 7..13: velocidades articulares, ambos normalizados.
        for (int i = 0; i < 7; i++)
        {
            ArticulationBody body = jointBodies[i];
            ArticulationDrive drive = body.xDrive;
            float middle = (drive.lowerLimit + drive.upperLimit) * 0.5f;
            float halfRange = Mathf.Max(1f, (drive.upperLimit - drive.lowerLimit) * 0.5f);
            sensor.AddObservation(Mathf.Clamp((body.jointPosition[0] * Mathf.Rad2Deg - middle) / halfRange, -1f, 1f));
        }
        for (int i = 0; i < 7; i++)
            sensor.AddObservation(Mathf.Clamp(jointBodies[i].jointVelocity[0] * Mathf.Rad2Deg / jointSpeed, -1f, 1f));

        // 14..16: cubo relativo al TCP en ejes de la arena.
        sensor.AddObservation(arenaRoot.InverseTransformDirection(cubeTransform.position - tcpTransform.position));
        // 17: altura relativa; 18: apertura; 19..21: velocidad local del cubo; 22: etapa.
        sensor.AddObservation(CubeHeight());
        sensor.AddObservation(Mathf.Clamp01(Mathf.InverseLerp(GripperClosedOnCube, GripperOpen,
            leftFinger.jointPosition[0])));
        sensor.AddObservation(arenaRoot.InverseTransformDirection(cubeRigidbody.linearVelocity));
        sensor.AddObservation(stage == TrainingStage.Reach ? 0f : 1f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var continuous = actions.ContinuousActions;
        episodeSteps++;
        totalActionCount++;
        float rewardBefore = episodeReward;
        if (settlingActionsRemaining > 0)
        {
            settlingActionsRemaining--;
            SetGripper(GripperOpen);
            float settlingDistance = Vector3.Distance(tcpTransform.position, cubeTransform.position);
            float settlingHeight = CubeHeight();
            if (!IsFinite(settlingDistance) || !IsFinite(settlingHeight))
            {
                Award(-1f);
                LogDecisionIfNeeded(continuous, settlingDistance, settlingHeight, episodeReward - rewardBefore);
                FinishEpisode("invalid_robot_state");
                EndEpisode();
                return;
            }
            // SetJointPositions se refleja en los transforms tras la simulación física.
            // Excluimos ese salto del shaping por progreso.
            initialDistance = previousDistance = bestDistance = settlingDistance;
            previousHeight = bestHeight = settlingHeight;
            Award(-StepCost);
            LogDecisionIfNeeded(continuous, settlingDistance, settlingHeight, episodeReward - rewardBefore);
            return;
        }
        for (int i = 0; i < 7; i++)
        {
            ArticulationBody body = jointBodies[i];
            ArticulationDrive drive = body.xDrive;
            float delta = Mathf.Clamp(continuous[i], -1f, 1f) * jointSpeed * Time.fixedDeltaTime;
            drive.target = Mathf.Clamp(drive.target + delta, drive.lowerLimit, drive.upperLimit);
            body.xDrive = drive;
        }
        bool closeGripper = continuous[7] < 0f;
        if (closeGripper && !previousGripperClosed) gripperCloseAttempts++;
        SetGripper(closeGripper ? GripperClosedOnCube : GripperOpen);

        float distance = Vector3.Distance(tcpTransform.position, cubeTransform.position);
        float height = CubeHeight();
        Vector3 cubePosition = cubeTransform.position;
        bool invalidRobot = !IsFinite(cubePosition) || !IsFinite(distance) || !IsFinite(height);
        for (int i = 0; i < jointBodies.Length; i++)
        {
            ArticulationDrive drive = jointBodies[i].xDrive;
            float degrees = jointBodies[i].jointPosition[0] * Mathf.Rad2Deg;
            invalidRobot |= !IsFinite(degrees) || degrees < drive.lowerLimit - 10f ||
                            degrees > drive.upperLimit + 10f;
        }
        bool cubeFell = height < -0.1f;
        if (tableCollider != null && IsFinite(cubePosition))
        {
            Bounds table = tableCollider.bounds;
            cubeFell |= cubePosition.x < table.min.x - 0.05f || cubePosition.x > table.max.x + 0.05f ||
                        cubePosition.z < table.min.z - 0.05f || cubePosition.z > table.max.z + 0.05f;
        }
        if (invalidRobot || cubeFell)
        {
            Award(-1f);
            LogDecisionIfNeeded(continuous, distance, height, episodeReward - rewardBefore);
            FinishEpisode(invalidRobot ? "invalid_robot_state" : "cube_fell");
            EndEpisode();
            return;
        }
        bestDistance = Mathf.Min(bestDistance, distance);
        bestHeight = Mathf.Max(bestHeight, height);
        Award((previousDistance - distance) * ProgressRewardScale - StepCost);
        previousDistance = distance;

        if (distance <= reachDistance && !reachedOnce)
        {
            reachedOnce = true;
            Award(0.35f);
            if (stage == TrainingStage.Reach)
            {
                Award(1f);
                episodeSucceeded = true;
                LogDecisionIfNeeded(continuous, distance, height, episodeReward - rewardBefore);
                FinishEpisode("reach_success");
                EndEpisode();
                return;
            }
        }
        if (stage == TrainingStage.Lift)
        {
            if (closeGripper && !previousGripperClosed && !graspRewardGiven && distance <= graspDistance)
            {
                Award(0.15f);
                graspRewardGiven = true;
            }
            Award((height - previousHeight) * HeightRewardScale);
            if (height >= targetHeight)
            {
                Award(3f);
                episodeSucceeded = true;
                LogDecisionIfNeeded(continuous, distance, height, episodeReward - rewardBefore);
                FinishEpisode("lift_success");
                EndEpisode();
                return;
            }
        }
        previousHeight = height;
        previousGripperClosed = closeGripper;
        LogDecisionIfNeeded(continuous, distance, height, episodeReward - rewardBefore);
    }

    protected override void OnDisable()
    {
        StopLoggingForSession();
        TrainingCsvLogger.Unregister(this);
        base.OnDisable();
    }

    public void StopLoggingForSession()
    {
        if (!episodeHasStarted) return;
        FinishEpisode("manual_stop");
        TrainingCsvLogger.Flush();
    }

    private void FinishEpisode(string reason)
    {
        if (!episodeHasStarted) return;
        episodeHasStarted = false;
        if (reason != "manual_stop") completedEpisodeCount++;
        float finalDistance = Vector3.Distance(tcpTransform.position, cubeTransform.position);
        float finalHeight = CubeHeight();
        if (!IsFinite(finalDistance)) finalDistance = previousDistance;
        if (!IsFinite(finalHeight)) finalHeight = previousHeight;
        if (reason != "manual_stop")
        {
            Academy.Instance.StatsRecorder.Add("Franka/Success", episodeSucceeded ? 1f : 0f);
            Academy.Instance.StatsRecorder.Add("Franka/MinimumDistance", bestDistance);
            Academy.Instance.StatsRecorder.Add("Franka/MaximumHeight", bestHeight);
        }
        TrainingCsvLogger.WriteEpisode(new TrainingCsvLogger.Episode {
            stage = stage.ToString(), arenaId = arenaId, arenaEpisode = arenaEpisode,
            steps = episodeSteps, reward = episodeReward, success = episodeSucceeded,
            terminalReason = reason, initialDistance = initialDistance, minimumDistance = bestDistance,
            finalDistance = finalDistance, maximumHeight = bestHeight, finalHeight = finalHeight,
            reachedCube = reachedOnce, gripperCloseAttempts = gripperCloseAttempts,
            cubeRandomX = cubeRandomX, cubeRandomZ = cubeRandomZ
        });
    }

    private void Award(float amount)
    {
        AddReward(amount);
        episodeReward += amount;
    }

    private void LogDecisionIfNeeded(ActionSegment<float> actions, float distance, float height, float instantReward)
    {
        if (!decisionPending) return;
        decisionPending = false;
        decisionCount++;
        if (!TrainingCsvLogger.DetailedStepLogging ||
            decisionCount % TrainingCsvLogger.StepLogEveryNDecisions != 0) return;
        float[] values = new float[8];
        for (int i = 0; i < values.Length; i++) values[i] = actions[i];
        TrainingCsvLogger.WriteDecision(new TrainingCsvLogger.Decision {
            arenaId = arenaId, arenaEpisode = arenaEpisode, episodeStep = episodeSteps,
            stage = stage.ToString(), distance = distance, height = height,
            instantReward = instantReward, cumulativeReward = episodeReward,
            gripperOpening = Mathf.Clamp01(Mathf.InverseLerp(GripperClosedOnCube, GripperOpen,
                leftFinger.jointPosition[0])), actions = values
        });
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var output = actionsOut.ContinuousActions;
        for (int i = 0; i < output.Length; i++) output[i] = 0f;
        output[7] = 1f;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        output[0] = (keyboard.digit1Key.isPressed ? 1f : 0f) - (keyboard.digit2Key.isPressed ? 1f : 0f);
        output[1] = (keyboard.digit3Key.isPressed ? 1f : 0f) - (keyboard.digit4Key.isPressed ? 1f : 0f);
        output[2] = (keyboard.digit5Key.isPressed ? 1f : 0f) - (keyboard.digit6Key.isPressed ? 1f : 0f);
        output[3] = (keyboard.qKey.isPressed ? 1f : 0f) - (keyboard.eKey.isPressed ? 1f : 0f);
        output[4] = (keyboard.aKey.isPressed ? 1f : 0f) - (keyboard.dKey.isPressed ? 1f : 0f);
        output[5] = (keyboard.zKey.isPressed ? 1f : 0f) - (keyboard.cKey.isPressed ? 1f : 0f);
        output[6] = (keyboard.rKey.isPressed ? 1f : 0f) - (keyboard.fKey.isPressed ? 1f : 0f);
        output[7] = keyboard.spaceKey.isPressed ? -1f : 1f;
    }

    private float CubeHeight()
    {
        return arenaRoot.InverseTransformPoint(cubeTransform.position).y - cubeSpawnLocalPosition.y;
    }

    private void SetGripper(float target)
    {
        SetDriveTarget(leftFinger, target);
        SetDriveTarget(rightFinger, target);
    }

    private static void SetDriveTarget(ArticulationBody body, float target)
    {
        ArticulationDrive drive = body.xDrive;
        drive.target = Mathf.Clamp(target, drive.lowerLimit, drive.upperLimit);
        body.xDrive = drive;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

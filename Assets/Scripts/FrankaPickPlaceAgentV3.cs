using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

public class FrankaPickPlaceAgentV3 : Agent
{
    public enum Phase { Reach, Grasp, Lift, Transport, Place }

    [Header("Articulaciones: link1 a link7")]
    public ArticulationBody[] jointBodies;
    public ArticulationBody leftFinger;
    public ArticulationBody rightFinger;
    public FrankaFingerContactV3 leftContact;
    public FrankaFingerContactV3 rightContact;

    [Header("Referencias de esta arena")]
    public Transform arenaRoot;
    public Transform cubeTransform;
    public Rigidbody cubeRigidbody;
    public Collider cubeCollider;
    public Transform tcpTransform;
    public Collider tableCollider;
    public Transform placeTarget;
    [HideInInspector] public int arenaId;

    [Header("Control y criterio de éxito")]
    [Min(1f)] public float jointSpeed = 35f;
    [Min(0.01f)] public float reachDistance = 0.07f;
    [Min(0.01f)] public float graspDistance = 0.09f;
    [Min(0.01f)] public float liftThreshold = 0.30f;
    [Min(0.01f)] public float targetRadius = 0.04f;
    [Min(0.001f)] public float placementHeightTolerance = 0.025f;
    [Min(0.001f)] public float maxPlacementLinearVelocity = 0.04f;
    [Min(0.001f)] public float maxPlacementAngularVelocity = 0.5f;
    [Min(1)] public int stablePlacementDecisions = 12;

    [Header("Regularización suave de postura")]
    [Min(1)] public int singularityCheckEveryNDecisions = 5;
    [Min(0f)] public float manipulabilitySafeThreshold = 0.10f;
    [Min(0f)] public float manipulabilityDangerThreshold = 0.025f;
    [Min(0f)] public float singularityPenaltyWeight = 0.00005f;
    [Range(0.01f, 0.49f)] public float jointLimitSafeFraction = 0.12f;
    [Min(0f)] public float jointLimitPenaltyWeight = 0.00001f;
    [Min(1f)] public float jointVelocitySafeDegPerSec = 52.5f;
    [Min(0f)] public float jointVelocityPenaltyWeight = 0.000005f;

    private const float GripperOpen = -0.04f;
    private const float GripperClosedOnCube = -0.014f;
    private const float StepCost = 0.0005f;
    private const int ResetSettlingActions = 5;
    private const int SustainedGripActions = 5;
    private static readonly float[] Home = { 75f, 10f, 20f, -75f, 0f, 90f, 48.92f };
    private readonly List<float> jointPositions = new List<float>();
    private readonly List<float> zeroVelocities = new List<float>();
    private readonly List<int> dofStarts = new List<int>();
    private ArticulationBody articulationRoot;
    private FrankaTcpManipulabilityV3 manipulabilityProbe;
    private Vector3 cubeInitialLocalPosition;
    private Quaternion cubeInitialLocalRotation;
    private Vector3 targetInitialLocalPosition;
    private Quaternion targetInitialLocalRotation;
    private Vector3 robotNominalLocalPosition;
    private Quaternion robotNominalLocalRotation;
    private float cubeInitialHeightLocal;
    private float cubeRestingHeightLocal;
    private float targetOffsetX;
    private float targetOffsetZ;
    private float robotBaseOffsetX;
    private float robotBaseOffsetZ;
    private Vector3 robotBaseEpisodeLocalPosition;
    private float initialTcpCubeDistance;
    private float minimumTcpCubeDistance;
    private float previousTcpCubeDistance;
    private float initialCubeTargetDistance;
    private float minimumCubeTargetDistance;
    private float previousCubeTargetDistance;
    private float maximumCubeHeight;
    private float previousCubeHeight;
    private float episodeReward;
    private int arenaEpisode;
    private int episodeSteps;
    private int decisionCount;
    private int settlingActionsRemaining;
    private int gripperCloseAttempts;
    private int gripperOpenAttempts;
    private int stableTargetDecisions;
    private int consecutiveHoldingActions;
    private int maxConsecutiveHoldingActions;
    private int bilateralContactActions;
    private int holdingActions;
    private int nearJointLimitActions;
    private float maximumHeldCubeHeight;
    private float minimumJointLimitMarginDeg;
    private float liftGoal;
    private float minimumManipulability;
    private float finalManipulability;
    private double manipulabilitySum;
    private int manipulabilitySamples;
    private int lastManipulabilityDecision;
    private int lastManipulabilityStep;
    private int singularityWarningCount;
    private int jointLimitWarningCount;
    private float maximumJointVelocityDegPerSec;
    private float currentJointLimitPenalty;
    private float currentJointVelocityPenalty;
    private int maxStableDecisionsRewarded;
    private int stepReachedCube;
    private int stepFirstGrasp;
    private int stepFirstLift;
    private int stepEnteredTarget;
    private int stepSuccess;
    private int totalActionCount;
    private int completedEpisodeCount;
    private int timeoutEpisodeCount;
    private bool episodeHasStarted;
    private bool decisionPending;
    private bool previousGripperClosed;
    private bool reachedCube;
    private bool graspAttempted;
    private bool wasGrasped;
    private bool wasLifted;
    private bool enteredTarget;
    private bool releasedInTarget;
    private bool placedSuccessfully;
    private bool placeHeightRewardGiven;
    private Phase phase;

    public int TotalActionCount { get { return totalActionCount; } }
    public int CompletedEpisodeCount { get { return completedEpisodeCount; } }
    public int TimeoutEpisodeCount { get { return timeoutEpisodeCount; } }
    public float RobotBaseOffsetX { get { return robotBaseOffsetX; } }
    public float RobotBaseOffsetZ { get { return robotBaseOffsetZ; } }
    public Phase CurrentPhase { get { return phase; } }

    public override void Initialize()
    {
        if (arenaRoot == null || jointBodies == null || jointBodies.Length != 7 ||
            leftFinger == null || rightFinger == null || leftContact == null || rightContact == null ||
            cubeTransform == null || cubeRigidbody == null || cubeCollider == null ||
            tcpTransform == null || tableCollider == null || placeTarget == null)
        {
            Debug.LogError("FrankaPickPlaceAgentV3: faltan referencias de la arena.", this);
            enabled = false;
            return;
        }
        foreach (ArticulationBody body in jointBodies)
            if (body == null || body.dofCount != 1)
            {
                Debug.LogError("FrankaPickPlaceAgentV3: se requieren siete articulaciones de un DOF.", this);
                enabled = false;
                return;
            }
        foreach (ArticulationBody body in jointBodies[0].GetComponentsInParent<ArticulationBody>())
            if (body.isRoot) articulationRoot = body;
        if (articulationRoot == null)
        {
            Debug.LogError("FrankaPickPlaceAgentV3: falta la raíz articulada.", this);
            enabled = false;
            return;
        }
        cubeInitialLocalPosition = arenaRoot.InverseTransformPoint(cubeTransform.position);
        cubeInitialLocalRotation = Quaternion.Inverse(arenaRoot.rotation) * cubeTransform.rotation;
        targetInitialLocalPosition = arenaRoot.InverseTransformPoint(placeTarget.position);
        targetInitialLocalRotation = Quaternion.Inverse(arenaRoot.rotation) * placeTarget.rotation;
        robotNominalLocalPosition = arenaRoot.InverseTransformPoint(articulationRoot.transform.position);
        robotNominalLocalRotation = Quaternion.Inverse(arenaRoot.rotation) * articulationRoot.transform.rotation;
        targetOffsetX = targetInitialLocalPosition.x - cubeInitialLocalPosition.x;
        targetOffsetZ = targetInitialLocalPosition.z - cubeInitialLocalPosition.z;
        cubeRestingHeightLocal = arenaRoot.InverseTransformPoint(
            new Vector3(cubeTransform.position.x, tableCollider.bounds.max.y + cubeCollider.bounds.extents.y,
                cubeTransform.position.z)).y;
        articulationRoot.GetDofStartIndices(dofStarts);
        manipulabilityProbe = new FrankaTcpManipulabilityV3(jointBodies, tcpTransform);
        leftContact.cubeCollider = cubeCollider;
        rightContact.cubeCollider = cubeCollider;
        TrainingCsvLoggerV3.Register(this);
    }

    public override void OnEpisodeBegin()
    {
        if (articulationRoot == null) return;
        if (episodeHasStarted)
            FinishEpisode(MaxStep > 0 && episodeSteps >= MaxStep ? "timeout" : "other");
        episodeHasStarted = true;
        arenaEpisode++;
        episodeSteps = decisionCount = gripperCloseAttempts = gripperOpenAttempts = 0;
        stableTargetDecisions = maxStableDecisionsRewarded = 0;
        consecutiveHoldingActions = maxConsecutiveHoldingActions = 0;
        bilateralContactActions = holdingActions = nearJointLimitActions = 0;
        maximumHeldCubeHeight = 0f;
        minimumJointLimitMarginDeg = float.PositiveInfinity;
        minimumManipulability = float.PositiveInfinity;
        finalManipulability = float.NaN;
        manipulabilitySum = 0.0;
        manipulabilitySamples = singularityWarningCount = jointLimitWarningCount = 0;
        lastManipulabilityDecision = -Mathf.Max(1, singularityCheckEveryNDecisions);
        lastManipulabilityStep = -1;
        maximumJointVelocityDegPerSec = currentJointLimitPenalty = currentJointVelocityPenalty = 0f;
        liftGoal = Mathf.Clamp(Academy.Instance.EnvironmentParameters.GetWithDefault(
            "lift_goal_m", liftThreshold), 0.05f, liftThreshold);
        stepReachedCube = stepFirstGrasp = stepFirstLift = stepEnteredTarget = stepSuccess = -1;
        episodeReward = 0f;
        decisionPending = previousGripperClosed = false;
        reachedCube = graspAttempted = wasGrasped = wasLifted = false;
        enteredTarget = releasedInTarget = placedSuccessfully = placeHeightRewardGiven = false;
        phase = Phase.Reach;
        settlingActionsRemaining = ResetSettlingActions;
        robotBaseOffsetX = robotBaseOffsetZ = 0f;
        Vector3 robotLocal = robotNominalLocalPosition;
        robotBaseEpisodeLocalPosition = robotLocal;
        articulationRoot.TeleportRoot(arenaRoot.TransformPoint(robotLocal),
            arenaRoot.rotation * robotNominalLocalRotation);
        articulationRoot.GetJointPositions(jointPositions);
        for (int i = 0; i < 7; i++)
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
        leftContact.Clear();
        rightContact.Clear();

        cubeRigidbody.position = arenaRoot.TransformPoint(cubeInitialLocalPosition);
        cubeRigidbody.rotation = arenaRoot.rotation * cubeInitialLocalRotation;
        cubeRigidbody.linearVelocity = Vector3.zero;
        cubeRigidbody.angularVelocity = Vector3.zero;
        placeTarget.position = arenaRoot.TransformPoint(targetInitialLocalPosition);
        placeTarget.rotation = arenaRoot.rotation * targetInitialLocalRotation;
        cubeInitialHeightLocal = cubeInitialLocalPosition.y;
        ResetDistanceBaseline();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        decisionPending = true;
        // 0..6: posiciones articulares; 7..13: velocidades articulares.
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
        // 14..19: TCP -> cubo y cubo -> target, en ejes locales de la arena.
        sensor.AddObservation(arenaRoot.InverseTransformDirection(cubeTransform.position - tcpTransform.position));
        sensor.AddObservation(arenaRoot.InverseTransformDirection(placeTarget.position - cubeTransform.position));
        // 20: altura sobre la posición asentada; 21: apertura real de pinza.
        sensor.AddObservation(CubeHeight());
        sensor.AddObservation(GripperOpening());
        // 22..27: velocidades lineal y angular locales del cubo.
        sensor.AddObservation(arenaRoot.InverseTransformDirection(cubeRigidbody.linearVelocity));
        sensor.AddObservation(arenaRoot.InverseTransformDirection(cubeRigidbody.angularVelocity));
        // 28..32: fase one-hot. La base permanece fija y no requiere observaciones de offset.
        for (int i = 0; i < 5; i++) sensor.AddObservation((int)phase == i ? 1f : 0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var continuous = actions.ContinuousActions;
        episodeSteps++;
        totalActionCount++;
        float rewardBefore = episodeReward;
        bool isDecision = decisionPending;
        if (settlingActionsRemaining > 0)
        {
            settlingActionsRemaining--;
            SetGripper(GripperOpen);
            if (!IsFinite(tcpTransform.position) || !IsFinite(cubeTransform.position))
            {
                Award(-1f);
                EndWithReason("invalid_robot_state", continuous, rewardBefore);
                return;
            }
            cubeInitialHeightLocal = arenaRoot.InverseTransformPoint(cubeTransform.position).y;
            ResetDistanceBaseline();
            Award(-StepCost);
            LogDecisionIfNeeded(continuous, rewardBefore);
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
        // Hysteresis: actions close below -0.25 and open above +0.25.
        // The interval between them retains the last command.
        bool closeGripper = continuous[7] <= -0.25f ||
                            (previousGripperClosed && continuous[7] < 0.25f);
        if (closeGripper && !previousGripperClosed) gripperCloseAttempts++;
        if (!closeGripper && previousGripperClosed) gripperOpenAttempts++;
        SetGripper(closeGripper ? GripperClosedOnCube : GripperOpen);

        float tcpDistance = Vector3.Distance(tcpTransform.position, cubeTransform.position);
        float targetDistance = CubeTargetDistance();
        float height = CubeHeight();
        float linearSpeed = cubeRigidbody.linearVelocity.magnitude;
        float angularSpeed = cubeRigidbody.angularVelocity.magnitude;
        if (!IsFinite(cubeTransform.position) || !IsFinite(tcpDistance) || !IsFinite(targetDistance) ||
            !IsFinite(height) || !IsFinite(linearSpeed) || !IsFinite(angularSpeed) || !JointStateValid())
        {
            Award(-1f);
            EndWithReason("invalid_robot_state", continuous, rewardBefore);
            return;
        }
        if (height < -0.10f)
        {
            Award(-1f);
            EndWithReason("cube_fell", continuous, rewardBefore);
            return;
        }
        Bounds table = tableCollider.bounds;
        Vector3 cubeWorld = cubeTransform.position;
        if (cubeWorld.x < table.min.x - 0.05f || cubeWorld.x > table.max.x + 0.05f ||
            cubeWorld.z < table.min.z - 0.05f || cubeWorld.z > table.max.z + 0.05f)
        {
            Award(-1f);
            EndWithReason("cube_out_of_bounds", continuous, rewardBefore);
            return;
        }

        float bestTargetBefore = minimumCubeTargetDistance;
        float bestHeightBefore = maximumCubeHeight;
        minimumTcpCubeDistance = Mathf.Min(minimumTcpCubeDistance, tcpDistance);
        Award(-StepCost);
        if (currentJointLimitPenalty > 0f) Award(-currentJointLimitPenalty);
        if (currentJointVelocityPenalty > 0f) Award(-currentJointVelocityPenalty);
        if (isDecision && decisionCount - lastManipulabilityDecision >=
            Mathf.Max(1, singularityCheckEveryNDecisions))
        {
            RecordManipulability(manipulabilityProbe.Measure(), true);
            lastManipulabilityDecision = decisionCount;
            lastManipulabilityStep = episodeSteps;
        }

        if (!wasGrasped)
        {
            // La diferencia telescópica no premia oscilaciones repetidas cerca del cubo.
            Award(2f * (previousTcpCubeDistance - tcpDistance));
            if (!reachedCube && tcpDistance <= reachDistance)
            {
                reachedCube = true;
                stepReachedCube = episodeSteps;
                phase = Phase.Grasp;
                Award(0.35f);
            }
            if (closeGripper && !previousGripperClosed && tcpDistance <= graspDistance)
                graspAttempted = true;
        }

        bool twoFingerContact = leftContact.TouchingCube && rightContact.TouchingCube;
        if (twoFingerContact) bilateralContactActions++;
        bool holding = closeGripper && twoFingerContact &&
                       tcpDistance <= graspDistance + 0.04f;
        consecutiveHoldingActions = holding ? consecutiveHoldingActions + 1 : 0;
        maxConsecutiveHoldingActions = Mathf.Max(maxConsecutiveHoldingActions, consecutiveHoldingActions);
        if (holding)
        {
            holdingActions++;
            maximumHeldCubeHeight = Mathf.Max(maximumHeldCubeHeight, height);
        }
        if (!wasGrasped && reachedCube && consecutiveHoldingActions >= SustainedGripActions)
        {
            wasGrasped = true;
            stepFirstGrasp = episodeSteps;
            phase = Phase.Lift;
            Award(0.6f);
        }
        if (wasGrasped && !wasLifted)
        {
            float delta = height - previousCubeHeight;
            if (holding)
                Award(5f * (delta > 0f ? Mathf.Min(delta, Mathf.Max(0f, height - bestHeightBefore)) : delta));
            if (holding && height >= liftGoal)
            {
                wasLifted = true;
                stepFirstLift = episodeSteps;
                phase = Phase.Transport;
                minimumCubeTargetDistance = targetDistance;
                Award(2f);
                if (liftGoal < liftThreshold - 0.001f)
                {
                    maximumCubeHeight = Mathf.Max(maximumCubeHeight, height);
                    Award(3f);
                    EndWithReason("curriculum_lift_success", continuous, rewardBefore);
                    return;
                }
            }
        }

        if (wasLifted)
        {
            float delta = previousCubeTargetDistance - targetDistance;
            if (holding && height >= 0.06f)
                Award(3f * (delta > 0f ? Mathf.Min(delta,
                    Mathf.Max(0f, bestTargetBefore - targetDistance)) : delta));
            if (!enteredTarget && targetDistance <= targetRadius && height >= 0.06f)
            {
                enteredTarget = true;
                stepEnteredTarget = episodeSteps;
                phase = Phase.Place;
                Award(0.6f);
            }
            if (enteredTarget && !placeHeightRewardGiven && targetDistance <= targetRadius &&
                Mathf.Abs(CubeLocalY() - cubeRestingHeightLocal) <= placementHeightTolerance)
            {
                placeHeightRewardGiven = true;
                Award(0.25f);
            }
            if (!releasedInTarget && V3PlacementCriteria.CanReleaseInTarget(
                enteredTarget, previousGripperClosed, !closeGripper, targetDistance,
                targetRadius, CubeLocalY() - cubeRestingHeightLocal, linearSpeed, angularSpeed,
                placementHeightTolerance, maxPlacementLinearVelocity, maxPlacementAngularVelocity))
            {
                releasedInTarget = true;
                Award(0.5f);
            }
            bool stable = V3PlacementCriteria.IsStable(wasLifted, enteredTarget, releasedInTarget,
                targetDistance <= targetRadius, !closeGripper && GripperOpening() >= 0.8f,
                CubeLocalY() - cubeRestingHeightLocal, linearSpeed, angularSpeed,
                placementHeightTolerance, maxPlacementLinearVelocity, maxPlacementAngularVelocity);
            if (isDecision)
            {
                stableTargetDecisions = stable ? stableTargetDecisions + 1 : 0;
                if (stable && stableTargetDecisions > maxStableDecisionsRewarded)
                {
                    maxStableDecisionsRewarded = stableTargetDecisions;
                    Award(0.02f);
                }
            }
            if (stableTargetDecisions >= stablePlacementDecisions)
            {
                placedSuccessfully = true;
                stepSuccess = episodeSteps;
                Award(10f);
                EndWithReason("place_success", continuous, rewardBefore);
                return;
            }
        }
        previousTcpCubeDistance = tcpDistance;
        previousCubeTargetDistance = targetDistance;
        previousCubeHeight = height;
        minimumCubeTargetDistance = Mathf.Min(minimumCubeTargetDistance, targetDistance);
        maximumCubeHeight = Mathf.Max(maximumCubeHeight, height);
        previousGripperClosed = closeGripper;
        LogDecisionIfNeeded(continuous, rewardBefore);
    }

    protected override void OnDisable()
    {
        StopLoggingForSession();
        TrainingCsvLoggerV3.Unregister(this);
        base.OnDisable();
    }

    public void StopLoggingForSession()
    {
        if (!episodeHasStarted) return;
        FinishEpisode("manual_stop");
        TrainingCsvLoggerV3.Flush();
    }

    private void EndWithReason(string reason, ActionSegment<float> actions, float rewardBefore)
    {
        LogDecisionIfNeeded(actions, rewardBefore);
        FinishEpisode(reason);
        EndEpisode();
    }

    private void FinishEpisode(string reason)
    {
        if (!episodeHasStarted) return;
        episodeHasStarted = false;
        if (manipulabilityProbe != null && lastManipulabilityStep != episodeSteps &&
            IsFinite(tcpTransform.position))
            RecordManipulability(manipulabilityProbe.Measure(), false);
        if (reason != "manual_stop") completedEpisodeCount++;
        if (reason == "timeout") timeoutEpisodeCount++;
        float finalTcpDistance = Vector3.Distance(tcpTransform.position, cubeTransform.position);
        float finalTargetDistance = CubeTargetDistance();
        float finalHeight = CubeHeight();
        if (!IsFinite(finalTcpDistance)) finalTcpDistance = previousTcpCubeDistance;
        if (!IsFinite(finalTargetDistance)) finalTargetDistance = previousCubeTargetDistance;
        if (!IsFinite(finalHeight)) finalHeight = previousCubeHeight;
        if (reason != "manual_stop")
        {
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("V3/ReachSuccess", reachedCube ? 1f : 0f);
            stats.Add("V3/GraspSuccess", wasGrasped ? 1f : 0f);
            stats.Add("V3/LiftSuccess", wasLifted ? 1f : 0f);
            stats.Add("V3/TargetEntry", enteredTarget ? 1f : 0f);
            stats.Add("V3/PlaceSuccess", placedSuccessfully ? 1f : 0f);
            stats.Add("V3/MinimumTcpCubeDistance", minimumTcpCubeDistance);
            stats.Add("V3/MaximumCubeHeight", maximumCubeHeight);
            stats.Add("V3/LiftGoal", liftGoal);
            stats.Add("V3/MaxConsecutiveHoldingActions", maxConsecutiveHoldingActions);
            stats.Add("V3/MaximumHeldCubeHeight", maximumHeldCubeHeight);
            stats.Add("V3/NearJointLimitActions", nearJointLimitActions);
            if (manipulabilitySamples > 0)
            {
                stats.Add("V3/MinimumManipulability", minimumManipulability);
                stats.Add("V3/MeanManipulability", (float)(manipulabilitySum / manipulabilitySamples));
                stats.Add("V3/SingularityWarningRate",
                    (float)singularityWarningCount / manipulabilitySamples);
            }
            if (!float.IsPositiveInfinity(minimumJointLimitMarginDeg))
                stats.Add("V3/MinimumJointLimitMargin", minimumJointLimitMarginDeg);
            stats.Add("V3/MaximumJointVelocity", maximumJointVelocityDegPerSec);
            stats.Add("V3/MinimumCubeTargetDistance", minimumCubeTargetDistance);
            stats.Add("V3/StepsToReach", stepReachedCube > 0 ? stepReachedCube : 0f);
            stats.Add("V3/StepsToLift", stepFirstLift > 0 ? stepFirstLift : 0f);
            stats.Add("V3/StepsToPlace", stepSuccess > 0 ? stepSuccess : 0f);
        }
        TrainingCsvLoggerV3.WriteEpisode(new TrainingCsvLoggerV3.Episode {
            arenaId = arenaId, arenaEpisode = arenaEpisode, steps = episodeSteps,
            reward = episodeReward, success = placedSuccessfully, terminalReason = reason,
            robotBaseOffsetX = robotBaseOffsetX, robotBaseOffsetZ = robotBaseOffsetZ,
            robotBaseLocalPosition = robotBaseEpisodeLocalPosition,
            cubeSpawnLocalPosition = cubeInitialLocalPosition,
            targetLocalPosition = targetInitialLocalPosition,
            initialTcpCubeDistance = initialTcpCubeDistance,
            minimumTcpCubeDistance = minimumTcpCubeDistance, finalTcpCubeDistance = finalTcpDistance,
            cubeInitialHeight = cubeInitialHeightLocal, maximumCubeHeight = maximumCubeHeight,
            finalCubeHeight = finalHeight, targetOffsetX = targetOffsetX, targetOffsetZ = targetOffsetZ,
            initialCubeTargetDistance = initialCubeTargetDistance,
            minimumCubeTargetDistance = minimumCubeTargetDistance, finalCubeTargetDistance = finalTargetDistance,
            reachedCube = reachedCube, graspAttempted = graspAttempted, wasGrasped = wasGrasped,
            wasLifted = wasLifted, enteredTarget = enteredTarget,
            releasedInTarget = releasedInTarget, placedSuccessfully = placedSuccessfully,
            gripperCloseAttempts = gripperCloseAttempts, gripperOpenAttempts = gripperOpenAttempts,
            stepReachedCube = stepReachedCube, stepFirstGrasp = stepFirstGrasp,
            stepFirstLift = stepFirstLift, stepEnteredTarget = stepEnteredTarget,
            stepSuccess = stepSuccess, stableTargetDecisions = stableTargetDecisions,
            finalCubeLinearSpeed = cubeRigidbody.linearVelocity.magnitude,
            finalCubeAngularSpeed = cubeRigidbody.angularVelocity.magnitude,
            liftGoal = liftGoal, bilateralContactActions = bilateralContactActions,
            holdingActions = holdingActions, maxConsecutiveHoldingActions = maxConsecutiveHoldingActions,
            maximumHeldCubeHeight = maximumHeldCubeHeight,
            nearJointLimitActions = nearJointLimitActions,
            minimumJointLimitMarginDeg = float.IsPositiveInfinity(minimumJointLimitMarginDeg)
                ? 0f : minimumJointLimitMarginDeg,
            minimumJointLimitMargin = float.IsPositiveInfinity(minimumJointLimitMarginDeg)
                ? float.NaN : minimumJointLimitMarginDeg,
            minimumManipulability = manipulabilitySamples > 0 ? minimumManipulability : float.NaN,
            meanManipulability = manipulabilitySamples > 0
                ? (float)(manipulabilitySum / manipulabilitySamples) : float.NaN,
            finalManipulability = finalManipulability,
            maximumJointVelocity = maximumJointVelocityDegPerSec,
            singularityWarningCount = singularityWarningCount,
            jointLimitWarningCount = jointLimitWarningCount
        });
    }

    private void ResetDistanceBaseline()
    {
        initialTcpCubeDistance = previousTcpCubeDistance = minimumTcpCubeDistance =
            Vector3.Distance(tcpTransform.position, cubeTransform.position);
        initialCubeTargetDistance = previousCubeTargetDistance = minimumCubeTargetDistance = CubeTargetDistance();
        previousCubeHeight = maximumCubeHeight = CubeHeight();
    }

    private float CubeLocalY() { return arenaRoot.InverseTransformPoint(cubeTransform.position).y; }
    private float CubeHeight() { return CubeLocalY() - cubeInitialHeightLocal; }
    private float CubeTargetDistance()
    {
        Vector3 cube = arenaRoot.InverseTransformPoint(cubeTransform.position);
        Vector3 target = arenaRoot.InverseTransformPoint(placeTarget.position);
        return Vector2.Distance(new Vector2(cube.x, cube.z), new Vector2(target.x, target.z));
    }

    private float GripperOpening()
    {
        return Mathf.Clamp01(Mathf.InverseLerp(GripperClosedOnCube, GripperOpen,
            leftFinger.jointPosition[0]));
    }

    private bool JointStateValid()
    {
        float actionMargin = float.PositiveInfinity;
        float normalizedMargin = 1f;
        float actionMaxVelocity = 0f;
        foreach (ArticulationBody body in jointBodies)
        {
            float angle = body.jointPosition[0] * Mathf.Rad2Deg;
            float velocity = Mathf.Abs(body.jointVelocity[0] * Mathf.Rad2Deg);
            ArticulationDrive drive = body.xDrive;
            if (!IsFinite(angle) || !IsFinite(velocity) ||
                angle < drive.lowerLimit - 10f || angle > drive.upperLimit + 10f)
                return false;
            actionMargin = Mathf.Min(actionMargin,
                Mathf.Min(angle - drive.lowerLimit, drive.upperLimit - angle));
            float range = Mathf.Max(1f, drive.upperLimit - drive.lowerLimit);
            normalizedMargin = Mathf.Min(normalizedMargin,
                Mathf.Min((angle - drive.lowerLimit) / range,
                    (drive.upperLimit - angle) / range));
            actionMaxVelocity = Mathf.Max(actionMaxVelocity, velocity);
        }
        minimumJointLimitMarginDeg = Mathf.Min(minimumJointLimitMarginDeg, actionMargin);
        if (actionMargin <= 5f) nearJointLimitActions++;
        maximumJointVelocityDegPerSec = Mathf.Max(maximumJointVelocityDegPerSec, actionMaxVelocity);
        float safeFraction = Mathf.Max(0.01f, jointLimitSafeFraction);
        if (normalizedMargin < safeFraction) jointLimitWarningCount++;
        currentJointLimitPenalty = jointLimitPenaltyWeight *
            Mathf.Clamp01((safeFraction - normalizedMargin) / safeFraction);
        currentJointVelocityPenalty = jointVelocityPenaltyWeight *
            Mathf.Clamp01((actionMaxVelocity - jointVelocitySafeDegPerSec) /
                          Mathf.Max(1f, jointVelocitySafeDegPerSec));
        return true;
    }

    private void RecordManipulability(float value, bool applyPenalty)
    {
        if (!IsFinite(value) || value < 0f) return;
        finalManipulability = value;
        minimumManipulability = Mathf.Min(minimumManipulability, value);
        manipulabilitySum += value;
        manipulabilitySamples++;
        float danger = Mathf.Max(0.000001f, manipulabilityDangerThreshold);
        float safe = Mathf.Max(danger + 0.000001f, manipulabilitySafeThreshold);
        if (value >= safe) return;
        singularityWarningCount++;
        if (!applyPenalty || singularityPenaltyWeight <= 0f) return;
        float severity = value >= danger
            ? (safe - value) / (safe - danger)
            : 1f + Mathf.Clamp01((danger - value) / danger);
        Award(-singularityPenaltyWeight * severity);
    }

    private void LogDecisionIfNeeded(ActionSegment<float> actions, float rewardBefore)
    {
        if (!decisionPending) return;
        decisionPending = false;
        decisionCount++;
        if (!TrainingCsvLoggerV3.DetailedStepLogging ||
            decisionCount % TrainingCsvLoggerV3.StepLogEveryNDecisions != 0) return;
        float[] values = new float[8];
        for (int i = 0; i < 8; i++) values[i] = actions[i];
        TrainingCsvLoggerV3.WriteDecision(new TrainingCsvLoggerV3.Decision {
            arenaId = arenaId, arenaEpisode = arenaEpisode, episodeStep = episodeSteps,
            phase = phase.ToString(), tcpCubeDistance = Vector3.Distance(tcpTransform.position, cubeTransform.position),
            cubeTargetDistance = CubeTargetDistance(), cubeHeight = CubeHeight(),
            instantReward = episodeReward - rewardBefore, cumulativeReward = episodeReward,
            gripperOpening = GripperOpening(), wasLifted = wasLifted,
            insideTarget = CubeTargetDistance() <= targetRadius,
            robotOffsetX = robotBaseOffsetX, robotOffsetZ = robotBaseOffsetZ, actions = values
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

    private void Award(float amount) { AddReward(amount); episodeReward += amount; }

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
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

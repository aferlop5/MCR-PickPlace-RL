using System;
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
    [Range(-1f, 0f)] public float gripperCloseThreshold = -0.25f;
    [Range(0f, 1f)] public float gripperOpenThreshold = 0.60f;

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
    private const float PreviousTimeoutSeconds = 60f;
    private const float GraspBonus = 0.15f;
    private const float LiftProgressScale = 18f;
    private const float PreGraspLiftScale = 2f;
    private const float HeldDropScale = 4f;
    private const float LiftMilestoneBonus = 0.10f;
    private const float GraspLossPenalty = 0.15f;
    private const float PrePlaceProgressScale = 8f;
    private const float ReleaseReadyBonus = 0.35f;
    private const float ControlledReleaseBonus = 0.50f;
    private const float HighReleasePenalty = 0.50f;
    private const float PhysicalPlaceBonus = 2f;
    private const float ControlledPlaceExtraBonus = 8f;
    private const float ReleaseReadyRadius = 0.035f;
    private const float MaximumControlledReleaseHeight = 0.05f;
    private const float MinimumControlledDescentVelocity = -0.40f;
    private const int PhysicalReleaseLossConfirmationActions = 2;
    private const int ReleaseConfirmationActions = 15;
    private const int GraspLossConfirmationActions = 3;
    private const int MaxPenalizedGraspLosses = 3;
    private const int ResetSettlingActions = 5;
    private const int SustainedGripDecisions = 3;
    private const float MaximumGraspOpening = 0.8f;
    private const float MaximumAlignmentReward = 0.20f;
    private enum RewardKind { Reach, Grasp, Lift, Transport, Place, Singularity, JointLimit, Velocity, Time, Failure, GraspLoss, Timeout, PrePlace, ReleaseReady, ControlledRelease, HighRelease, PrePlaceMilestones, ControlledPlace, Alignment }
    private static readonly float[] Home = { 75f, 10f, 20f, -75f, 0f, 90f, 48.92f };
    private static readonly float[] PrePlaceHeights = { 0.30f, 0.25f, 0.20f, 0.15f, 0.10f, 0.05f };
    private static readonly float[] PrePlaceBonuses = { 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.40f };
    private static readonly string[] PrePlaceEventNames = {
        "preplace_30cm", "preplace_25cm", "preplace_20cm",
        "preplace_15cm", "preplace_10cm", "preplace_5cm"
    };
    private readonly List<float> jointPositions = new List<float>();
    private readonly List<float> zeroVelocities = new List<float>();
    private readonly List<int> dofStarts = new List<int>();
    private ArticulationBody articulationRoot;
    private FrankaTcpManipulabilityV3 manipulabilityProbe;
    private Collider targetCollider;
    private Vector3 cubeInitialLocalPosition;
    private Quaternion cubeInitialLocalRotation;
    private Vector3 targetInitialLocalPosition;
    private Quaternion targetInitialLocalRotation;
    private Vector3 robotNominalLocalPosition;
    private Quaternion robotNominalLocalRotation;
    private float cubeInitialHeightLocal;
    private float initialObjectYawDeg;
    private int orientationCurriculumLevel;
    private float orientationYawMinDeg, orientationYawMaxDeg;
    private float orientationFocusProbability;
    private string orientationSamplingMode;
    private float bestEligibleAlignment, alignmentAtFirstGrasp;
    private int stepFirstPhysicalGraspLoss;
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
    private readonly float[] rewardParts = new float[(int)RewardKind.Alignment + 1];
    private float rewardPositiveTotal, rewardPenaltyTotal, liftProgressRewardTotal;
    private readonly float[] jointMinimumMargins = new float[7];
    private readonly float[] jointMaximumVelocities = new float[7];
    private readonly double[] jointActionSums = new double[7];
    private double jointActionMagnitudeSum;
    private float maximumAbsJointAction;
    private int jointActionSamples, saturatedJointDecisions, closeActionDecisions, openActionDecisions;
    private float minimumJointLimitMarginNormalized;
    private int jointClosestToLimit;
    private readonly TrainingCsvLoggerV3.EpisodeEvent[] episodeEvents = new TrainingCsvLoggerV3.EpisodeEvent[20];
    private readonly float[][] eventJointAngles = new float[20][];
    private int eventCount;
    private int reachEventIndex, bilateralEventIndex, graspEventIndex, maxHeightEventIndex, liftEventIndex, lossEventIndex;
    private int targetAlignedEventIndex, prePlaceEligibleEventIndex, releaseReadyEventIndex, releaseEventIndex;
    private int placeEventIndex, controlledPlaceEventIndex;
    private int releaseCommandEventIndex, confirmedGraspLossEventIndex;
    private readonly int[] prePlaceEventIndices = new int[6];
    private readonly bool[] reachedPrePlaceMilestone = new bool[6];
    private readonly int[] stepPrePlaceMilestone = new int[6];
    private bool targetAligned, releaseReadyReached, releasedCorrectly, releasedTooHigh;
    private bool prePlaceEligibleReached;
    private bool controlledPlaceSuccess, controlledReleaseForCurrentPlacement;
    private bool previousPrePlaceEligible, releaseCandidateActive, releaseCandidateReady;
    private bool currentPrePlaceRetentionValid, previousRobustPrePlaceHolding;
    private bool releaseCandidateContinuouslyHeld, releaseWasContinuouslyHeldBeforeLoss;
    private bool releaseLossConfirmed;
    private float bestReleaseHeightError, previousReleaseHeightError, releaseCommandAction;
    private int releaseCandidateStartStep, stepTargetAligned, stepReleaseReady, stepRelease;
    private int stepPrePlaceEligible, retentionDecisionsAtTargetAlignment, retentionDecisionsAtPrePlaceEligible;
    private float releaseHeightAtPrePlaceEligible, verticalVelocityAtPrePlaceEligible;
    private int releaseLossCandidateActions, releaseLossCandidateStep;
    private int stepReleaseCommand, stepGraspLossForRelease, stepReleaseConfirmed;
    private float horizontalAtAlignment, releaseHeightAtAlignment, horizontalAtReady;
    private float horizontalAtRelease, releaseHeightAtRelease, verticalVelocityAtRelease;
    private float gripperActionAtRelease, gripperOpeningAtRelease;
    private float releaseCandidateCommandHeight, releaseCandidateCommandVelocity;
    private float releaseCandidateCommandHorizontal, releaseCandidateCommandOpening;
    private float releaseCandidateCommandCubeHeight, releaseCandidateCommandTcpDistance;
    private float releaseLossCandidateHeight, releaseLossCandidateVelocity;
    private float releaseLossCandidateHorizontal, releaseLossCandidateOpening;
    private float releaseLossCandidateCubeHeight, releaseLossCandidateTcpDistance;
    private float releaseCommandHeight, graspLossHeightForRelease, releaseConfirmedHeight;
    private float releaseCommandVerticalVelocity, graspLossVerticalVelocity;
    private float minimumReleaseHeightWhileHeld, bestPrePlaceHeight;
    private readonly bool[] reachedLiftMilestone = new bool[5];
    private readonly int[] stepLiftMilestone = new int[5];
    private float rewardedLiftHighWater;
    private int lossCandidateActions, penalizedGraspLosses;
    private bool retentionActive;
    private int postGraspActionSamples, postGraspOpenActionDecisions, postGraspCloseActionDecisions;
    private double postGraspActionSum;
    private float postGraspMaxAction, postGraspMinAction;
    private int stepFirstGraspLoss;
    private float heightAtFirstGraspLoss, openingAtFirstGraspLoss, distanceAtFirstGraspLoss;
    private double episodeStartSimTime;
    private float episodeDurationSimSeconds;
    private int stepFirstGraspAttempt, stepFirstBilateralContact, stepFirstLiftMotion, stepMaxHeight;
    private int graspAttemptCount, leftContactDecisions, rightContactDecisions;
    private int leftOnlyContactDecisions, rightOnlyContactDecisions, bilateralContactDecisions;
    private int longestBilateralContactDecisions, consecutiveBilateralContactDecisions;
    private int consecutiveHoldingDecisions, retentionDecisions, longestRetentionDecisions, graspLostCount;
    private int consecutiveRetentionDecisions;
    private float openingAtReach, openingAtGrasp, openingAtMaxHeight;
    private float distanceAtReach, distanceAtGrasp, distanceAtMaxHeight;
    private float heightAtReach, heightAtGrasp;
    private float maximumCubeVerticalVelocity, positiveVerticalVelocitySum;
    private int positiveVerticalVelocitySamples;
    private float manipulabilityAtReach, manipulabilityAtGrasp, manipulabilityAtMaxHeight;
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
    private bool previousHolding;
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
        targetCollider = placeTarget.GetComponent<Collider>();
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
        for (int i = 0; i < eventJointAngles.Length; i++)
            eventJointAngles[i] = new float[7];
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
        episodeStartSimTime = Time.fixedTimeAsDouble;
        episodeDurationSimSeconds = 0f;
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
        // Un único parámetro discreto permite cambiar de nivel al reanudar sin usar
        // un criterio global de éxito que ocultaría los sectores difíciles.
        float requestedOrientationLevel = Academy.Instance.EnvironmentParameters.GetWithDefault(
            "prism_yaw_level", 3f);
        orientationCurriculumLevel = IsFinite(requestedOrientationLevel)
            ? Mathf.Clamp(Mathf.RoundToInt(requestedOrientationLevel), 1, 3) : 3;
        orientationYawMinDeg = orientationCurriculumLevel == 1 ? 30f :
            orientationCurriculumLevel == 2 ? 15f : 0f;
        orientationYawMaxDeg = 180f - orientationYawMinDeg;
        float requestedFocusProbability = Academy.Instance.EnvironmentParameters.GetWithDefault(
            "prism_focus_probability", 0f);
        orientationFocusProbability = IsFinite(requestedFocusProbability)
            ? Mathf.Clamp01(requestedFocusProbability) : 0f;
        bestEligibleAlignment = alignmentAtFirstGrasp = float.NaN;
        stepFirstPhysicalGraspLoss = -1;
        stepReachedCube = stepFirstGrasp = stepFirstLift = stepEnteredTarget = stepSuccess = -1;
        episodeReward = 0f;
        Array.Clear(rewardParts, 0, rewardParts.Length);
        rewardPositiveTotal = rewardPenaltyTotal = liftProgressRewardTotal = 0f;
        jointActionMagnitudeSum = maximumAbsJointAction = 0f;
        jointActionSamples = saturatedJointDecisions = closeActionDecisions = openActionDecisions = 0;
        minimumJointLimitMarginNormalized = float.PositiveInfinity;
        jointClosestToLimit = -1;
        for (int i = 0; i < 7; i++)
        {
            jointMinimumMargins[i] = float.PositiveInfinity;
            jointMaximumVelocities[i] = 0f;
            jointActionSums[i] = 0.0;
        }
        eventCount = 0;
        reachEventIndex = bilateralEventIndex = graspEventIndex = maxHeightEventIndex = liftEventIndex = lossEventIndex = -1;
        targetAlignedEventIndex = prePlaceEligibleEventIndex = releaseReadyEventIndex = releaseEventIndex = -1;
        placeEventIndex = controlledPlaceEventIndex = -1;
        releaseCommandEventIndex = confirmedGraspLossEventIndex = -1;
        targetAligned = releaseReadyReached = releasedCorrectly = releasedTooHigh = false;
        prePlaceEligibleReached = false;
        controlledPlaceSuccess = controlledReleaseForCurrentPlacement = false;
        previousPrePlaceEligible = releaseCandidateActive = releaseCandidateReady = false;
        currentPrePlaceRetentionValid = previousRobustPrePlaceHolding = false;
        releaseCandidateContinuouslyHeld = releaseWasContinuouslyHeldBeforeLoss = false;
        releaseLossConfirmed = false;
        bestReleaseHeightError = float.PositiveInfinity;
        minimumReleaseHeightWhileHeld = bestPrePlaceHeight = float.PositiveInfinity;
        previousReleaseHeightError = releaseCommandAction = float.NaN;
        releaseCandidateStartStep = stepTargetAligned = stepReleaseReady = stepRelease = -1;
        stepPrePlaceEligible = retentionDecisionsAtTargetAlignment =
            retentionDecisionsAtPrePlaceEligible = -1;
        releaseHeightAtPrePlaceEligible = verticalVelocityAtPrePlaceEligible = float.NaN;
        releaseLossCandidateActions = 0;
        releaseLossCandidateStep = stepReleaseCommand = stepGraspLossForRelease =
            stepReleaseConfirmed = -1;
        horizontalAtAlignment = releaseHeightAtAlignment = horizontalAtReady = float.NaN;
        horizontalAtRelease = releaseHeightAtRelease = verticalVelocityAtRelease = float.NaN;
        gripperActionAtRelease = gripperOpeningAtRelease = float.NaN;
        releaseCandidateCommandHeight = releaseCandidateCommandVelocity = float.NaN;
        releaseCandidateCommandHorizontal = releaseCandidateCommandOpening = float.NaN;
        releaseCandidateCommandCubeHeight = releaseCandidateCommandTcpDistance = float.NaN;
        releaseLossCandidateHeight = releaseLossCandidateVelocity = float.NaN;
        releaseLossCandidateHorizontal = releaseLossCandidateOpening = float.NaN;
        releaseLossCandidateCubeHeight = releaseLossCandidateTcpDistance = float.NaN;
        releaseCommandHeight = graspLossHeightForRelease = releaseConfirmedHeight = float.NaN;
        releaseCommandVerticalVelocity = graspLossVerticalVelocity = float.NaN;
        for (int i = 0; i < reachedPrePlaceMilestone.Length; i++)
        {
            reachedPrePlaceMilestone[i] = false;
            stepPrePlaceMilestone[i] = prePlaceEventIndices[i] = -1;
        }
        for (int i = 0; i < 5; i++)
        {
            reachedLiftMilestone[i] = false;
            stepLiftMilestone[i] = -1;
        }
        rewardedLiftHighWater = 0f;
        lossCandidateActions = penalizedGraspLosses = 0;
        retentionActive = false;
        postGraspActionSamples = postGraspOpenActionDecisions = postGraspCloseActionDecisions = 0;
        postGraspActionSum = 0.0;
        postGraspMaxAction = float.NegativeInfinity;
        postGraspMinAction = float.PositiveInfinity;
        stepFirstGraspLoss = -1;
        heightAtFirstGraspLoss = openingAtFirstGraspLoss = distanceAtFirstGraspLoss = float.NaN;
        stepFirstGraspAttempt = stepFirstBilateralContact = stepFirstLiftMotion = stepMaxHeight = -1;
        graspAttemptCount = leftContactDecisions = rightContactDecisions = 0;
        leftOnlyContactDecisions = rightOnlyContactDecisions = bilateralContactDecisions = 0;
        longestBilateralContactDecisions = consecutiveBilateralContactDecisions = 0;
        consecutiveHoldingDecisions = retentionDecisions = longestRetentionDecisions = 0;
        graspLostCount = consecutiveRetentionDecisions = 0;
        openingAtReach = openingAtGrasp = openingAtMaxHeight = float.NaN;
        distanceAtReach = distanceAtGrasp = distanceAtMaxHeight = float.NaN;
        heightAtReach = heightAtGrasp = float.NaN;
        maximumCubeVerticalVelocity = positiveVerticalVelocitySum = 0f;
        positiveVerticalVelocitySamples = 0;
        manipulabilityAtReach = manipulabilityAtGrasp = manipulabilityAtMaxHeight = float.NaN;
        decisionPending = previousGripperClosed = previousHolding = false;
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
        bool focused = orientationCurriculumLevel == 1 &&
            UnityEngine.Random.value < orientationFocusProbability;
        orientationSamplingMode = focused ? "focused" : "uniform";
        float cubeYaw;
        if (focused)
        {
            bool lowerEdge = UnityEngine.Random.value < 0.5f;
            cubeYaw = lowerEdge ? UnityEngine.Random.Range(30f, 45f) :
                UnityEngine.Random.Range(135f, 150f);
        }
        else cubeYaw = UnityEngine.Random.Range(orientationYawMinDeg, orientationYawMaxDeg);
        cubeYaw += UnityEngine.Random.value < 0.5f ? 0f : 180f;
        initialObjectYawDeg = cubeYaw;
        cubeRigidbody.rotation = arenaRoot.rotation *
            Quaternion.Euler(0f, cubeYaw, 0f) * cubeInitialLocalRotation;
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
        // 33..34: orientación REAL del eje largo (+Z del prisma) en el marco de la arena.
        Vector3 prismForward = arenaRoot.InverseTransformDirection(cubeTransform.forward);
        float horizontalSquared = prismForward.x * prismForward.x + prismForward.z * prismForward.z;
        float yawRadians = !float.IsNaN(horizontalSquared) && !float.IsInfinity(horizontalSquared) && horizontalSquared > 1e-8f
            ? Mathf.Atan2(prismForward.x, prismForward.z) : 0f;
        sensor.AddObservation(Mathf.Sin(2f * yawRadians));
        sensor.AddObservation(Mathf.Cos(2f * yawRadians));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var continuous = actions.ContinuousActions;
        episodeSteps++;
        totalActionCount++;
        float rewardBefore = episodeReward;
        bool isDecision = decisionPending;
        episodeDurationSimSeconds = (float)(Time.fixedTimeAsDouble - episodeStartSimTime);
        if (settlingActionsRemaining > 0)
        {
            settlingActionsRemaining--;
            SetGripper(GripperOpen);
            if (!IsFinite(tcpTransform.position) || !IsFinite(cubeTransform.position))
            {
                Award(-1f, RewardKind.Failure);
                EndWithReason("invalid_robot_state", continuous, rewardBefore);
                return;
            }
            cubeInitialHeightLocal = arenaRoot.InverseTransformPoint(cubeTransform.position).y;
            ResetDistanceBaseline();
            Award(-StepCost, RewardKind.Time);
            LogDecisionIfNeeded(continuous, rewardBefore);
            return;
        }

        bool saturatedAction = false;
        for (int i = 0; i < 7; i++)
        {
            if (isDecision)
            {
                float magnitude = Mathf.Abs(Mathf.Clamp(continuous[i], -1f, 1f));
                jointActionSums[i] += magnitude;
                jointActionMagnitudeSum += magnitude;
                maximumAbsJointAction = Mathf.Max(maximumAbsJointAction, magnitude);
                if (magnitude >= 0.95f) saturatedAction = true;
            }
            ArticulationBody body = jointBodies[i];
            ArticulationDrive drive = body.xDrive;
            float delta = Mathf.Clamp(continuous[i], -1f, 1f) * jointSpeed * Time.fixedDeltaTime;
            drive.target = Mathf.Clamp(drive.target + delta, drive.lowerLimit, drive.upperLimit);
            body.xDrive = drive;
        }
        if (isDecision)
        {
            jointActionSamples++;
            if (saturatedAction) saturatedJointDecisions++;
            if (continuous[7] <= gripperCloseThreshold) closeActionDecisions++;
            if (continuous[7] >= gripperOpenThreshold) openActionDecisions++;
        }
        // La banda central conserva el target anterior: PPO sigue pudiendo abrir.
        bool closeGripper = previousGripperClosed;
        if (continuous[7] <= gripperCloseThreshold) closeGripper = true;
        else if (continuous[7] >= gripperOpenThreshold) closeGripper = false;
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
            Award(-1f, RewardKind.Failure);
            EndWithReason("invalid_robot_state", continuous, rewardBefore);
            return;
        }
        if (height < -0.10f)
        {
            Award(-1f, RewardKind.Failure);
            EndWithReason("cube_fell", continuous, rewardBefore);
            return;
        }
        Bounds table = tableCollider.bounds;
        Vector3 cubeWorld = cubeTransform.position;
        if (cubeWorld.x < table.min.x - 0.05f || cubeWorld.x > table.max.x + 0.05f ||
            cubeWorld.z < table.min.z - 0.05f || cubeWorld.z > table.max.z + 0.05f)
        {
            Award(-1f, RewardKind.Failure);
            EndWithReason("cube_out_of_bounds", continuous, rewardBefore);
            return;
        }

        float bestTargetBefore = minimumCubeTargetDistance;
        float bestHeightBefore = maximumCubeHeight;
        minimumTcpCubeDistance = Mathf.Min(minimumTcpCubeDistance, tcpDistance);
        Award(-StepCost, RewardKind.Time);
        if (currentJointLimitPenalty > 0f) Award(-currentJointLimitPenalty, RewardKind.JointLimit);
        if (currentJointVelocityPenalty > 0f) Award(-currentJointVelocityPenalty, RewardKind.Velocity);
        if (isDecision && decisionCount - lastManipulabilityDecision >=
            Mathf.Max(1, singularityCheckEveryNDecisions))
        {
            RecordManipulability(manipulabilityProbe.Measure(), true);
            lastManipulabilityDecision = decisionCount;
            lastManipulabilityStep = episodeSteps;
        }

        float verticalVelocity = arenaRoot.InverseTransformDirection(cubeRigidbody.linearVelocity).y;
        maximumCubeVerticalVelocity = Mathf.Max(maximumCubeVerticalVelocity, verticalVelocity);
        if (verticalVelocity > 0f)
        {
            positiveVerticalVelocitySum += verticalVelocity;
            positiveVerticalVelocitySamples++;
        }
        if (stepFirstLiftMotion < 0 && height - previousCubeHeight >= 0.001f)
            stepFirstLiftMotion = episodeSteps;
        if (height > bestHeightBefore + 0.00001f || maxHeightEventIndex < 0)
        {
            maximumCubeHeight = Mathf.Max(maximumCubeHeight, height);
            stepMaxHeight = episodeSteps;
            openingAtMaxHeight = GripperOpening();
            distanceAtMaxHeight = tcpDistance;
            CaptureEvent(ref maxHeightEventIndex, "max_height", tcpDistance, height, verticalVelocity);
            manipulabilityAtMaxHeight = episodeEvents[maxHeightEventIndex].manipulability;
        }

        if (!reachedCube)
        {
            // La diferencia telescópica no premia oscilaciones repetidas cerca del cubo.
            Award(2f * (previousTcpCubeDistance - tcpDistance), RewardKind.Reach);
            if (tcpDistance <= reachDistance)
            {
                reachedCube = true;
                stepReachedCube = episodeSteps;
                phase = Phase.Grasp;
                openingAtReach = GripperOpening();
                distanceAtReach = tcpDistance;
                heightAtReach = height;
                CaptureEvent(ref reachEventIndex, "reach", tcpDistance, height, verticalVelocity);
                manipulabilityAtReach = episodeEvents[reachEventIndex].manipulability;
                Award(0.35f, RewardKind.Reach);
            }
        }
        if (closeGripper && !previousGripperClosed && tcpDistance <= graspDistance)
        {
            graspAttempted = true;
            graspAttemptCount++;
            if (stepFirstGraspAttempt < 0) stepFirstGraspAttempt = episodeSteps;
        }
        // Solo se paga una mejora del mejor alineamiento durante una aproximación
        // con intento activo de cierre; el primer valor elegible fija la referencia.
        if (isDecision && reachedCube && closeGripper && !wasGrasped &&
            tcpDistance <= reachDistance && tcpDistance <= previousTcpCubeDistance)
        {
            float alignment = FingerToPrismShortAxisAlignment();
            if (IsFinite(alignment))
            {
                if (float.IsNaN(bestEligibleAlignment)) bestEligibleAlignment = alignment;
                else if (alignment > bestEligibleAlignment)
                {
                    Award(MaximumAlignmentReward * (alignment - bestEligibleAlignment), RewardKind.Alignment);
                    bestEligibleAlignment = alignment;
                }
            }
        }

        bool leftTouching = leftContact.TouchingCube;
        bool rightTouching = rightContact.TouchingCube;
        bool twoFingerContact = leftTouching && rightTouching;
        if (twoFingerContact) bilateralContactActions++;
        bool holding = closeGripper && twoFingerContact &&
                       GripperOpening() <= MaximumGraspOpening && tcpDistance <= graspDistance;
        if (isDecision)
        {
            if (leftTouching) leftContactDecisions++;
            if (rightTouching) rightContactDecisions++;
            if (leftTouching && !rightTouching) leftOnlyContactDecisions++;
            if (rightTouching && !leftTouching) rightOnlyContactDecisions++;
            if (twoFingerContact)
            {
                bilateralContactDecisions++;
                consecutiveBilateralContactDecisions++;
                longestBilateralContactDecisions = Mathf.Max(longestBilateralContactDecisions,
                    consecutiveBilateralContactDecisions);
                if (stepFirstBilateralContact < 0)
                {
                    stepFirstBilateralContact = episodeSteps;
                    CaptureEvent(ref bilateralEventIndex, "first_bilateral_contact", tcpDistance,
                        height, verticalVelocity);
                }
            }
            else consecutiveBilateralContactDecisions = 0;
        }
        consecutiveHoldingActions = holding ? consecutiveHoldingActions + 1 : 0;
        maxConsecutiveHoldingActions = Mathf.Max(maxConsecutiveHoldingActions, consecutiveHoldingActions);
        if (holding)
        {
            holdingActions++;
            maximumHeldCubeHeight = Mathf.Max(maximumHeldCubeHeight, height);
        }
        if (!holding) consecutiveHoldingDecisions = 0;
        else if (isDecision) consecutiveHoldingDecisions++;
        if (!wasGrasped && reachedCube && consecutiveHoldingDecisions >= SustainedGripDecisions)
        {
            wasGrasped = true;
            stepFirstGrasp = episodeSteps;
            phase = Phase.Lift;
            openingAtGrasp = GripperOpening();
            distanceAtGrasp = tcpDistance;
            heightAtGrasp = height;
            alignmentAtFirstGrasp = FingerToPrismShortAxisAlignment();
            CaptureEvent(ref graspEventIndex, "grasp_confirmed", tcpDistance, height, verticalVelocity);
            manipulabilityAtGrasp = episodeEvents[graspEventIndex].manipulability;
            rewardedLiftHighWater = height;
            retentionActive = true;
            Award(GraspBonus, RewardKind.Grasp);
        }
        if (wasGrasped)
        {
            if (isDecision && episodeSteps > stepFirstGrasp)
            {
                float gripperAction = Mathf.Clamp(continuous[7], -1f, 1f);
                postGraspActionSamples++;
                postGraspActionSum += gripperAction;
                postGraspMaxAction = Mathf.Max(postGraspMaxAction, gripperAction);
                postGraspMinAction = Mathf.Min(postGraspMinAction, gripperAction);
                if (gripperAction >= gripperOpenThreshold) postGraspOpenActionDecisions++;
                if (gripperAction <= gripperCloseThreshold) postGraspCloseActionDecisions++;
            }
            if (holding)
            {
                lossCandidateActions = 0;
                retentionActive = true;
                if (isDecision)
                {
                    retentionDecisions++;
                    consecutiveRetentionDecisions++;
                    longestRetentionDecisions = Mathf.Max(longestRetentionDecisions,
                        consecutiveRetentionDecisions);
                }
            }
            else
            {
                consecutiveRetentionDecisions = 0;
                if (retentionActive && !twoFingerContact && stepFirstPhysicalGraspLoss < 0)
                    stepFirstPhysicalGraspLoss = episodeSteps;
                if (retentionActive && ++lossCandidateActions >= GraspLossConfirmationActions)
                {
                    retentionActive = false;
                    graspLostCount++;
                    if (stepFirstGraspLoss < 0)
                    {
                        stepFirstGraspLoss = episodeSteps;
                        heightAtFirstGraspLoss = height;
                        openingAtFirstGraspLoss = GripperOpening();
                        distanceAtFirstGraspLoss = tcpDistance;
                        CaptureEvent(ref lossEventIndex, "first_grasp_loss", tcpDistance,
                            height, verticalVelocity);
                    }
                    if (penalizedGraspLosses < MaxPenalizedGraspLosses)
                    {
                        penalizedGraspLosses++;
                        Award(-GraspLossPenalty, RewardKind.GraspLoss);
                    }
                }
            }
        }
        if (wasGrasped && !wasLifted)
        {
            float delta = height - previousCubeHeight;
            if (holding)
            {
                float newHeldHeight = Mathf.Max(0f, height - rewardedLiftHighWater);
                if (newHeldHeight > 0f)
                {
                    float liftReward = LiftProgressScale * newHeldHeight;
                    liftProgressRewardTotal += liftReward;
                    Award(liftReward, RewardKind.Lift);
                    rewardedLiftHighWater = height;
                }
                if (delta < 0f)
                {
                    float dropPenalty = HeldDropScale * delta;
                    liftProgressRewardTotal += dropPenalty;
                    Award(dropPenalty, RewardKind.Lift);
                }
                AwardLiftMilestones(height);
            }
            if (holding && height >= liftGoal)
            {
                wasLifted = true;
                stepFirstLift = episodeSteps;
                phase = Phase.Transport;
                minimumCubeTargetDistance = targetDistance;
                CaptureEvent(ref liftEventIndex, "lift_success", tcpDistance, height, verticalVelocity);
                Award(2f, RewardKind.Lift);
                if (liftGoal < liftThreshold - 0.001f)
                {
                    Award(3f, RewardKind.Lift);
                    EndWithReason("curriculum_lift_success", continuous, rewardBefore);
                    return;
                }
            }
        }
        else if (!wasGrasped && twoFingerContact && closeGripper)
        {
            float delta = height - previousCubeHeight;
            float progress = delta > 0f
                ? Mathf.Min(delta, Mathf.Max(0f, height - bestHeightBefore)) : delta;
            float liftReward = PreGraspLiftScale * progress;
            liftProgressRewardTotal += liftReward;
            Award(liftReward, RewardKind.Lift);
        }

        if (wasLifted)
        {
            float delta = previousCubeTargetDistance - targetDistance;
            if (holding && height >= 0.06f && targetDistance > targetRadius)
                Award(3f * (delta > 0f ? Mathf.Min(delta,
                    Mathf.Max(0f, bestTargetBefore - targetDistance)) : delta), RewardKind.Transport);
            if (!enteredTarget && targetDistance <= targetRadius && height >= 0.06f)
            {
                enteredTarget = true;
                stepEnteredTarget = episodeSteps;
                phase = Phase.Place;
                Award(0.6f, RewardKind.Transport);
            }
            float releaseHeight = ReleaseHeightAboveTarget();
            bool alignedNow = holding && targetDistance <= targetRadius;
            currentPrePlaceRetentionValid = holding && retentionActive &&
                consecutiveRetentionDecisions >= SustainedGripDecisions &&
                verticalVelocity > MinimumControlledDescentVelocity &&
                !releaseCandidateActive;
            bool prePlaceEligible = alignedNow && currentPrePlaceRetentionValid;
            if (holding && stepRelease >= 0) controlledReleaseForCurrentPlacement = false;
            if (currentPrePlaceRetentionValid)
                minimumReleaseHeightWhileHeld = Mathf.Min(minimumReleaseHeightWhileHeld, releaseHeight);
            if (alignedNow && !targetAligned)
            {
                targetAligned = true;
                stepTargetAligned = episodeSteps;
                retentionDecisionsAtTargetAlignment = consecutiveRetentionDecisions;
                horizontalAtAlignment = targetDistance;
                releaseHeightAtAlignment = releaseHeight;
                CapturePlaceEvent(ref targetAlignedEventIndex, "target_aligned", tcpDistance,
                    height, verticalVelocity, targetDistance, releaseHeight, continuous[7]);
            }
            if (prePlaceEligible)
            {
                if (!prePlaceEligibleReached)
                {
                    prePlaceEligibleReached = true;
                    stepPrePlaceEligible = episodeSteps;
                    retentionDecisionsAtPrePlaceEligible = consecutiveRetentionDecisions;
                    releaseHeightAtPrePlaceEligible = releaseHeight;
                    verticalVelocityAtPrePlaceEligible = verticalVelocity;
                    CapturePlaceEvent(ref prePlaceEligibleEventIndex, "preplace_eligible", tcpDistance,
                        height, verticalVelocity, targetDistance, releaseHeight, continuous[7]);
                }
                float error = Mathf.Max(0f, releaseHeight);
                if (previousPrePlaceEligible && error < previousReleaseHeightError &&
                    error < bestReleaseHeightError)
                    Award(PrePlaceProgressScale * Mathf.Min(previousReleaseHeightError - error,
                        bestReleaseHeightError - error), RewardKind.PrePlace);
                bestReleaseHeightError = Mathf.Min(bestReleaseHeightError, error);
                previousReleaseHeightError = error;
                bestPrePlaceHeight = Mathf.Min(bestPrePlaceHeight, releaseHeight);
                for (int i = 0; i < PrePlaceHeights.Length; i++)
                {
                    if (reachedPrePlaceMilestone[i] || releaseHeight > PrePlaceHeights[i]) continue;
                    reachedPrePlaceMilestone[i] = true;
                    stepPrePlaceMilestone[i] = episodeSteps;
                    Award(PrePlaceBonuses[i], RewardKind.PrePlaceMilestones);
                    CapturePlaceEvent(ref prePlaceEventIndices[i], PrePlaceEventNames[i],
                        tcpDistance, height, verticalVelocity, targetDistance,
                        releaseHeight, continuous[7]);
                }
            }
            previousPrePlaceEligible = prePlaceEligible;
            bool readyNow = prePlaceEligible && targetDistance <= ReleaseReadyRadius &&
                releaseHeight >= -placementHeightTolerance &&
                releaseHeight <= MaximumControlledReleaseHeight;
            if (readyNow && !releaseReadyReached)
            {
                releaseReadyReached = true;
                stepReleaseReady = episodeSteps;
                horizontalAtReady = targetDistance;
                CapturePlaceEvent(ref releaseReadyEventIndex, "release_ready", tcpDistance,
                    height, verticalVelocity, targetDistance, releaseHeight, continuous[7]);
                Award(ReleaseReadyBonus, RewardKind.ReleaseReady);
            }
            // La orden de apertura inicia un candidato; el evento se confirma al perder contacto físico.
            if (previousHolding && previousGripperClosed && !closeGripper && twoFingerContact &&
                targetDistance <= targetRadius)
            {
                releaseCandidateActive = true;
                releaseCandidateContinuouslyHeld = previousRobustPrePlaceHolding;
                releaseCandidateReady = releaseReadyReached &&
                    targetDistance <= ReleaseReadyRadius &&
                    releaseHeight >= -placementHeightTolerance &&
                    releaseHeight <= MaximumControlledReleaseHeight;
                releaseCandidateStartStep = episodeSteps;
                releaseCommandAction = continuous[7];
                releaseCandidateCommandHeight = releaseHeight;
                releaseCandidateCommandVelocity = verticalVelocity;
                releaseCandidateCommandHorizontal = targetDistance;
                releaseCandidateCommandOpening = GripperOpening();
                releaseCandidateCommandCubeHeight = height;
                releaseCandidateCommandTcpDistance = tcpDistance;
                releaseLossCandidateActions = 0;
                releaseLossConfirmed = false;
            }
            if (releaseCandidateActive)
            {
                if (closeGripper || episodeSteps - releaseCandidateStartStep > ReleaseConfirmationActions)
                {
                    releaseCandidateActive = false;
                    releaseLossCandidateActions = 0;
                }
                else
                {
                    if (twoFingerContact)
                    {
                        releaseLossCandidateActions = 0;
                        releaseLossConfirmed = false;
                    }
                    else if (!releaseLossConfirmed)
                    {
                        if (releaseLossCandidateActions++ == 0)
                        {
                            releaseLossCandidateStep = episodeSteps;
                            releaseLossCandidateHeight = releaseHeight;
                            releaseLossCandidateVelocity = verticalVelocity;
                            releaseLossCandidateHorizontal = targetDistance;
                            releaseLossCandidateOpening = GripperOpening();
                            releaseLossCandidateCubeHeight = height;
                            releaseLossCandidateTcpDistance = tcpDistance;
                        }
                        if (releaseLossCandidateActions >= PhysicalReleaseLossConfirmationActions)
                        {
                            releaseLossConfirmed = true;
                            if (stepGraspLossForRelease < 0)
                            {
                                stepReleaseCommand = releaseCandidateStartStep;
                                releaseCommandHeight = releaseCandidateCommandHeight;
                                releaseCommandVerticalVelocity = releaseCandidateCommandVelocity;
                                CaptureReleaseSnapshot(ref releaseCommandEventIndex, "release_command",
                                    stepReleaseCommand, releaseCandidateCommandTcpDistance,
                                    releaseCandidateCommandCubeHeight, releaseCandidateCommandVelocity,
                                    releaseCandidateCommandHorizontal, releaseCandidateCommandHeight,
                                    releaseCommandAction, releaseCandidateCommandOpening,
                                    releaseCandidateContinuouslyHeld);
                                stepGraspLossForRelease = releaseLossCandidateStep;
                                graspLossHeightForRelease = releaseLossCandidateHeight;
                                graspLossVerticalVelocity = releaseLossCandidateVelocity;
                                releaseWasContinuouslyHeldBeforeLoss =
                                    releaseCandidateContinuouslyHeld;
                                CaptureReleaseSnapshot(ref confirmedGraspLossEventIndex,
                                    "confirmed_grasp_loss", stepGraspLossForRelease,
                                    releaseLossCandidateTcpDistance, releaseLossCandidateCubeHeight,
                                    releaseLossCandidateVelocity, releaseLossCandidateHorizontal,
                                    releaseLossCandidateHeight, releaseCommandAction,
                                    releaseLossCandidateOpening, false);
                            }
                        }
                    }
                    if (releaseLossConfirmed && !twoFingerContact && GripperOpening() >= 0.2f)
                    {
                        controlledReleaseForCurrentPlacement = false;
                        if (stepRelease < 0)
                        {
                            stepRelease = stepReleaseConfirmed = episodeSteps;
                            horizontalAtRelease = targetDistance;
                            releaseHeightAtRelease = releaseConfirmedHeight = releaseHeight;
                            verticalVelocityAtRelease = verticalVelocity;
                            gripperActionAtRelease = releaseCommandAction;
                            gripperOpeningAtRelease = GripperOpening();
                            CapturePlaceEvent(ref releaseEventIndex, "release", tcpDistance,
                                height, verticalVelocity, targetDistance, releaseHeight,
                                releaseCommandAction);
                        }
                        bool controlled = releaseCandidateReady &&
                            releaseCandidateContinuouslyHeld &&
                            releaseLossCandidateHorizontal <= ReleaseReadyRadius &&
                            releaseLossCandidateHeight >= -placementHeightTolerance &&
                            releaseLossCandidateHeight <= MaximumControlledReleaseHeight;
                        if (controlled)
                        {
                            controlledReleaseForCurrentPlacement = true;
                            if (!releasedCorrectly)
                            {
                                releasedCorrectly = true;
                                Award(ControlledReleaseBonus, RewardKind.ControlledRelease);
                            }
                        }
                        else if ((releaseCandidateCommandHeight > MaximumControlledReleaseHeight ||
                                  releaseLossCandidateHeight > MaximumControlledReleaseHeight) &&
                                 releaseCandidateCommandHorizontal <= targetRadius && !releasedTooHigh)
                        {
                            releasedTooHigh = true;
                            Award(-HighReleasePenalty, RewardKind.HighRelease);
                        }
                        releaseCandidateActive = false;
                    }
                }
            }
            if (enteredTarget && !placeHeightRewardGiven && targetDistance <= targetRadius &&
                Mathf.Abs(CubeLocalY() - cubeRestingHeightLocal) <= placementHeightTolerance)
            {
                placeHeightRewardGiven = true;
                Award(0.25f, RewardKind.Place);
            }
            if (!releasedInTarget && V3PlacementCriteria.CanReleaseInTarget(
                enteredTarget, previousGripperClosed, !closeGripper, targetDistance,
                targetRadius, CubeLocalY() - cubeRestingHeightLocal, linearSpeed, angularSpeed,
                placementHeightTolerance, maxPlacementLinearVelocity, maxPlacementAngularVelocity))
            {
                releasedInTarget = true;
                Award(0.5f, RewardKind.Place);
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
                    Award(0.02f, RewardKind.Place);
                }
            }
            if (stableTargetDecisions >= stablePlacementDecisions)
            {
                placedSuccessfully = true;
                controlledPlaceSuccess = releaseReadyReached &&
                    controlledReleaseForCurrentPlacement;
                stepSuccess = episodeSteps;
                Award(PhysicalPlaceBonus, RewardKind.Place);
                if (controlledPlaceSuccess)
                    Award(ControlledPlaceExtraBonus, RewardKind.ControlledPlace);
                CapturePlaceEvent(ref placeEventIndex, "place", tcpDistance, height,
                    verticalVelocity, targetDistance, releaseHeight, continuous[7]);
                if (controlledPlaceSuccess)
                    CapturePlaceEvent(ref controlledPlaceEventIndex, "controlled_place",
                        tcpDistance, height, verticalVelocity, targetDistance,
                        releaseHeight, continuous[7]);
                EndWithReason("place_success", continuous, rewardBefore);
                return;
            }
        }
        previousTcpCubeDistance = tcpDistance;
        previousCubeTargetDistance = targetDistance;
        previousCubeHeight = height;
        minimumCubeTargetDistance = Mathf.Min(minimumCubeTargetDistance, targetDistance);
        previousRobustPrePlaceHolding = currentPrePlaceRetentionValid;
        previousHolding = holding;
        previousGripperClosed = closeGripper;
        // Agent.StepCount aumenta antes de esta acción; EndEpisode evita el reset automático de MaxStep.
        if (MaxStep > 0 && StepCount >= MaxStep)
        {
            float formerTimeCost = StepCost * PreviousTimeoutSeconds / Time.fixedDeltaTime;
            float shortTimeCost = StepCost * MaxStep;
            Award(-Mathf.Max(0f, formerTimeCost - shortTimeCost), RewardKind.Timeout);
            EndWithReason("timeout", continuous, rewardBefore);
            return;
        }
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
        episodeDurationSimSeconds = Mathf.Max(episodeDurationSimSeconds,
            Mathf.Max(0f, (float)(Time.fixedTimeAsDouble - episodeStartSimTime)));
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
            stats.Add("V3/ReachRate", reachedCube ? 1f : 0f);
            stats.Add("V3/GraspConfirmedRate", wasGrasped ? 1f : 0f);
            stats.Add("V3/LiftRate", wasLifted ? 1f : 0f);
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
            if (!float.IsPositiveInfinity(minimumJointLimitMarginNormalized))
                stats.Add("V3/MinimumJointLimitMargin", minimumJointLimitMarginNormalized);
            stats.Add("V3/RewardReach", rewardParts[(int)RewardKind.Reach]);
            stats.Add("V3/RewardGrasp", rewardParts[(int)RewardKind.Grasp]);
            stats.Add("V3/RewardLift", rewardParts[(int)RewardKind.Lift]);
            stats.Add("V3/RewardRegularization", rewardParts[(int)RewardKind.Singularity] +
                rewardParts[(int)RewardKind.JointLimit] + rewardParts[(int)RewardKind.Velocity]);
            stats.Add("V3/BilateralContactRate", decisionCount > 0
                ? (float)bilateralContactDecisions / decisionCount : 0f);
            stats.Add("V3/GraspRetention", wasGrasped && decisionCount > 0
                ? (float)retentionDecisions / decisionCount : 0f);
            stats.Add("V3/MeanGraspRetention", longestRetentionDecisions);
            stats.Add("V3/GraspLossRate", graspLostCount > 0 ? 1f : 0f);
            stats.Add("V3/Lift1cmRate", reachedLiftMilestone[0] ? 1f : 0f);
            stats.Add("V3/Lift2cmRate", reachedLiftMilestone[1] ? 1f : 0f);
            stats.Add("V3/Lift3cmRate", reachedLiftMilestone[2] ? 1f : 0f);
            stats.Add("V3/Lift4cmRate", reachedLiftMilestone[3] ? 1f : 0f);
            stats.Add("V3/Lift5cmRate", reachedLiftMilestone[4] ? 1f : 0f);
            stats.Add("V3/PrePlace30cmRate", reachedPrePlaceMilestone[0] ? 1f : 0f);
            stats.Add("V3/PrePlace20cmRate", reachedPrePlaceMilestone[2] ? 1f : 0f);
            stats.Add("V3/PrePlace10cmRate", reachedPrePlaceMilestone[4] ? 1f : 0f);
            stats.Add("V3/PrePlace5cmRate", reachedPrePlaceMilestone[5] ? 1f : 0f);
            stats.Add("V3/ReleaseReadyRate", releaseReadyReached ? 1f : 0f);
            stats.Add("V3/ControlledReleaseRate", releasedCorrectly ? 1f : 0f);
            stats.Add("V3/HighReleaseRate", releasedTooHigh ? 1f : 0f);
            if (stepRelease >= 0) stats.Add("V3/MeanReleaseHeight", releaseHeightAtRelease);
            stats.Add("V3/PlaceSuccessRate", placedSuccessfully ? 1f : 0f);
            stats.Add("V3/ControlledPlaceSuccessRate", controlledPlaceSuccess ? 1f : 0f);
            stats.Add("V3/MaximumJointVelocity", maximumJointVelocityDegPerSec);
            stats.Add("V3/MinimumCubeTargetDistance", minimumCubeTargetDistance);
            stats.Add("V3/StepsToReach", stepReachedCube > 0 ? stepReachedCube : 0f);
            stats.Add("V3/StepsToLift", stepFirstLift > 0 ? stepFirstLift : 0f);
            stats.Add("V3/StepsToPlace", stepSuccess > 0 ? stepSuccess : 0f);
        }
        var meanActions = new float[7];
        for (int i = 0; i < 7; i++)
            meanActions[i] = jointActionSamples > 0 ?
                (float)(jointActionSums[i] / jointActionSamples) : 0f;
        float accountedReward = 0f;
        for (int i = 0; i < rewardParts.Length; i++) accountedReward += rewardParts[i];
        TrainingCsvLoggerV3.WriteEpisode(new TrainingCsvLoggerV3.Episode {
            arenaId = arenaId, arenaEpisode = arenaEpisode, steps = episodeSteps,
            reward = episodeReward, success = placedSuccessfully, terminalReason = reason,
            robotBaseOffsetX = robotBaseOffsetX, robotBaseOffsetZ = robotBaseOffsetZ,
            robotBaseLocalPosition = robotBaseEpisodeLocalPosition,
            cubeSpawnLocalPosition = cubeInitialLocalPosition,
            initialObjectYawDeg = initialObjectYawDeg,
            orientationCurriculumLevel = orientationCurriculumLevel,
            orientationYawMinDeg = orientationYawMinDeg,
            orientationYawMaxDeg = orientationYawMaxDeg,
            orientationSamplingMode = orientationSamplingMode,
            orientationFocusProbability = orientationFocusProbability,
            alignmentAtFirstGrasp = alignmentAtFirstGrasp,
            rewardAlignment = rewardParts[(int)RewardKind.Alignment],
            graspToFirstPhysicalLossSteps = stepFirstGrasp >= 0 && stepFirstPhysicalGraspLoss >= 0
                ? stepFirstPhysicalGraspLoss - stepFirstGrasp : -1,
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
            controlledPlaceSuccess = controlledPlaceSuccess,
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
            jointLimitWarningCount = jointLimitWarningCount,
            stepFirstGraspAttempt = stepFirstGraspAttempt,
            stepFirstBilateralContact = stepFirstBilateralContact,
            stepFirstLiftMotion = stepFirstLiftMotion, stepMaxHeight = stepMaxHeight,
            graspAttemptCount = graspAttemptCount,
            bilateralContactDecisions = bilateralContactDecisions,
            longestBilateralContactDecisions = longestBilateralContactDecisions,
            leftContactDecisions = leftContactDecisions,
            rightContactDecisions = rightContactDecisions,
            leftOnlyContactDecisions = leftOnlyContactDecisions,
            rightOnlyContactDecisions = rightOnlyContactDecisions,
            retentionDecisions = retentionDecisions,
            longestRetentionDecisions = longestRetentionDecisions,
            graspLostCount = graspLostCount,
            openingAtReach = openingAtReach, openingAtGrasp = openingAtGrasp,
            openingAtMaxHeight = openingAtMaxHeight,
            distanceAtReach = distanceAtReach, distanceAtGrasp = distanceAtGrasp,
            distanceAtMaxHeight = distanceAtMaxHeight,
            heightAtReach = heightAtReach, heightAtGrasp = heightAtGrasp,
            netCubeHeightGain = IsFinite(heightAtGrasp) ? finalHeight - heightAtGrasp : float.NaN,
            maximumCubeVerticalVelocity = maximumCubeVerticalVelocity,
            meanPositiveCubeVerticalVelocity = positiveVerticalVelocitySamples > 0
                ? positiveVerticalVelocitySum / positiveVerticalVelocitySamples : 0f,
            liftProgressRewardTotal = liftProgressRewardTotal,
            minimumJointLimitMarginNormalized = float.IsPositiveInfinity(minimumJointLimitMarginNormalized)
                ? float.NaN : minimumJointLimitMarginNormalized,
            jointClosestToLimit = jointClosestToLimit,
            jointMinimumMargins = (float[])jointMinimumMargins.Clone(),
            jointMaximumVelocities = (float[])jointMaximumVelocities.Clone(),
            meanAbsActions = meanActions,
            meanAbsJointAction = jointActionSamples > 0
                ? (float)(jointActionMagnitudeSum / (7 * jointActionSamples)) : 0f,
            maxAbsJointAction = maximumAbsJointAction,
            jointActionSaturationRate = jointActionSamples > 0
                ? (float)saturatedJointDecisions / jointActionSamples : 0f,
            gripperCloseActionRate = jointActionSamples > 0
                ? (float)closeActionDecisions / jointActionSamples : 0f,
            gripperOpenActionRate = jointActionSamples > 0
                ? (float)openActionDecisions / jointActionSamples : 0f,
            manipulabilityAtReach = manipulabilityAtReach,
            manipulabilityAtGrasp = manipulabilityAtGrasp,
            manipulabilityAtMaxHeight = manipulabilityAtMaxHeight,
            rewardReach = rewardParts[(int)RewardKind.Reach],
            rewardGrasp = rewardParts[(int)RewardKind.Grasp],
            rewardLift = rewardParts[(int)RewardKind.Lift],
            rewardTransport = rewardParts[(int)RewardKind.Transport],
            rewardPlace = rewardParts[(int)RewardKind.Place] +
                rewardParts[(int)RewardKind.ControlledPlace],
            rewardPhysicalPlace = rewardParts[(int)RewardKind.Place],
            rewardControlledPlace = rewardParts[(int)RewardKind.ControlledPlace],
            rewardSingularity = rewardParts[(int)RewardKind.Singularity],
            rewardJointLimit = rewardParts[(int)RewardKind.JointLimit],
            rewardVelocity = rewardParts[(int)RewardKind.Velocity],
            rewardTime = rewardParts[(int)RewardKind.Time],
            rewardFailure = rewardParts[(int)RewardKind.Failure],
            rewardTimeout = rewardParts[(int)RewardKind.Timeout],
            rewardPrePlace = rewardParts[(int)RewardKind.PrePlace],
            rewardReleaseReady = rewardParts[(int)RewardKind.ReleaseReady],
            rewardControlledRelease = rewardParts[(int)RewardKind.ControlledRelease],
            rewardHighRelease = rewardParts[(int)RewardKind.HighRelease],
            rewardPrePlaceMilestones = rewardParts[(int)RewardKind.PrePlaceMilestones],
            rewardPositive = rewardPositiveTotal, rewardPenalty = rewardPenaltyTotal,
            rewardAccounted = accountedReward,
            curriculumLesson = liftGoal < 0.075f ? "Lift05cm" :
                liftGoal < 0.15f ? "Lift10cm" :
                liftGoal < liftThreshold - 0.001f ? "Lift20cm" : "PickPlace30cm",
            events = episodeEvents, eventCount = eventCount,
            postGraspOpenActionRate = postGraspActionSamples > 0
                ? (float)postGraspOpenActionDecisions / postGraspActionSamples : float.NaN,
            postGraspCloseActionRate = postGraspActionSamples > 0
                ? (float)postGraspCloseActionDecisions / postGraspActionSamples : float.NaN,
            meanGripperActionAfterGrasp = postGraspActionSamples > 0
                ? (float)(postGraspActionSum / postGraspActionSamples) : float.NaN,
            maxGripperActionAfterGrasp = postGraspActionSamples > 0 ? postGraspMaxAction : float.NaN,
            minGripperActionAfterGrasp = postGraspActionSamples > 0 ? postGraspMinAction : float.NaN,
            stepFirstGraspLoss = stepFirstGraspLoss,
            heightAtFirstGraspLoss = heightAtFirstGraspLoss,
            openingAtFirstGraspLoss = openingAtFirstGraspLoss,
            distanceAtFirstGraspLoss = distanceAtFirstGraspLoss,
            graspLossesAfterConfirmation = graspLostCount,
            reachedLiftMilestone = (bool[])reachedLiftMilestone.Clone(),
            stepLiftMilestone = (int[])stepLiftMilestone.Clone(),
            reachedPrePlaceMilestone = (bool[])reachedPrePlaceMilestone.Clone(),
            stepPrePlaceMilestone = (int[])stepPrePlaceMilestone.Clone(),
            rewardGraspLoss = rewardParts[(int)RewardKind.GraspLoss],
            episodeDurationSimSeconds = episodeDurationSimSeconds,
            timeoutLimitSeconds = MaxStep * Time.fixedDeltaTime,
            episodeMaxStep = MaxStep,
            stepTargetAligned = stepTargetAligned, stepReleaseReady = stepReleaseReady,
            stepPrePlaceEligible = stepPrePlaceEligible,
            prePlaceEligibleReached = prePlaceEligibleReached,
            retentionDecisionsAtTargetAlignment = retentionDecisionsAtTargetAlignment,
            retentionDecisionsAtPrePlaceEligible = retentionDecisionsAtPrePlaceEligible,
            releaseHeightAtPrePlaceEligible = releaseHeightAtPrePlaceEligible,
            verticalVelocityAtPrePlaceEligible = verticalVelocityAtPrePlaceEligible,
            stepRelease = stepRelease, targetAligned = targetAligned,
            releaseReadyReached = releaseReadyReached, releasedCorrectly = releasedCorrectly,
            releasedTooHigh = releasedTooHigh, horizontalAtAlignment = horizontalAtAlignment,
            horizontalAtReady = horizontalAtReady, horizontalAtRelease = horizontalAtRelease,
            releaseHeightAtAlignment = releaseHeightAtAlignment,
            releaseHeightAtRelease = releaseHeightAtRelease,
            verticalVelocityAtRelease = verticalVelocityAtRelease,
            gripperActionAtRelease = gripperActionAtRelease,
            gripperOpeningAtRelease = gripperOpeningAtRelease,
            stepReleaseCommand = stepReleaseCommand,
            stepGraspLossForRelease = stepGraspLossForRelease,
            stepReleaseConfirmed = stepReleaseConfirmed,
            releaseCommandHeight = releaseCommandHeight,
            graspLossHeightForRelease = graspLossHeightForRelease,
            releaseConfirmedHeight = releaseConfirmedHeight,
            releaseCommandVerticalVelocity = releaseCommandVerticalVelocity,
            graspLossVerticalVelocity = graspLossVerticalVelocity,
            releaseWasContinuouslyHeldBeforeLoss = releaseWasContinuouslyHeldBeforeLoss,
            minimumReleaseHeightWhileHeld = float.IsPositiveInfinity(minimumReleaseHeightWhileHeld)
                ? float.NaN : minimumReleaseHeightWhileHeld,
            bestPrePlaceHeight = float.IsPositiveInfinity(bestPrePlaceHeight)
                ? float.NaN : bestPrePlaceHeight
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

    private float FingerToPrismShortAxisAlignment()
    {
        Vector3 fingerAxis = arenaRoot.InverseTransformDirection(
            rightFinger.transform.position - leftFinger.transform.position);
        Vector3 prismShortAxis = arenaRoot.InverseTransformDirection(cubeTransform.right);
        fingerAxis.y = prismShortAxis.y = 0f;
        if (!IsFinite(fingerAxis) || !IsFinite(prismShortAxis) ||
            fingerAxis.sqrMagnitude < 1e-8f || prismShortAxis.sqrMagnitude < 1e-8f)
            return float.NaN;
        return Mathf.Clamp01(Mathf.Abs(Vector3.Dot(fingerAxis.normalized, prismShortAxis.normalized)));
    }
    private float CubeHeight() { return CubeLocalY() - cubeInitialHeightLocal; }
    private float CubeTargetDistance()
    {
        Vector3 cube = arenaRoot.InverseTransformPoint(cubeTransform.position);
        Vector3 target = arenaRoot.InverseTransformPoint(placeTarget.position);
        return Vector2.Distance(new Vector2(cube.x, cube.z), new Vector2(target.x, target.z));
    }

    private float ReleaseHeightAboveTarget()
    {
        // Centro esperado al apoyar el cubo sobre la superficie marcada por el target.
        float surfaceY = targetCollider != null ? targetCollider.bounds.max.y : tableCollider.bounds.max.y;
        Vector3 supportedCenter = cubeTransform.position;
        supportedCenter.y = surfaceY + cubeCollider.bounds.extents.y;
        return CubeLocalY() - arenaRoot.InverseTransformPoint(supportedCenter).y;
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
        for (int i = 0; i < jointBodies.Length; i++)
        {
            ArticulationBody body = jointBodies[i];
            float angle = body.jointPosition[0] * Mathf.Rad2Deg;
            float velocity = Mathf.Abs(body.jointVelocity[0] * Mathf.Rad2Deg);
            ArticulationDrive drive = body.xDrive;
            if (!IsFinite(angle) || !IsFinite(velocity) ||
                angle < drive.lowerLimit - 10f || angle > drive.upperLimit + 10f)
                return false;
            float marginDeg = Mathf.Min(angle - drive.lowerLimit, drive.upperLimit - angle);
            actionMargin = Mathf.Min(actionMargin, marginDeg);
            float range = Mathf.Max(1f, drive.upperLimit - drive.lowerLimit);
            float margin = Mathf.Clamp(marginDeg / range, 0f, 0.5f);
            jointMinimumMargins[i] = Mathf.Min(jointMinimumMargins[i], margin);
            if (margin < minimumJointLimitMarginNormalized)
            {
                minimumJointLimitMarginNormalized = margin;
                jointClosestToLimit = i + 1;
            }
            normalizedMargin = Mathf.Min(normalizedMargin, margin);
            actionMaxVelocity = Mathf.Max(actionMaxVelocity, velocity);
            jointMaximumVelocities[i] = Mathf.Max(jointMaximumVelocities[i], velocity);
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
        Award(-singularityPenaltyWeight * severity, RewardKind.Singularity);
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

    private void Award(float amount, RewardKind kind)
    {
        AddReward(amount);
        episodeReward += amount;
        rewardParts[(int)kind] += amount;
        if (amount >= 0f) rewardPositiveTotal += amount;
        else rewardPenaltyTotal += amount;
    }

    private void AwardLiftMilestones(float height)
    {
        for (int i = 0; i < reachedLiftMilestone.Length; i++)
        {
            if (reachedLiftMilestone[i] || height < 0.01f * (i + 1)) continue;
            reachedLiftMilestone[i] = true;
            stepLiftMilestone[i] = episodeSteps;
            // El quinto hito usa el bonus de éxito de Lift existente.
            if (i < 4) Award(LiftMilestoneBonus, RewardKind.Lift);
        }
    }

    private void CaptureEvent(ref int index, string name, float distance, float height,
        float verticalVelocity)
    {
        if (index < 0)
        {
            if (eventCount >= episodeEvents.Length) return;
            index = eventCount++;
        }
        float[] angles = eventJointAngles[index];
        float minimumMargin = 0.5f;
        int closestJoint = 0;
        for (int i = 0; i < 7; i++)
        {
            ArticulationBody body = jointBodies[i];
            float angle = body.jointPosition[0] * Mathf.Rad2Deg;
            angles[i] = angle;
            ArticulationDrive drive = body.xDrive;
            float margin = Mathf.Clamp(Mathf.Min(angle - drive.lowerLimit,
                drive.upperLimit - angle) /
                Mathf.Max(1f, drive.upperLimit - drive.lowerLimit), 0f, 0.5f);
            if (margin < minimumMargin)
            {
                minimumMargin = margin;
                closestJoint = i + 1;
            }
        }
        float eventManipulability = lastManipulabilityStep == episodeSteps
            ? finalManipulability : manipulabilityProbe.Measure();
        episodeEvents[index] = new TrainingCsvLoggerV3.EpisodeEvent {
            timestampUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            eventName = name, episodeStep = episodeSteps, jointDegrees = angles,
            manipulability = eventManipulability, minimumJointMargin = minimumMargin,
            jointClosestToLimit = closestJoint, tcpCubeDistance = distance,
            cubeHeight = height, cubeVerticalVelocity = verticalVelocity,
            gripperOpening = GripperOpening(), horizontalTargetDistance = float.NaN,
            releaseHeightAboveTarget = float.NaN, gripperAction = float.NaN,
            retentionState = currentPrePlaceRetentionValid,
            retentionStreakDecisions = consecutiveRetentionDecisions
        };
    }

    private void CapturePlaceEvent(ref int index, string name, float distance, float height,
        float verticalVelocity, float horizontalDistance, float releaseHeight, float gripperAction)
    {
        CaptureEvent(ref index, name, distance, height, verticalVelocity);
        if (index < 0) return;
        TrainingCsvLoggerV3.EpisodeEvent captured = episodeEvents[index];
        captured.horizontalTargetDistance = horizontalDistance;
        captured.releaseHeightAboveTarget = releaseHeight;
        captured.gripperAction = gripperAction;
        episodeEvents[index] = captured;
    }

    private void CaptureReleaseSnapshot(ref int index, string name, int step, float distance,
        float height, float verticalVelocity, float horizontalDistance, float releaseHeight,
        float gripperAction, float gripperOpening, bool retentionState)
    {
        CapturePlaceEvent(ref index, name, distance, height, verticalVelocity,
            horizontalDistance, releaseHeight, gripperAction);
        if (index < 0) return;
        TrainingCsvLoggerV3.EpisodeEvent captured = episodeEvents[index];
        captured.episodeStep = step;
        captured.gripperOpening = gripperOpening;
        captured.retentionState = retentionState;
        captured.retentionStreakDecisions = -1;
        // La postura no se guardó en el instante original: no atribuirle la postura posterior.
        for (int i = 0; i < captured.jointDegrees.Length; i++)
            captured.jointDegrees[i] = float.NaN;
        captured.manipulability = captured.minimumJointMargin = float.NaN;
        captured.jointClosestToLimit = -1;
        episodeEvents[index] = captured;
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
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Un solo escritor con búfer para las arenas; se usa desde el hilo principal.
public static class TrainingCsvLoggerV3
{
    public struct Episode
    {
        public int arenaId, arenaEpisode, steps;
        public float reward;
        public bool success;
        public string terminalReason;
        public float robotBaseOffsetX, robotBaseOffsetZ;
        public Vector3 robotBaseLocalPosition, cubeSpawnLocalPosition, targetLocalPosition;
        public float initialTcpCubeDistance, minimumTcpCubeDistance, finalTcpCubeDistance;
        public float cubeInitialHeight, maximumCubeHeight, finalCubeHeight;
        public float targetOffsetX, targetOffsetZ;
        public float initialCubeTargetDistance, minimumCubeTargetDistance, finalCubeTargetDistance;
        public bool reachedCube, graspAttempted, wasGrasped, wasLifted, enteredTarget;
        public bool releasedInTarget, placedSuccessfully;
        public int gripperCloseAttempts, gripperOpenAttempts;
        public int stepReachedCube, stepFirstGrasp, stepFirstLift, stepEnteredTarget, stepSuccess;
        public int stableTargetDecisions;
        public float finalCubeLinearSpeed, finalCubeAngularSpeed;
        public float liftGoal, maximumHeldCubeHeight, minimumJointLimitMarginDeg;
        public int bilateralContactActions, holdingActions, maxConsecutiveHoldingActions, nearJointLimitActions;
        public float minimumManipulability, meanManipulability, finalManipulability;
        public float minimumJointLimitMargin, maximumJointVelocity;
        public int singularityWarningCount, jointLimitWarningCount;
    }

    public struct Decision
    {
        public int arenaId, arenaEpisode, episodeStep;
        public string phase;
        public float tcpCubeDistance, cubeTargetDistance, cubeHeight;
        public float instantReward, cumulativeReward, gripperOpening;
        public bool wasLifted, insideTarget;
        public float robotOffsetX, robotOffsetZ;
        public float[] actions;
    }

    private const string EpisodeHeader =
        "timestamp_utc,session_id,run_name,arena_id,arena_episode,global_episode,episode_steps,cumulative_reward,success,terminal_reason,number_of_arenas,time_scale," +
        "robot_base_offset_x_m,robot_base_offset_z_m,robot_base_local_x_m,robot_base_local_y_m,robot_base_local_z_m," +
        "cube_spawn_local_x_m,cube_spawn_local_y_m,cube_spawn_local_z_m,target_local_x_m,target_local_y_m,target_local_z_m," +
        "initial_tcp_cube_distance_m,minimum_tcp_cube_distance_m,final_tcp_cube_distance_m," +
        "cube_initial_height_m,maximum_cube_height_m,final_cube_height_m,target_offset_x_m,target_offset_z_m," +
        "initial_cube_target_distance_m,minimum_cube_target_distance_m,final_cube_target_distance_m," +
        "reached_cube,grasp_attempted,was_grasped,was_lifted,entered_target,released_in_target,placed_successfully," +
        "gripper_close_attempts,gripper_open_attempts,step_reached_cube,step_first_grasp,step_first_lift,step_entered_target,step_success," +
        "stable_target_decisions,final_cube_linear_speed,final_cube_angular_speed," +
        "lift_goal_m,bilateral_contact_actions,holding_actions,max_consecutive_holding_actions," +
        "maximum_held_cube_height_m,near_joint_limit_actions,minimum_joint_limit_margin_deg," +
        "minimum_manipulability,mean_manipulability,final_manipulability," +
        "minimum_joint_limit_margin,maximum_joint_velocity," +
        "singularity_warning_count,joint_limit_warning_count";
    private const string SummaryHeader =
        "timestamp_utc,session_id,run_name,global_episode,episodes_in_window,mean_reward,reach_rate,grasp_rate,lift_rate,target_entry_rate,place_success_rate," +
        "mean_episode_length,mean_min_tcp_cube_distance,mean_max_cube_height,mean_min_cube_target_distance,mean_final_cube_target_distance," +
        "mean_steps_to_reach,mean_steps_to_lift,mean_steps_to_place,reach_samples,lift_samples,place_samples,number_of_arenas,time_scale," +
        "mean_lift_goal_m,mean_bilateral_contact_actions,mean_holding_actions," +
        "mean_max_consecutive_holding_actions,mean_maximum_held_cube_height_m," +
        "mean_near_joint_limit_actions,mean_minimum_joint_limit_margin_deg," +
        "mean_minimum_manipulability,mean_minimum_joint_limit_margin,mean_maximum_joint_velocity";
    private const string StepHeader =
        "timestamp_utc,session_id,run_name,arena_id,episode,episode_step,phase,tcp_cube_distance,cube_target_distance,cube_height," +
        "instant_reward,cumulative_reward,gripper_opening,was_lifted,inside_target,robot_offset_x,robot_offset_z," +
        "action_0,action_1,action_2,action_3,action_4,action_5,action_6,action_gripper";

    private static readonly CultureInfo CsvCulture = CultureInfo.InvariantCulture;
    private static readonly HashSet<FrankaPickPlaceAgentV3> ActiveAgents = new HashSet<FrankaPickPlaceAgentV3>();
    private static StreamWriter episodeWriter, summaryWriter, stepWriter;
    private static string runName, sessionId;
    private static int arenaCount, globalEpisode, flushEveryEpisodes, sinceFlush;
    private static int summaryWindowEpisodes, stepLogEveryNDecisions;
    private static float timeScale;
    private static int windowCount, reachCount, graspCount, liftCount, targetEntryCount, placeCount;
    private static float rewardSum, lengthSum, minTcpSum, maxHeightSum, minTargetSum, finalTargetSum;
    private static float stepsReachSum, stepsLiftSum, stepsPlaceSum;
    private static float liftGoalSum, bilateralContactSum, holdingSum, maxHoldingSum;
    private static float maxHeldHeightSum, nearLimitSum, minLimitMarginSum;
    private static double minManipulabilitySum, minMarginSum, maxVelocitySum;
    private static int manipEpisodeCount, marginEpisodeCount;

    public static bool DetailedStepLogging { get { return stepWriter != null; } }
    public static int StepLogEveryNDecisions { get { return stepLogEveryNDecisions; } }

    public static void Open(string requestedRunName, int numberOfArenas, float requestedTimeScale,
        int requestedSummaryWindow, int requestedFlushEvery, bool detailedSteps, int requestedStepInterval)
    {
        Close();
        if (string.IsNullOrEmpty(requestedRunName) || !IsSafeName(requestedRunName))
            throw new ArgumentException("trainingRunName V3 solo admite letras, números, guion y guion bajo.");
        runName = requestedRunName;
        sessionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CsvCulture) + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8);
        arenaCount = Mathf.Max(1, numberOfArenas);
        timeScale = requestedTimeScale;
        summaryWindowEpisodes = Mathf.Max(1, requestedSummaryWindow);
        flushEveryEpisodes = Mathf.Max(1, requestedFlushEvery);
        stepLogEveryNDecisions = Mathf.Max(1, requestedStepInterval);
        globalEpisode = sinceFlush = 0;
        ResetWindow();
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TrainingLogs", runName));
        Directory.CreateDirectory(directory);
        string episodesPath = Path.Combine(directory, "episodes.csv");
        globalEpisode = ReadLastGlobalEpisode(episodesPath);
        episodeWriter = OpenWriter(episodesPath, EpisodeHeader);
        summaryWriter = OpenWriter(Path.Combine(directory, "summary.csv"), SummaryHeader);
        if (detailedSteps) stepWriter = OpenWriter(Path.Combine(directory, "steps.csv"), StepHeader);
        Application.quitting += Close;
    }

    public static void Register(FrankaPickPlaceAgentV3 agent) { ActiveAgents.Add(agent); }
    public static void Unregister(FrankaPickPlaceAgentV3 agent) { ActiveAgents.Remove(agent); }

    public static void WriteEpisode(Episode e)
    {
        if (episodeWriter == null) return;
        float actualScale = Time.timeScale;
        if (!Mathf.Approximately(timeScale, actualScale))
        {
            WriteSummary();
            timeScale = actualScale;
        }
        globalEpisode++;
        episodeWriter.WriteLine(string.Join(",", new[] {
            Timestamp(), sessionId, runName, Int(e.arenaId), Int(e.arenaEpisode), Int(globalEpisode),
            Int(e.steps), Number(e.reward), Bit(e.success), e.terminalReason,
            Int(arenaCount), Number(timeScale), Number(e.robotBaseOffsetX), Number(e.robotBaseOffsetZ),
            Number(e.robotBaseLocalPosition.x), Number(e.robotBaseLocalPosition.y), Number(e.robotBaseLocalPosition.z),
            Number(e.cubeSpawnLocalPosition.x), Number(e.cubeSpawnLocalPosition.y), Number(e.cubeSpawnLocalPosition.z),
            Number(e.targetLocalPosition.x), Number(e.targetLocalPosition.y), Number(e.targetLocalPosition.z),
            Number(e.initialTcpCubeDistance), Number(e.minimumTcpCubeDistance), Number(e.finalTcpCubeDistance),
            Number(e.cubeInitialHeight), Number(e.maximumCubeHeight), Number(e.finalCubeHeight),
            Number(e.targetOffsetX), Number(e.targetOffsetZ), Number(e.initialCubeTargetDistance),
            Number(e.minimumCubeTargetDistance), Number(e.finalCubeTargetDistance),
            Bit(e.reachedCube), Bit(e.graspAttempted), Bit(e.wasGrasped), Bit(e.wasLifted),
            Bit(e.enteredTarget), Bit(e.releasedInTarget), Bit(e.placedSuccessfully),
            Int(e.gripperCloseAttempts), Int(e.gripperOpenAttempts), Int(e.stepReachedCube),
            Int(e.stepFirstGrasp), Int(e.stepFirstLift), Int(e.stepEnteredTarget), Int(e.stepSuccess),
            Int(e.stableTargetDecisions), Number(e.finalCubeLinearSpeed), Number(e.finalCubeAngularSpeed),
            Number(e.liftGoal), Int(e.bilateralContactActions), Int(e.holdingActions),
            Int(e.maxConsecutiveHoldingActions), Number(e.maximumHeldCubeHeight),
            Int(e.nearJointLimitActions), Number(e.minimumJointLimitMarginDeg),
            NumberOptional(e.minimumManipulability), NumberOptional(e.meanManipulability),
            NumberOptional(e.finalManipulability), NumberOptional(e.minimumJointLimitMargin),
            Number(e.maximumJointVelocity), Int(e.singularityWarningCount),
            Int(e.jointLimitWarningCount)
        }));
        if (e.terminalReason != "manual_stop")
        {
            windowCount++;
            rewardSum += Finite(e.reward);
            lengthSum += e.steps;
            minTcpSum += Finite(e.minimumTcpCubeDistance);
            maxHeightSum += Finite(e.maximumCubeHeight);
            minTargetSum += Finite(e.minimumCubeTargetDistance);
            finalTargetSum += Finite(e.finalCubeTargetDistance);
            liftGoalSum += Finite(e.liftGoal);
            bilateralContactSum += e.bilateralContactActions;
            holdingSum += e.holdingActions;
            maxHoldingSum += e.maxConsecutiveHoldingActions;
            maxHeldHeightSum += Finite(e.maximumHeldCubeHeight);
            nearLimitSum += e.nearJointLimitActions;
            minLimitMarginSum += Finite(e.minimumJointLimitMarginDeg);
            if (IsFinite(e.minimumManipulability))
            {
                minManipulabilitySum += e.minimumManipulability;
                manipEpisodeCount++;
            }
            if (IsFinite(e.minimumJointLimitMargin))
            {
                minMarginSum += e.minimumJointLimitMargin;
                marginEpisodeCount++;
            }
            maxVelocitySum += Finite(e.maximumJointVelocity);
            if (e.reachedCube) { reachCount++; stepsReachSum += e.stepReachedCube; }
            if (e.wasGrasped) graspCount++;
            if (e.wasLifted) { liftCount++; stepsLiftSum += e.stepFirstLift; }
            if (e.enteredTarget) targetEntryCount++;
            if (e.placedSuccessfully) { placeCount++; stepsPlaceSum += e.stepSuccess; }
            if (windowCount >= summaryWindowEpisodes) WriteSummary();
        }
        if (++sinceFlush >= flushEveryEpisodes) Flush();
    }

    public static void WriteDecision(Decision d)
    {
        if (stepWriter == null || d.actions == null || d.actions.Length != 8) return;
        stepWriter.WriteLine(string.Join(",", new[] {
            Timestamp(), sessionId, runName, Int(d.arenaId), Int(d.arenaEpisode), Int(d.episodeStep),
            d.phase, Number(d.tcpCubeDistance), Number(d.cubeTargetDistance), Number(d.cubeHeight),
            Number(d.instantReward), Number(d.cumulativeReward), Number(d.gripperOpening),
            Bit(d.wasLifted), Bit(d.insideTarget), Number(d.robotOffsetX), Number(d.robotOffsetZ),
            Number(d.actions[0]), Number(d.actions[1]), Number(d.actions[2]), Number(d.actions[3]),
            Number(d.actions[4]), Number(d.actions[5]), Number(d.actions[6]), Number(d.actions[7])
        }));
    }

    public static void Flush()
    {
        episodeWriter?.Flush();
        summaryWriter?.Flush();
        stepWriter?.Flush();
        sinceFlush = 0;
    }

    public static void Close()
    {
        if (episodeWriter == null) return;
        foreach (FrankaPickPlaceAgentV3 agent in new List<FrankaPickPlaceAgentV3>(ActiveAgents))
            if (agent != null) agent.StopLoggingForSession();
        ActiveAgents.Clear();
        WriteSummary();
        Flush();
        episodeWriter.Close();
        summaryWriter.Close();
        stepWriter?.Close();
        episodeWriter = summaryWriter = stepWriter = null;
        Application.quitting -= Close;
    }

    private static void WriteSummary()
    {
        if (summaryWriter == null || windowCount == 0) return;
        float n = windowCount;
        summaryWriter.WriteLine(string.Join(",", new[] {
            Timestamp(), sessionId, runName, Int(globalEpisode), Int(windowCount),
            Number(rewardSum/n), Number(reachCount/n), Number(graspCount/n), Number(liftCount/n),
            Number(targetEntryCount/n), Number(placeCount/n), Number(lengthSum/n), Number(minTcpSum/n),
            Number(maxHeightSum/n), Number(minTargetSum/n), Number(finalTargetSum/n),
            Number(reachCount > 0 ? stepsReachSum/reachCount : 0f),
            Number(liftCount > 0 ? stepsLiftSum/liftCount : 0f),
            Number(placeCount > 0 ? stepsPlaceSum/placeCount : 0f),
            Int(reachCount), Int(liftCount), Int(placeCount), Int(arenaCount), Number(timeScale),
            Number(liftGoalSum/n), Number(bilateralContactSum/n), Number(holdingSum/n),
            Number(maxHoldingSum/n), Number(maxHeldHeightSum/n), Number(nearLimitSum/n),
            Number(minLimitMarginSum/n),
            NumberOptional(manipEpisodeCount > 0 ? (float)(minManipulabilitySum/manipEpisodeCount) : float.NaN),
            NumberOptional(marginEpisodeCount > 0 ? (float)(minMarginSum/marginEpisodeCount) : float.NaN),
            Number((float)(maxVelocitySum/n))
        }));
        ResetWindow();
    }

    private static void ResetWindow()
    {
        windowCount = reachCount = graspCount = liftCount = targetEntryCount = placeCount = 0;
        rewardSum = lengthSum = minTcpSum = maxHeightSum = minTargetSum = finalTargetSum = 0f;
        stepsReachSum = stepsLiftSum = stepsPlaceSum = 0f;
        liftGoalSum = bilateralContactSum = holdingSum = maxHoldingSum = 0f;
        maxHeldHeightSum = nearLimitSum = minLimitMarginSum = 0f;
        minManipulabilitySum = minMarginSum = maxVelocitySum = 0.0;
        manipEpisodeCount = marginEpisodeCount = 0;
    }

    private static StreamWriter OpenWriter(string path, string header)
    {
        bool needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 65536);
        var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
        if (needsHeader) writer.WriteLine(header);
        return writer;
    }

    private static int ReadLastGlobalEpisode(string path)
    {
        if (!File.Exists(path)) return 0;
        string last = null;
        using (var reader = new StreamReader(path))
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line)) last = line;
            }
        if (last == null) return 0;
        string[] cells = last.Split(',');
        int previous;
        return cells.Length > 5 && int.TryParse(cells[5], NumberStyles.None, CsvCulture, out previous)
            ? previous : 0;
    }

    private static bool IsSafeName(string name)
    {
        foreach (char character in name)
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '_') return false;
        return true;
    }

    private static string Timestamp() { return DateTime.UtcNow.ToString("O", CsvCulture); }
    private static float Finite(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value; }
    private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static string NumberOptional(float value) { return IsFinite(value) ? Number(value) : ""; }
    private static string Number(float value) { return Finite(value).ToString("G9", CsvCulture); }
    private static string Int(int value) { return value.ToString(CsvCulture); }
    private static string Bit(bool value) { return value ? "1" : "0"; }
}

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
internal static class TrainingCsvLoggerV3EditorHook
{
    static TrainingCsvLoggerV3EditorHook()
    {
        UnityEditor.EditorApplication.playModeStateChanged += state =>
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) TrainingCsvLoggerV3.Close();
        };
    }
}
#endif

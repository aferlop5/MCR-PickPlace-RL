using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Una sola instancia de escritura para todos los agentes; únicamente se llama desde el hilo principal de Unity.
public static class TrainingCsvLogger
{
    public struct Episode
    {
        public string stage;
        public int arenaId;
        public int arenaEpisode;
        public int steps;
        public float reward;
        public bool success;
        public string terminalReason;
        public float initialDistance;
        public float minimumDistance;
        public float finalDistance;
        public float maximumHeight;
        public float finalHeight;
        public bool reachedCube;
        public int gripperCloseAttempts;
        public float cubeRandomX;
        public float cubeRandomZ;
    }

    public struct Decision
    {
        public int arenaId;
        public int arenaEpisode;
        public int episodeStep;
        public string stage;
        public float distance;
        public float height;
        public float instantReward;
        public float cumulativeReward;
        public float gripperOpening;
        public float[] actions;
    }

    private const string EpisodeHeader = "timestamp_utc,session_id,run_name,stage,arena_id,arena_episode,global_episode,episode_steps,cumulative_reward,success,terminal_reason,initial_distance_m,minimum_distance_m,final_distance_m,maximum_cube_height_m,final_cube_height_m,reached_cube,gripper_close_attempts,cube_random_x_m,cube_random_z_m,number_of_arenas,time_scale";
    private const string SummaryHeader = "timestamp_utc,session_id,run_name,global_episode,episodes_in_window,mean_reward,success_rate,mean_episode_length,mean_minimum_distance,mean_final_distance,mean_maximum_height,number_of_arenas,time_scale,stage";
    private const string StepHeader = "timestamp_utc,session_id,run_name,arena_id,episode,episode_step,stage,distance_tcp_cube,cube_height,instant_reward,cumulative_reward,gripper_opening,action_0,action_1,action_2,action_3,action_4,action_5,action_6,action_gripper";

    private static readonly CultureInfo CsvCulture = CultureInfo.InvariantCulture;
    private static readonly HashSet<FrankaLiftAgent> ActiveAgents = new HashSet<FrankaLiftAgent>();
    private static StreamWriter episodeWriter;
    private static StreamWriter summaryWriter;
    private static StreamWriter stepWriter;
    private static string runName;
    private static string sessionId;
    private static int arenaCount;
    private static float timeScale;
    private static int globalEpisode;
    private static int sinceFlush;
    private static int flushEveryEpisodes;
    private static int summaryWindowEpisodes;
    private static int stepLogEveryNDecisions;
    private static int windowCount;
    private static int windowSuccesses;
    private static float windowReward;
    private static float windowLength;
    private static float windowMinimumDistance;
    private static float windowFinalDistance;
    private static float windowMaximumHeight;
    private static string windowStage;

    public static bool IsOpen { get { return episodeWriter != null; } }
    public static bool DetailedStepLogging { get { return stepWriter != null; } }
    public static int StepLogEveryNDecisions { get { return stepLogEveryNDecisions; } }
    public static string SessionId { get { return sessionId; } }

    public static void Open(string requestedRunName, int numberOfArenas, float requestedTimeScale,
        int requestedSummaryWindow, int requestedFlushEvery, bool detailedSteps, int requestedStepInterval)
    {
        Close();
        if (string.IsNullOrEmpty(requestedRunName) || !IsSafeName(requestedRunName))
            throw new ArgumentException("trainingRunName solo admite letras, números, guion y guion bajo.");

        runName = requestedRunName;
        sessionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CsvCulture) + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8);
        arenaCount = Mathf.Max(1, numberOfArenas);
        timeScale = requestedTimeScale;
        summaryWindowEpisodes = Mathf.Max(1, requestedSummaryWindow);
        flushEveryEpisodes = Mathf.Max(1, requestedFlushEvery);
        stepLogEveryNDecisions = Mathf.Max(1, requestedStepInterval);
        globalEpisode = 0;
        sinceFlush = 0;
        ResetWindow();

        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TrainingLogs", runName));
        Directory.CreateDirectory(directory);
        string episodesPath = Path.Combine(directory, "episodes.csv");
        globalEpisode = ReadLastGlobalEpisode(episodesPath);
        episodeWriter = OpenWriter(episodesPath, EpisodeHeader);
        summaryWriter = OpenWriter(Path.Combine(directory, "summary.csv"), SummaryHeader);
        if (detailedSteps)
            stepWriter = OpenWriter(Path.Combine(directory, "steps.csv"), StepHeader);
        Application.quitting += Close;
    }

    public static void Register(FrankaLiftAgent agent)
    {
        ActiveAgents.Add(agent);
    }

    public static void Unregister(FrankaLiftAgent agent)
    {
        ActiveAgents.Remove(agent);
    }

    public static void WriteEpisode(Episode episode)
    {
        if (episodeWriter == null) return;
        float actualScale = Time.timeScale;
        if (!Mathf.Approximately(timeScale, actualScale))
        {
            if (windowCount > 0) WriteSummary();
            timeScale = actualScale;
        }
        if (windowCount > 0 && windowStage != episode.stage) WriteSummary();
        if (windowCount == 0) windowStage = episode.stage;

        globalEpisode++;
        episodeWriter.WriteLine(string.Join(",", new[] {
            Timestamp(), sessionId, runName, episode.stage, episode.arenaId.ToString(CsvCulture),
            episode.arenaEpisode.ToString(CsvCulture), globalEpisode.ToString(CsvCulture),
            episode.steps.ToString(CsvCulture), Number(episode.reward), episode.success ? "1" : "0",
            episode.terminalReason, Number(episode.initialDistance), Number(episode.minimumDistance),
            Number(episode.finalDistance), Number(episode.maximumHeight), Number(episode.finalHeight),
            episode.reachedCube ? "1" : "0", episode.gripperCloseAttempts.ToString(CsvCulture),
            Number(episode.cubeRandomX), Number(episode.cubeRandomZ), arenaCount.ToString(CsvCulture),
            Number(timeScale)
        }));

        windowCount++;
        if (episode.success) windowSuccesses++;
        windowReward += Finite(episode.reward);
        windowLength += episode.steps;
        windowMinimumDistance += Finite(episode.minimumDistance);
        windowFinalDistance += Finite(episode.finalDistance);
        windowMaximumHeight += Finite(episode.maximumHeight);
        if (windowCount >= summaryWindowEpisodes) WriteSummary();
        if (++sinceFlush >= flushEveryEpisodes) Flush();
    }

    public static void WriteDecision(Decision decision)
    {
        if (stepWriter == null || decision.actions == null || decision.actions.Length != 8) return;
        string[] values = {
            Timestamp(), sessionId, runName, decision.arenaId.ToString(CsvCulture),
            decision.arenaEpisode.ToString(CsvCulture), decision.episodeStep.ToString(CsvCulture),
            decision.stage, Number(decision.distance), Number(decision.height), Number(decision.instantReward),
            Number(decision.cumulativeReward), Number(decision.gripperOpening),
            Number(decision.actions[0]), Number(decision.actions[1]), Number(decision.actions[2]),
            Number(decision.actions[3]), Number(decision.actions[4]), Number(decision.actions[5]),
            Number(decision.actions[6]), Number(decision.actions[7])
        };
        stepWriter.WriteLine(string.Join(",", values));
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
        // También registra los episodios incompletos antes de detener Play.
        foreach (FrankaLiftAgent agent in new List<FrankaLiftAgent>(ActiveAgents))
            if (agent != null) agent.StopLoggingForSession();
        ActiveAgents.Clear();
        if (windowCount > 0) WriteSummary();
        Flush();
        episodeWriter.Close();
        summaryWriter.Close();
        stepWriter?.Close();
        episodeWriter = null;
        summaryWriter = null;
        stepWriter = null;
        Application.quitting -= Close;
    }

    private static void WriteSummary()
    {
        if (summaryWriter == null || windowCount == 0) return;
        float divisor = windowCount;
        summaryWriter.WriteLine(string.Join(",", new[] {
            Timestamp(), sessionId, runName, globalEpisode.ToString(CsvCulture),
            windowCount.ToString(CsvCulture), Number(windowReward / divisor),
            Number(windowSuccesses / divisor), Number(windowLength / divisor),
            Number(windowMinimumDistance / divisor), Number(windowFinalDistance / divisor),
            Number(windowMaximumHeight / divisor), arenaCount.ToString(CsvCulture), Number(timeScale), windowStage
        }));
        ResetWindow();
    }

    private static void ResetWindow()
    {
        windowCount = 0;
        windowSuccesses = 0;
        windowReward = 0f;
        windowLength = 0f;
        windowMinimumDistance = 0f;
        windowFinalDistance = 0f;
        windowMaximumHeight = 0f;
        windowStage = "";
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
        return cells.Length > 6 && int.TryParse(cells[6], NumberStyles.None, CsvCulture, out previous)
            ? previous : 0;
    }

    private static bool IsSafeName(string name)
    {
        foreach (char character in name)
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '_') return false;
        return true;
    }

    private static string Timestamp()
    {
        return DateTime.UtcNow.ToString("O", CsvCulture);
    }

    private static float Finite(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private static string Number(float value)
    {
        return Finite(value).ToString("G9", CsvCulture);
    }
}

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
internal static class TrainingCsvLoggerEditorHook
{
    static TrainingCsvLoggerEditorHook()
    {
        UnityEditor.EditorApplication.playModeStateChanged += state =>
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) TrainingCsvLogger.Close();
        };
    }
}
#endif

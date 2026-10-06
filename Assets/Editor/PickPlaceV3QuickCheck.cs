using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PickPlaceV3QuickCheck
{
    private const string ScenePath = "Assets/Scenes/PickPlaceV3.unity";
    private const string ActiveKey = "PickPlaceRL.Safe50.Active";
    private const string OriginalRunKey = "PickPlaceRL.Safe50.OriginalRun";
    private const string StartKey = "PickPlaceRL.Safe50.StartTicks";
    private const string ErrorsKey = "PickPlaceRL.Safe50.Errors";
    private const string StoppingKey = "PickPlaceRL.Safe50.Stopping";

    static PickPlaceV3QuickCheck()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/PickPlaceRL/V3/Comprobación corta de 50 arenas")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || SessionState.GetBool(ActiveKey, false))
            throw new InvalidOperationException("Detén Play antes de comprobar las 50 arenas.");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TrainingArenaSpawnerV3 spawner = Object.FindFirstObjectByType<TrainingArenaSpawnerV3>();
        if (spawner == null || spawner.numberOfArenas != 50 || spawner.columns != 10 ||
            !Mathf.Approximately(spawner.spacing, 5f) || !Mathf.Approximately(spawner.timeScale, 1f))
            throw new InvalidOperationException("La escena no configura 50 arenas, 10 columnas, 5 m y timeScale 1.");
        SessionState.SetString(OriginalRunKey, spawner.trainingRunName);
        SessionState.SetInt(ErrorsKey, 0);
        SessionState.SetBool(StoppingKey, false);
        SessionState.SetBool(ActiveKey, true);
        spawner.trainingRunName = "v3_safe50_quickcheck";
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.SaveScene(scene);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            foreach (FrankaPickPlaceAgentV3 agent in
                Object.FindObjectsByType<FrankaPickPlaceAgentV3>(FindObjectsSortMode.None))
                agent.MaxStep = 50; // Solo la prueba: permite escribir al menos un episodio por arena.
            SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString());
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            TrainingArenaSpawnerV3 spawner = Object.FindFirstObjectByType<TrainingArenaSpawnerV3>();
            spawner.trainingRunName = SessionState.GetString(OriginalRunKey, "franka_pickplace_v3_safe50");
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(ActiveKey, false);
            Debug.Log("V3SAFE50: escena restaurada; CSV en TrainingLogs/v3_safe50_quickcheck/.");
            if (Application.isBatchMode)
                EditorApplication.Exit(SessionState.GetInt(ErrorsKey, 0) == 0 ? 0 : 1);
        }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying ||
            SessionState.GetBool(StoppingKey, false)) return;
        long ticks;
        if (!long.TryParse(SessionState.GetString(StartKey, ""), out ticks)) return;
        if ((DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds < 3.0) return;
        TrainingArenaReferencesV3[] arenas =
            Object.FindObjectsByType<TrainingArenaReferencesV3>(FindObjectsSortMode.None);
        var ids = new bool[50];
        var unique = new HashSet<Object>();
        int activeAgents = 0;
        int actionCalls = 0;
        bool valid = arenas.Length == 50;
        bool localAndUnique = true;
        foreach (TrainingArenaReferencesV3 arena in arenas)
        {
            if (arena == null || !arena.HasLocalReferences() || arena.agent == null ||
                arena.agent.jointBodies == null || arena.agent.jointBodies.Length != 7)
            {
                valid = false;
                continue;
            }
            FrankaPickPlaceAgentV3 agent = arena.agent;
            if (agent.isActiveAndEnabled) activeAgents++;
            actionCalls += agent.TotalActionCount;
            int id = agent.arenaId;
            if (id < 0 || id >= 50 || ids[id]) valid = false;
            else ids[id] = true;
            localAndUnique &= unique.Add(agent) && unique.Add(arena.trainingCube) &&
                              unique.Add(arena.placeTarget) && unique.Add(agent.jointBodies[0]);
        }
        bool allIds = true;
        foreach (bool found in ids) allIds &= found;
        valid &= allIds && localAndUnique;
        valid &= activeAgents == 50 && actionCalls > 0 &&
                 SessionState.GetInt(ErrorsKey, 0) == 0;
        if (!valid) SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
        Debug.Log("V3SAFE50_" + (valid ? "PASS" : "FAIL") + " active_agents=" + activeAgents +
                  " arena_ids_0_49=" + allIds + " unique_local_references=" + localAndUnique +
                  " action_calls=" + actionCalls + " agent_errors=" +
                  SessionState.GetInt(ErrorsKey, 0));
        SessionState.SetBool(StoppingKey, true);
        EditorApplication.ExitPlaymode();
    }

    private static void OnLog(string message, string trace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying ||
            (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) ||
            trace.Contains("UnityEditor.Search.SearchDatabase")) return;
        SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
    }
}

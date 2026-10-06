using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PickPlaceV3Benchmark
{
    private static readonly int[] Counts = { 1, 4, 8, 16, 24, 36 };
    private const string ScenePath = "Assets/Scenes/PickPlaceV3.unity";
    private const string PrefabPath = "Assets/Prefabs/TrainingArenaV3.prefab";
    private const int DurationSeconds = 15;
    private const string ActiveKey = "PickPlaceRL.V3Benchmark.Active";
    private const string IndexKey = "PickPlaceRL.V3Benchmark.Index";
    private const string StartKey = "PickPlaceRL.V3Benchmark.StartTicks";
    private const string ErrorsKey = "PickPlaceRL.V3Benchmark.Errors";
    private const string EditorErrorsKey = "PickPlaceRL.V3Benchmark.EditorSearchErrors";
    private const string NextKey = "PickPlaceRL.V3Benchmark.AwaitingNext";
    private const string OriginalCountKey = "PickPlaceRL.V3Benchmark.OriginalCount";
    private const string OriginalRunKey = "PickPlaceRL.V3Benchmark.OriginalRun";
    private const string OriginalScaleKey = "PickPlaceRL.V3Benchmark.OriginalScale";
    private const string OriginalDetailedKey = "PickPlaceRL.V3Benchmark.OriginalDetailed";
    private const string OriginalStepIntervalKey = "PickPlaceRL.V3Benchmark.OriginalStepInterval";
    private const string DetailedKey = "PickPlaceRL.V3Benchmark.DetailedSmoke";

    static PickPlaceV3Benchmark()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/PickPlaceRL/Validar referencias 1-4-8-16-24-36")]
    public static void ValidatePrefabMatrix()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Falta TrainingArena.prefab.");
        foreach (int count in Counts)
        {
            var copies = new List<GameObject>();
            var unique = new HashSet<Object>();
            try
            {
                Vector3 baselineCube = Vector3.zero;
                Vector3 baselineTcp = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    GameObject copy = Object.Instantiate(prefab);
                    copies.Add(copy);
                    copy.transform.position = new Vector3((i % 4) * 5f, 0f, (i / 4) * 5f);
                    TrainingArenaReferencesV3 references = copy.GetComponent<TrainingArenaReferencesV3>();
                    if (references == null || !references.HasLocalReferences())
                        throw new InvalidOperationException("Referencias externas en arena " + i);
                    FrankaPickPlaceAgentV3 agent = references.agent;
                    BehaviorParameters behavior = agent.GetComponent<BehaviorParameters>();
                    DecisionRequester requester = agent.GetComponent<DecisionRequester>();
                    if (behavior == null || behavior.BehaviorName != "FrankaPickPlaceV3" ||
                        behavior.BrainParameters.VectorObservationSize != 33 ||
                        behavior.BrainParameters.ActionSpec.NumContinuousActions != 8 ||
                        requester == null || requester.DecisionPeriod != 5 ||
                        !requester.TakeActionsBetweenDecisions || !Mathf.Approximately(agent.liftThreshold, 0.30f) ||
                        agent.MaxStep != 2500 || agent.placeTarget == null)
                        throw new InvalidOperationException("Configuración de ML-Agents distinta en arena " + i);
                    if (copy.GetComponentsInChildren<RobotPickSequence>(true).Length != 0 ||
                        copy.GetComponentsInChildren<RobotJointController>(true).Length != 0)
                        throw new InvalidOperationException("Hay un controlador manual en la arena de RL " + i);
                    RequireUnique(unique, agent, i);
                    RequireUnique(unique, references.trainingCube, i);
                    RequireUnique(unique, agent.tcpTransform, i);
                    RequireUnique(unique, agent.leftFinger, i);
                    RequireUnique(unique, agent.rightFinger, i);
                    RequireUnique(unique, agent.placeTarget, i);
                    RequireUnique(unique, agent.leftContact, i);
                    RequireUnique(unique, agent.rightContact, i);
                    foreach (ArticulationBody joint in agent.jointBodies) RequireUnique(unique, joint, i);
                    Vector3 localCube = copy.transform.InverseTransformPoint(references.trainingCube.position);
                    Vector3 localTcp = copy.transform.InverseTransformPoint(agent.tcpTransform.position);
                    if (i == 0) { baselineCube = localCube; baselineTcp = localTcp; }
                    else if (Vector3.Distance(localCube, baselineCube) > 0.0001f ||
                             Vector3.Distance(localTcp, baselineTcp) > 0.0001f)
                        throw new InvalidOperationException("Las arenas no representan las mismas posiciones locales.");
                }
                Debug.Log("Referencias, acciones, observaciones y posiciones locales correctas con " + count + " arenas.");
            }
            finally
            {
                foreach (GameObject copy in copies) Object.DestroyImmediate(copy);
            }
        }
    }

    [MenuItem("Tools/PickPlaceRL/Benchmark de arenas 1-4-8-16-24-36")]
    public static void RunBatchmode()
    {
        Begin(false);
    }

    [MenuItem("Tools/PickPlaceRL/V3/Validar CSV detallado con 2 arenas")]
    public static void RunDetailedLogSmoke()
    {
        Begin(true);
    }

    private static void Begin(bool detailedSmoke)
    {
        if (EditorApplication.isPlaying || SessionState.GetBool(ActiveKey, false))
            throw new InvalidOperationException("Detén Play o el benchmark anterior antes de empezar.");
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Guarda la escena antes del benchmark.");
        ValidatePrefabMatrix();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TrainingArenaSpawnerV3 spawner = Object.FindFirstObjectByType<TrainingArenaSpawnerV3>();
        if (spawner == null) throw new InvalidOperationException("SampleScene no contiene TrainingArenaSpawnerV3.");
        SessionState.SetInt(OriginalCountKey, spawner.numberOfArenas);
        SessionState.SetString(OriginalRunKey, spawner.trainingRunName);
        SessionState.SetString(OriginalScaleKey, spawner.timeScale.ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetBool(OriginalDetailedKey, spawner.detailedStepLogging);
        SessionState.SetInt(OriginalStepIntervalKey, spawner.stepLogEveryNDecisions);
        SessionState.SetBool(DetailedKey, detailedSmoke);
        SessionState.SetInt(IndexKey, 0);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(NextKey, false);
        EditorSceneManager.SaveScene(scene);
        StartNextScenario();
    }

    private static void StartNextScenario()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        int index = SessionState.GetInt(IndexKey, 0);
        bool detailedSmoke = SessionState.GetBool(DetailedKey, false);
        if (index >= (detailedSmoke ? 1 : Counts.Length)) { Finish(); return; }
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TrainingArenaSpawnerV3 spawner = Object.FindFirstObjectByType<TrainingArenaSpawnerV3>();
        spawner.numberOfArenas = detailedSmoke ? 2 : Counts[index];
        spawner.timeScale = 1f;
        spawner.trainingRunName = detailedSmoke ? "v3_lift36_arena_detailed_smoke" :
            "v3_lift36_arena_benchmark_" + Counts[index];
        spawner.detailedStepLogging = detailedSmoke;
        spawner.stepLogEveryNDecisions = 10;
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetString(StartKey, "");
        SessionState.SetInt(ErrorsKey, 0);
        SessionState.SetInt(EditorErrorsKey, 0);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) BeginTimedRun();
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(NextKey, false))
        {
            SessionState.SetBool(NextKey, false);
            SessionState.SetInt(IndexKey, SessionState.GetInt(IndexKey, 0) + 1);
            EditorApplication.delayCall += StartNextScenario;
        }
    }

    private static void BeginTimedRun()
    {
        bool detailedSmoke = SessionState.GetBool(DetailedKey, false);
        foreach (FrankaPickPlaceAgentV3 agent in Object.FindObjectsByType<FrankaPickPlaceAgentV3>(FindObjectsSortMode.None))
        {
            agent.MaxStep = 100; // Solo en la prueba corta: fuerza episodios reales y comprueba el CSV.
        }
        SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying ||
            SessionState.GetBool(NextKey, false)) return;
        string started = SessionState.GetString(StartKey, "");
        if (string.IsNullOrEmpty(started)) { BeginTimedRun(); return; }
        long ticks;
        if (!long.TryParse(started, out ticks)) return;
        double duration = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
        bool detailedSmoke = SessionState.GetBool(DetailedKey, false);
        if (duration < (detailedSmoke ? 7 : DurationSeconds)) return;

        int requested = detailedSmoke ? 2 : Counts[SessionState.GetInt(IndexKey, 0)];
        FrankaPickPlaceAgentV3[] agents = Object.FindObjectsByType<FrankaPickPlaceAgentV3>(FindObjectsSortMode.None);
        TrainingArenaReferencesV3[] references = Object.FindObjectsByType<TrainingArenaReferencesV3>(FindObjectsSortMode.None);
        int episodes = 0;
        int timeouts = 0;
        int actions = 0;
        foreach (FrankaPickPlaceAgentV3 agent in agents)
        {
            episodes += agent.CompletedEpisodeCount;
            timeouts += agent.TimeoutEpisodeCount;
            actions += agent.TotalActionCount;
        }
        bool local = references.Length == requested;
        foreach (TrainingArenaReferencesV3 arena in references) local &= arena.HasLocalReferences();
        int errors = SessionState.GetInt(ErrorsKey, 0);
        int editorSearchErrors = SessionState.GetInt(EditorErrorsKey, 0);
        bool stable = agents.Length == requested && local && errors == 0 && actions > 0 &&
                      Application.runInBackground;
        if (detailedSmoke)
            Debug.Log("CSV detallado: " + episodes + " episodios, " + actions +
                      " acciones, referencias locales=" + local + ", errores=" + errors);
        else
            WriteResult(requested, duration, episodes, timeouts, actions, errors, editorSearchErrors,
                agents.Length, local, stable);
        SessionState.SetBool(NextKey, true);
        EditorApplication.ExitPlaymode();
    }

    private static void WriteResult(int count, double duration, int episodes, int timeouts, int actions, int errors,
        int editorSearchErrors, int activeAgents, bool local, bool stable)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TrainingLogs", "v3_lift36_arena_benchmark.csv"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        bool header = !File.Exists(path) || new FileInfo(path).Length == 0;
        using (var writer = new StreamWriter(path, true))
        {
            if (header) writer.WriteLine("number_of_arenas,time_scale,duration_seconds,episodes_completed,timeouts,episodes_per_second,agent_action_calls_per_second,errors,editor_search_errors,stable,active_agents,local_references,run_in_background");
            writer.WriteLine(string.Join(",", new[] {
                count.ToString(CultureInfo.InvariantCulture), "1", duration.ToString("F3", CultureInfo.InvariantCulture),
                episodes.ToString(CultureInfo.InvariantCulture), timeouts.ToString(CultureInfo.InvariantCulture),
                (episodes / duration).ToString("F3", CultureInfo.InvariantCulture),
                (actions / duration).ToString("F3", CultureInfo.InvariantCulture), errors.ToString(CultureInfo.InvariantCulture),
                editorSearchErrors.ToString(CultureInfo.InvariantCulture),
                stable ? "1" : "0", activeAgents.ToString(CultureInfo.InvariantCulture), local ? "1" : "0",
                Application.runInBackground ? "1" : "0"
            }));
        }
        Debug.Log("Benchmark de " + count + " arenas: " + actions + " llamadas de acción en " +
                  duration.ToString("F1", CultureInfo.InvariantCulture) + " s; estable=" + stable);
    }

    private static void Finish()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TrainingArenaSpawnerV3 spawner = Object.FindFirstObjectByType<TrainingArenaSpawnerV3>();
        spawner.numberOfArenas = SessionState.GetInt(OriginalCountKey, 1);
        spawner.trainingRunName = SessionState.GetString(OriginalRunKey, "franka_pickplace_v3_lift36");
        spawner.detailedStepLogging = SessionState.GetBool(OriginalDetailedKey, false);
        spawner.stepLogEveryNDecisions = SessionState.GetInt(OriginalStepIntervalKey, 10);
        float scale;
        if (float.TryParse(SessionState.GetString(OriginalScaleKey, "1"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out scale)) spawner.timeScale = scale;
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetBool(ActiveKey, false);
        Debug.Log(SessionState.GetBool(DetailedKey, false)
            ? "Prueba detallada terminada. Consulta TrainingLogs/v3_lift36_arena_detailed_smoke/."
            : "Benchmark terminado. Consulta TrainingLogs/v3_lift36_arena_benchmark.csv.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying ||
            (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        if (stackTrace.Contains("UnityEditor.Search.SearchDatabase"))
            SessionState.SetInt(EditorErrorsKey, SessionState.GetInt(EditorErrorsKey, 0) + 1);
        else
            SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
    }

    private static void RequireUnique(HashSet<Object> items, Object item, int arena)
    {
        if (!items.Add(item)) throw new InvalidOperationException("Referencia compartida entre arenas: " + arena);
    }
}


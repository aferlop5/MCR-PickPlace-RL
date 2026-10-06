using UnityEngine;

public class TrainingArenaSpawnerV3 : MonoBehaviour
{
    public TrainingArenaReferencesV3 firstArena;
    [Range(1, 50)] public int numberOfArenas = 50;
    [Min(1)] public int columns = 10;
    [Min(2f)] public float spacing = 5f;
    [Min(0.1f)] public float timeScale = 1f;
    [Header("Registro V3")]
    public string trainingRunName = "franka_pickplace_v3_safe50";
    [Min(1)] public int summaryWindowEpisodes = 100;
    [Min(1)] public int flushEveryEpisodes = 32;
    public bool detailedStepLogging = false;
    [Min(1)] public int stepLogEveryNDecisions = 10;
    private float previousTimeScale;
    private bool previousRunInBackground;

    private void Awake()
    {
        previousTimeScale = Time.timeScale;
        previousRunInBackground = Application.runInBackground;
        if (firstArena == null || !firstArena.HasLocalReferences())
        {
            Debug.LogError("TrainingArenaSpawnerV3: falta una arena V3 con referencias locales válidas.", this);
            return;
        }
        Application.runInBackground = true;
        Time.timeScale = Mathf.Max(0.1f, timeScale);
        TrainingCsvLoggerV3.Open(trainingRunName, numberOfArenas, Time.timeScale,
            summaryWindowEpisodes, flushEveryEpisodes, detailedStepLogging, stepLogEveryNDecisions);
        firstArena.ConfigureForTraining(0);
        Vector3 origin = firstArena.transform.position;
        Quaternion rotation = firstArena.transform.rotation;
        for (int index = 1; index < numberOfArenas; index++)
        {
            int row = index / columns;
            int column = index % columns;
            Vector3 position = origin + rotation * new Vector3(column * spacing, 0f, row * spacing);
            TrainingArenaReferencesV3 copy = Instantiate(firstArena, position, rotation, transform);
            copy.name = "TrainingArenaV3_" + (index + 1).ToString("00");
            if (!copy.HasLocalReferences())
                Debug.LogError("TrainingArenaSpawnerV3: referencia externa en una copia.", copy);
            copy.ConfigureForTraining(index);
        }
    }

    private void Update()
    {
        float desired = Mathf.Max(0.1f, timeScale);
        if (!Mathf.Approximately(Time.timeScale, desired)) Time.timeScale = desired;
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying) return;
        TrainingCsvLoggerV3.Close();
        Time.timeScale = previousTimeScale;
        Application.runInBackground = previousRunInBackground;
    }
}

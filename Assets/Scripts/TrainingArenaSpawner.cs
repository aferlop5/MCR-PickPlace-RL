using UnityEngine;

public class TrainingArenaSpawner : MonoBehaviour
{
    [Tooltip("Instancia de TrainingArena ya colocada en la escena")]
    public TrainingArenaReferences firstArena;
    [Range(1, 16)] public int numberOfArenas = 1;
    [Min(1)] public int columns = 4;
    [Min(2f)] public float spacing = 5f;
    [Min(0.1f)] public float timeScale = 1f;
    [Header("Registro del entrenamiento")]
    public string trainingRunName = "franka_reach_v2";
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
            Debug.LogError("TrainingArenaSpawner: asigna una primera arena con referencias internas válidas.", this);
            return;
        }

        Application.runInBackground = true;
        Time.timeScale = Mathf.Max(0.1f, timeScale);
        TrainingCsvLogger.Open(trainingRunName, numberOfArenas, Time.timeScale,
            summaryWindowEpisodes, flushEveryEpisodes, detailedStepLogging, stepLogEveryNDecisions);
        firstArena.ConfigureForTraining(0);

        Vector3 origin = firstArena.transform.position;
        Quaternion rotation = firstArena.transform.rotation;
        for (int index = 1; index < numberOfArenas; index++)
        {
            int row = index / columns;
            int column = index % columns;
            Vector3 position = origin + rotation * new Vector3(column * spacing, 0f, row * spacing);
            TrainingArenaReferences copy = Instantiate(firstArena, position, rotation, transform);
            copy.name = "TrainingArena_" + (index + 1).ToString("00");
            if (!copy.HasLocalReferences())
                Debug.LogError("TrainingArenaSpawner: una copia contiene referencias a otra arena.", copy);
            copy.ConfigureForTraining(index);
        }
    }

    private void Update()
    {
        // ML-Agents puede aplicar su propio EngineConfiguration al conectarse o reiniciar.
        // El Inspector de esta escena sigue siendo la referencia para la escala física.
        float desired = Mathf.Max(0.1f, timeScale);
        if (!Mathf.Approximately(Time.timeScale, desired)) Time.timeScale = desired;
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying) return;
        TrainingCsvLogger.Close();
        Time.timeScale = previousTimeScale;
        Application.runInBackground = previousRunInBackground;
    }
}

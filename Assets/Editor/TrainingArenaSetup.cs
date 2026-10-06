using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TrainingArenaSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/TrainingArena.prefab";

    [MenuItem("Tools/PickPlaceRL/Crear prefab de arena desde SampleScene")]
    public static void CreateFromSampleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject existing = FindRoot(scene, "TrainingArena");
        if (existing != null)
        {
            Debug.Log("TrainingArena ya existe en SampleScene; no se han duplicado objetos.");
            return;
        }

        GameObject robot = FindRoot(scene, "fr3");
        GameObject table = FindRoot(scene, "Table");
        GameObject cube = FindRoot(scene, "TestObject");
        if (robot == null || table == null || cube == null)
            throw new System.InvalidOperationException("Faltan fr3, Table o TestObject como objetos raíz.");

        FrankaLiftAgent agent = robot.GetComponent<FrankaLiftAgent>();
        Collider tableCollider = table.GetComponent<Collider>();
        Rigidbody cubeRigidbody = cube.GetComponent<Rigidbody>();
        if (agent == null || tableCollider == null || cubeRigidbody == null ||
            agent.cubeTransform != cube.transform || agent.cubeRigidbody != cubeRigidbody)
            throw new System.InvalidOperationException("Las referencias actuales de la escena no coinciden con la arena esperada.");

        GameObject arena = new GameObject("TrainingArena");
        robot.transform.SetParent(arena.transform, true);
        table.transform.SetParent(arena.transform, true);
        cube.transform.SetParent(arena.transform, true);
        agent.arenaRoot = arena.transform;
        agent.tableCollider = tableCollider;
        TrainingArenaReferences references = arena.AddComponent<TrainingArenaReferences>();
        references.agent = agent;
        references.trainingCube = cube.transform;
        references.tableCollider = tableCollider;

        if (!references.HasLocalReferences())
            throw new System.InvalidOperationException("La arena no ha quedado autocontenida.");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        PrefabUtility.SaveAsPrefabAssetAndConnect(arena, PrefabPath, InteractionMode.AutomatedAction);

        GameObject spawnerObject = new GameObject("TrainingArenaSpawner");
        TrainingArenaSpawner spawner = spawnerObject.AddComponent<TrainingArenaSpawner>();
        spawner.firstArena = arena.GetComponent<TrainingArenaReferences>();
        spawner.numberOfArenas = 1;
        spawner.columns = 4;
        spawner.spacing = 5f;
        spawner.timeScale = 1f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("TrainingArena.prefab creado. SampleScene contiene una arena; el spawner comienza en 1.");
    }

    [MenuItem("Tools/PickPlaceRL/Validar referencias de dos arenas")]
    public static void ValidateTwoArenas()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new System.InvalidOperationException("No existe TrainingArena.prefab.");

        GameObject first = Object.Instantiate(prefab);
        GameObject second = Object.Instantiate(prefab);
        try
        {
            TrainingArenaReferences a = first.GetComponent<TrainingArenaReferences>();
            TrainingArenaReferences b = second.GetComponent<TrainingArenaReferences>();
            if (a == null || b == null || !a.HasLocalReferences() || !b.HasLocalReferences() ||
                a.agent == b.agent || a.trainingCube == b.trainingCube || a.tableCollider == b.tableCollider)
                throw new System.InvalidOperationException("Las dos arenas no tienen referencias independientes.");
            Debug.Log("Dos instancias de TrainingArena mantienen referencias internas independientes.");
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }
}

using System;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PickPlaceV3Setup
{
    private const string SourceScene = "Assets/Scenes/SampleScene.unity";
    private const string SourcePrefab = "Assets/Prefabs/TrainingArena.prefab";
    private const string V3Scene = "Assets/Scenes/PickPlaceV3.unity";
    private const string V3Prefab = "Assets/Prefabs/TrainingArenaV3.prefab";
    private const string TargetMaterial = "Assets/Prefabs/PlaceTargetV3.mat";

    [MenuItem("Tools/PickPlaceRL/V3/Crear escena y prefab V3 desde V2")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(V3Prefab) != null ||
            AssetDatabase.LoadAssetAtPath<SceneAsset>(V3Scene) != null)
            throw new InvalidOperationException("V3 ya existe. No se sobrescribe automáticamente.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab) == null ||
            AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
            throw new InvalidOperationException("Falta la escena o el prefab V2.");

        Material material = MakeTargetMaterial();
        GameObject contents = PrefabUtility.LoadPrefabContents(SourcePrefab);
        try
        {
            contents.name = "TrainingArenaV3";
            TrainingArenaReferences oldReferences = contents.GetComponent<TrainingArenaReferences>();
            if (oldReferences == null || !oldReferences.HasLocalReferences())
                throw new InvalidOperationException("El prefab V2 no tiene referencias internas válidas.");
            FrankaLiftAgent oldAgent = oldReferences.agent;
            GameObject robot = oldAgent.gameObject;
            Collider cubeCollider = oldReferences.trainingCube.GetComponent<Collider>();
            if (cubeCollider == null) throw new InvalidOperationException("El cubo no tiene Collider.");
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            target.name = "PlaceTarget";
            target.transform.SetParent(contents.transform, false);
            Vector3 cubeLocal = contents.transform.InverseTransformPoint(oldReferences.trainingCube.position);
            float tableTop = oldReferences.tableCollider.bounds.max.y;
            float tableTopLocal = contents.transform.InverseTransformPoint(
                new Vector3(oldReferences.trainingCube.position.x, tableTop,
                    oldReferences.trainingCube.position.z)).y;
            target.transform.localPosition = new Vector3(cubeLocal.x, tableTopLocal + 0.002f,
                cubeLocal.z + 0.18f); // Izquierda del Franka mirando hacia el cubo: +Z local.
            target.transform.localScale = new Vector3(0.08f, 0.001f, 0.08f);
            Collider zone = target.GetComponent<Collider>();
            zone.isTrigger = true;
            target.GetComponent<MeshRenderer>().sharedMaterial = material;

            FrankaFingerContactV3 leftContact = oldAgent.leftFinger.gameObject.AddComponent<FrankaFingerContactV3>();
            FrankaFingerContactV3 rightContact = oldAgent.rightFinger.gameObject.AddComponent<FrankaFingerContactV3>();
            leftContact.cubeCollider = cubeCollider;
            rightContact.cubeCollider = cubeCollider;
            FrankaPickPlaceAgentV3 agent = robot.AddComponent<FrankaPickPlaceAgentV3>();
            agent.jointBodies = oldAgent.jointBodies;
            agent.leftFinger = oldAgent.leftFinger;
            agent.rightFinger = oldAgent.rightFinger;
            agent.leftContact = leftContact;
            agent.rightContact = rightContact;
            agent.arenaRoot = contents.transform;
            agent.cubeTransform = oldReferences.trainingCube;
            agent.cubeRigidbody = oldAgent.cubeRigidbody;
            agent.cubeCollider = cubeCollider;
            agent.tcpTransform = oldAgent.tcpTransform;
            agent.tableCollider = oldReferences.tableCollider;
            agent.placeTarget = target.transform;
            agent.MaxStep = 2500;
            BehaviorParameters behavior = robot.GetComponent<BehaviorParameters>();
            if (behavior == null) throw new InvalidOperationException("Falta BehaviorParameters.");
            behavior.BehaviorName = "FrankaPickPlaceV3";
            behavior.BrainParameters.VectorObservationSize = 33;
            behavior.BehaviorType = BehaviorType.Default;
            behavior.Model = null;

            UnityEngine.Object.DestroyImmediate(oldAgent);
            UnityEngine.Object.DestroyImmediate(oldReferences);
            TrainingArenaReferencesV3 references = contents.AddComponent<TrainingArenaReferencesV3>();
            references.agent = agent;
            references.trainingCube = agent.cubeTransform;
            references.placeTarget = target.transform;
            references.tableCollider = agent.tableCollider;
            if (!references.HasLocalReferences())
                throw new InvalidOperationException("Las referencias V3 no son internas al prefab.");
            PrefabUtility.SaveAsPrefabAsset(contents, V3Prefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        if (!AssetDatabase.CopyAsset(SourceScene, V3Scene))
            throw new InvalidOperationException("No se pudo copiar SampleScene a PickPlaceV3.");
        Scene scene = EditorSceneManager.OpenScene(V3Scene, OpenSceneMode.Single);
        TrainingArenaReferences originalArena = UnityEngine.Object.FindFirstObjectByType<TrainingArenaReferences>();
        TrainingArenaSpawner oldSpawner = UnityEngine.Object.FindFirstObjectByType<TrainingArenaSpawner>();
        if (originalArena == null || oldSpawner == null)
            throw new InvalidOperationException("La copia de SampleScene no contiene arena o spawner V2.");
        Vector3 origin = originalArena.transform.position;
        Quaternion rotation = originalArena.transform.rotation;
        UnityEngine.Object.DestroyImmediate(oldSpawner.gameObject);
        UnityEngine.Object.DestroyImmediate(originalArena.gameObject);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(V3Prefab);
        GameObject arenaInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        arenaInstance.transform.SetPositionAndRotation(origin, rotation);
        GameObject spawnerObject = new GameObject("TrainingArenaSpawnerV3");
        SceneManager.MoveGameObjectToScene(spawnerObject, scene);
        TrainingArenaSpawnerV3 spawner = spawnerObject.AddComponent<TrainingArenaSpawnerV3>();
        spawner.firstArena = arenaInstance.GetComponent<TrainingArenaReferencesV3>();
        spawner.numberOfArenas = 50;
        spawner.columns = 10;
        spawner.spacing = 5f;
        spawner.timeScale = 1f;
        spawner.trainingRunName = "franka_pickplace_v3_safe50";
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("V3 preparada: target +Z local 0.18 m, 50 arenas, una política FrankaPickPlaceV3.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static Material MakeTargetMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null) throw new InvalidOperationException("No se encontró un shader Unlit para el target.");
        Material material = new Material(shader);
        material.name = "PlaceTargetV3_Green";
        Color color = new Color(0.1f, 1f, 0.25f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else material.color = color;
        AssetDatabase.CreateAsset(material, TargetMaterial);
        return material;
    }
}

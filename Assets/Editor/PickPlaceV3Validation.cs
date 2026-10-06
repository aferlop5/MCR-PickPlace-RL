using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class PickPlaceV3Validation
{
    [MenuItem("Tools/PickPlaceRL/V3/Validar criterio de colocación")]
    public static void ValidatePlacementGate()
    {
        // Deslizar/empujar al target: nunca hubo Lift.
        Require(!V3PlacementCriteria.IsStable(false, true, true, true, true,
            0f, 0f, 0f, 0.025f, 0.04f, 0.5f), "Un empujón contó como éxito.");
        // Lanzarlo sobre el target: velocidad excesiva.
        Require(!V3PlacementCriteria.IsStable(true, true, true, true, true,
            0f, 1f, 2f, 0.025f, 0.04f, 0.5f), "Un lanzamiento contó como éxito.");
        Require(!V3PlacementCriteria.IsStable(true, true, false, true, true,
            0f, 0f, 0f, 0.025f, 0.04f, 0.5f), "Colocación sin soltar contó como éxito.");
        Require(!V3PlacementCriteria.IsStable(true, true, true, true, false,
            0f, 0f, 0f, 0.025f, 0.04f, 0.5f), "Pinza cerrada contó como éxito.");
        Require(!V3PlacementCriteria.CanReleaseInTarget(true, true, true,
            0.01f, 0.04f, 0.10f, 0f, 0f, 0.025f, 0.04f, 0.5f),
            "Soltar desde altura contó como colocación.");
        Require(!V3PlacementCriteria.CanReleaseInTarget(true, true, true,
            0.01f, 0.04f, 0f, 1f, 0f, 0.025f, 0.04f, 0.5f),
            "Soltar a velocidad alta contó como colocación.");
        Require(V3PlacementCriteria.CanReleaseInTarget(true, true, true,
            0.01f, 0.04f, 0.005f, 0.005f, 0.01f, 0.025f, 0.04f, 0.5f),
            "Soltar despacio junto a la mesa no se acepta.");
        Require(V3PlacementCriteria.IsStable(true, true, true, true, true,
            0.005f, 0.005f, 0.01f, 0.025f, 0.04f, 0.5f),
            "Una colocación válida no satisface el criterio.");
        Debug.Log("V3PLACE: empujar=false, lanzar=false, sin soltar=false, pinza cerrada=false, estable=true.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    [MenuItem("Tools/PickPlaceRL/V3/Validar geometría y poses del target")]
    public static void ValidateGeometryAndReach()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PickPlaceV3.unity", OpenSceneMode.Single);
        TrainingArenaReferencesV3 arena = Object.FindFirstObjectByType<TrainingArenaReferencesV3>();
        Require(arena != null && arena.HasLocalReferences(), "Faltan referencias V3.");
        FrankaPickPlaceAgentV3 agent = arena.agent;
        Vector3 cubeLocal = arena.transform.InverseTransformPoint(arena.trainingCube.position);
        Vector3 targetLocal = arena.transform.InverseTransformPoint(arena.placeTarget.position);
        Vector3 targetDelta = targetLocal - cubeLocal;
        Require(Mathf.Abs(targetDelta.x) < 1e-4f && Mathf.Abs(targetDelta.z - 0.18f) < 1e-4f,
            "El target no está a +Z 0.18 m.");
        Require(arena.placeTarget.GetComponent<Collider>().isTrigger, "El target no es Trigger.");
        Bounds table = arena.tableCollider.bounds;
        Require(arena.placeTarget.position.x > table.min.x && arena.placeTarget.position.x < table.max.x &&
                arena.placeTarget.position.z > table.min.z && arena.placeTarget.position.z < table.max.z,
            "El target queda fuera de la mesa.");

        ArticulationBody root = agent.jointBodies[0].GetComponentsInParent<ArticulationBody>()
            .First(x => x.isRoot);
        var starts = new List<int>();
        root.GetDofStartIndices(starts);
        var positions = new List<float>();
        root.GetJointPositions(positions);
        var zeros = Enumerable.Repeat(0f, positions.Count).ToList();
        Vector3 nominal = root.transform.position;
        Quaternion rotation = root.transform.rotation;
        SimulationMode previous = Physics.simulationMode;
        float[] pick = {85.3f, 80.8f, 18.2f, -25.9f, -18.3f, 105.6f, 48.5f};
        float[] prePick = {85.7f, 75.1f, 19f, -22.7f, -18.3f, 98.9f, 48.5f};
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            ProbePose("cube-pick", pick, 0f, cubeLocal, root, agent, arena, starts, positions, zeros, nominal, rotation);
            Vector3 raisedTarget = targetLocal + Vector3.up * 0.17f;
            ProbePose("target-raised", prePick, -12f, raisedTarget, root, agent, arena, starts,
                positions, zeros, nominal, rotation);
            Vector3 placement = targetLocal + Vector3.up * 0.05f;
            ProbePose("target-place", pick, -12f, placement, root, agent, arena, starts,
                positions, zeros, nominal, rotation);
        }
        finally
        {
            Physics.simulationMode = previous;
        }
        Debug.Log("V3GEOMETRY: cubo y target dentro de la mesa; target +Z 0.18 m, Trigger; poses TCP aproximadas alcanzables.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void ProbePose(string name, float[] angles, float yawDelta, Vector3 localDestination,
        ArticulationBody root, FrankaPickPlaceAgentV3 agent, TrainingArenaReferencesV3 arena,
        List<int> starts, List<float> positions, List<float> zeros, Vector3 nominal, Quaternion rotation)
    {
        root.TeleportRoot(nominal, rotation);
        for (int i = 0; i < 7; i++)
        {
            ArticulationBody body = agent.jointBodies[i];
            float degrees = angles[i] + (i == 0 ? yawDelta : 0f);
            Require(degrees >= body.xDrive.lowerLimit && degrees <= body.xDrive.upperLimit,
                name + ": límite articular " + i);
            positions[starts[body.index]] = degrees * Mathf.Deg2Rad;
            ArticulationDrive drive = body.xDrive;
            drive.target = degrees;
            body.xDrive = drive;
        }
        root.SetJointPositions(positions);
        root.SetJointVelocities(zeros);
        root.linearVelocity = Vector3.zero;
        root.angularVelocity = Vector3.zero;
        for (int i = 0; i < 5; i++) Physics.Simulate(0.02f);
        foreach (ArticulationBody body in root.GetComponentsInChildren<ArticulationBody>())
            body.PublishTransform();
        Vector3 tcp = arena.transform.InverseTransformPoint(agent.tcpTransform.position);
        float error = Vector3.Distance(tcp, localDestination);
        Debug.Log("V3POSE " + name + " tcp_local=" + tcp.ToString("F4") +
            " destination_local=" + localDestination.ToString("F4") + " error_m=" +
            error.ToString("F4"));
        Require(error < 0.07f, name + ": TCP demasiado lejos del destino.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

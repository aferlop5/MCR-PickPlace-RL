using UnityEngine;

public class TrainingArenaReferencesV3 : MonoBehaviour
{
    [HideInInspector] public int arenaId;
    public FrankaPickPlaceAgentV3 agent;
    public Transform trainingCube;
    public Transform placeTarget;
    public Collider tableCollider;

    public bool HasLocalReferences()
    {
        if (agent == null || trainingCube == null || placeTarget == null || tableCollider == null ||
            agent.jointBodies == null || agent.jointBodies.Length != 7 ||
            agent.leftFinger == null || agent.rightFinger == null || agent.tcpTransform == null ||
            agent.cubeRigidbody == null || agent.cubeCollider == null ||
            agent.leftContact == null || agent.rightContact == null) return false;
        foreach (ArticulationBody body in agent.jointBodies)
            if (body == null || !body.transform.IsChildOf(transform)) return false;
        return agent.transform.IsChildOf(transform) && trainingCube.IsChildOf(transform) &&
               placeTarget.IsChildOf(transform) && tableCollider.transform.IsChildOf(transform) &&
               agent.leftFinger.transform.IsChildOf(transform) &&
               agent.rightFinger.transform.IsChildOf(transform) &&
               agent.tcpTransform.IsChildOf(transform) &&
               agent.leftContact.transform.IsChildOf(transform) &&
               agent.rightContact.transform.IsChildOf(transform) &&
               agent.arenaRoot == transform && agent.cubeTransform == trainingCube &&
               agent.cubeRigidbody.transform == trainingCube &&
               agent.cubeCollider.transform == trainingCube &&
               agent.placeTarget == placeTarget && agent.tableCollider == tableCollider &&
               agent.leftContact.cubeCollider == agent.cubeCollider &&
               agent.rightContact.cubeCollider == agent.cubeCollider;
    }

    public void ConfigureForTraining(int id)
    {
        arenaId = id;
        agent.arenaId = id;
        if (!agent.enabled) return;
        foreach (RobotPickSequence manual in GetComponentsInChildren<RobotPickSequence>(true))
            if (manual.enabled) manual.enabled = false;
        foreach (RobotJointController manual in GetComponentsInChildren<RobotJointController>(true))
            if (manual.enabled) manual.enabled = false;
        foreach (FrankaLiftAgent oldAgent in GetComponentsInChildren<FrankaLiftAgent>(true))
            if (oldAgent.enabled) oldAgent.enabled = false;
    }

    private void Awake()
    {
        if (!HasLocalReferences())
            Debug.LogError("TrainingArenaV3: referencias ausentes o externas a esta arena.", this);
    }
}

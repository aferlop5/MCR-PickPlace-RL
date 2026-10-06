using UnityEngine;

// Todas las referencias apuntan a objetos hijos de esta misma instancia.
public class TrainingArenaReferences : MonoBehaviour
{
    [HideInInspector] public int arenaId;
    public FrankaLiftAgent agent;
    public Transform trainingCube;
    public Collider tableCollider;

    public bool HasLocalReferences()
    {
        if (agent == null || trainingCube == null || tableCollider == null ||
            agent.jointBodies == null || agent.jointBodies.Length != 7 ||
            agent.leftFinger == null || agent.rightFinger == null || agent.tcpTransform == null ||
            agent.cubeRigidbody == null) return false;
        for (int i = 0; i < agent.jointBodies.Length; i++)
            if (agent.jointBodies[i] == null || !agent.jointBodies[i].transform.IsChildOf(transform))
                return false;
        return
               agent.transform.IsChildOf(transform) &&
               trainingCube.IsChildOf(transform) &&
               tableCollider.transform.IsChildOf(transform) &&
               agent.leftFinger.transform.IsChildOf(transform) &&
               agent.rightFinger.transform.IsChildOf(transform) &&
               agent.tcpTransform.IsChildOf(transform) &&
               agent.arenaRoot == transform &&
               agent.cubeTransform == trainingCube &&
               agent.cubeRigidbody.transform == trainingCube &&
               agent.tableCollider == tableCollider;
    }

    public void ConfigureForTraining(int id)
    {
        arenaId = id;
        agent.arenaId = id;
        if (!agent.enabled) return; // Permite activar la demostración manual cuando RL está desactivado.
        foreach (RobotPickSequence manual in GetComponentsInChildren<RobotPickSequence>(true))
            if (manual.enabled)
            {
                manual.enabled = false;
                Debug.LogWarning("TrainingArena: RobotPickSequence desactivado durante RL.", manual);
            }
        foreach (RobotJointController manual in GetComponentsInChildren<RobotJointController>(true))
            if (manual.enabled)
            {
                manual.enabled = false;
                Debug.LogWarning("TrainingArena: RobotJointController desactivado durante RL.", manual);
            }
    }

    private void Awake()
    {
        if (!HasLocalReferences())
            Debug.LogError("TrainingArena: hay referencias ausentes o externas a esta arena.", this);
    }
}

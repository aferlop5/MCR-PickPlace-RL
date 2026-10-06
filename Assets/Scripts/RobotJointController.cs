using UnityEngine;

public class RobotJointController : MonoBehaviour
{
    [Header("Articulaciones Principales (link1 a link7)")]
    public ArticulationBody[] jointBodies;

    [Header("Pinza")]
    public ArticulationBody leftFinger;
    public ArticulationBody rightFinger;

    [Header("Control Articulaciones (° grados)")]
    public float targetLink1 = 0f;
    public float targetLink2 = 0f;
    public float targetLink3 = 0f;
    public float targetLink4 = 0f;
    public float targetLink5 = 0f;
    public float targetLink6 = 0f;
    public float targetLink7 = 0f;

    [Header("Control Pinza")]
    [Range(0f, 1f)]
    [Tooltip("0 = Totalmente cerrado, 1 = Totalmente abierto")]
    public float aperturaPinza = 1f;

    private const float MAX_APERTURA_METROS = -0.04f;

    void Start()
    {
        // Leemos la posicion en la que ya esta colocado en la escena
        if (jointBodies != null)
        {
            if (jointBodies.Length > 0 && jointBodies[0] != null) targetLink1 = jointBodies[0].xDrive.target;
            if (jointBodies.Length > 1 && jointBodies[1] != null) targetLink2 = jointBodies[1].xDrive.target;
            if (jointBodies.Length > 2 && jointBodies[2] != null) targetLink3 = jointBodies[2].xDrive.target;
            if (jointBodies.Length > 3 && jointBodies[3] != null) targetLink4 = jointBodies[3].xDrive.target;
            if (jointBodies.Length > 4 && jointBodies[4] != null) targetLink5 = jointBodies[4].xDrive.target;
            if (jointBodies.Length > 5 && jointBodies[5] != null) targetLink6 = jointBodies[5].xDrive.target;
            if (jointBodies.Length > 6 && jointBodies[6] != null) targetLink7 = jointBodies[6].xDrive.target;
        }
    }

    void Update()
    {
        if (jointBodies != null)
        {
            if (jointBodies.Length > 0 && jointBodies[0] != null) SetDriveTarget(jointBodies[0], targetLink1);
            if (jointBodies.Length > 1 && jointBodies[1] != null) SetDriveTarget(jointBodies[1], targetLink2);
            if (jointBodies.Length > 2 && jointBodies[2] != null) SetDriveTarget(jointBodies[2], targetLink3);
            if (jointBodies.Length > 3 && jointBodies[3] != null) SetDriveTarget(jointBodies[3], targetLink4);
            if (jointBodies.Length > 4 && jointBodies[4] != null) SetDriveTarget(jointBodies[4], targetLink5);
            if (jointBodies.Length > 5 && jointBodies[5] != null) SetDriveTarget(jointBodies[5], targetLink6);
            if (jointBodies.Length > 6 && jointBodies[6] != null) SetDriveTarget(jointBodies[6], targetLink7);
        }

        if (leftFinger != null && rightFinger != null)
        {
            float targetDedo = aperturaPinza * MAX_APERTURA_METROS;
            SetDriveTarget(leftFinger, targetDedo);
            SetDriveTarget(rightFinger, targetDedo);
        }
    }

    private void SetDriveTarget(ArticulationBody body, float target)
    {
        var drive = body.xDrive;
        drive.target = target;
        body.xDrive = drive;
    }
}
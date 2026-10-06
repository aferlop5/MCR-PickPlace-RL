using System.Collections;
using UnityEngine;

public class RobotPickSequence : MonoBehaviour
{
    [Header("Articulaciones Principales (link1 a link7)")]
    public ArticulationBody[] jointBodies;

    [Header("Dedos de la Pinza")]
    public ArticulationBody leftFinger;
    public ArticulationBody rightFinger;

    [Header("Velocidad de movimiento")]
    [Tooltip("Grados por segundo para cada articulación")]
    public float jointSpeed = 35f;

    // Valores calibrados de apertura
    private const float GRIPPER_ABIERTO = -0.04f;
    private const float GRIPPER_CERRADO_CUBO = -0.014f; // Apertura del 35% donde sujeta firme el cubo

    // 1. Postura Home (Elevada de reposo)
    private readonly float[] poseHome = new float[] { 75f, 10f, 20f, -75f, 0f, 90f, 48.92f };

    // 2. Postura Pre-Pick (Alineado verticalmente justo encima del cubo)
    private readonly float[] posePrePick = new float[] { 85.7f, 75.1f, 19f, -22.7f, -18.3f, 98.9f, 48.5f };

    // 3. Postura Pick (Descendido abrazando el cubo)
    private readonly float[] posePick = new float[] { 85.3f, 80.8f, 18.2f, -25.9f, -18.3f, 105.6f, 48.5f };

    void Start()
    {
        StartCoroutine(CicloCompletoPickAndPlace());
    }

    IEnumerator CicloCompletoPickAndPlace()
    {
        // 1. Abrir pinza e ir a postura Home
        SetGripper(GRIPPER_ABIERTO);
        yield return MoverArticulaciones(poseHome);
        yield return new WaitForSeconds(0.5f);

        // 2. Mover a Pre-Pick (encima del cubo)
        yield return MoverArticulaciones(posePrePick);
        yield return new WaitForSeconds(0.2f);

        // 3. Descender suavemente a Pick (abrazando el cubo)
        yield return MoverArticulaciones(posePick);
        yield return new WaitForSeconds(0.5f);

        // 4. Cerrar la pinza sobre el cubo
        SetGripper(GRIPPER_CERRADO_CUBO);
        yield return new WaitForSeconds(0.8f); // Pausa para asegurar el agarre físico

        // 5. Retirada vertical al Pre-Pick levantando el cubo de la mesa
        yield return MoverArticulaciones(posePrePick);
        yield return new WaitForSeconds(0.2f);

        // 6. Subir a Home con el cubo sujeto en el aire
        yield return MoverArticulaciones(poseHome);
    }

    IEnumerator MoverArticulaciones(float[] targets)
    {
        bool enPosicion = false;
        while (!enPosicion)
        {
            enPosicion = true;
            for (int i = 0; i < jointBodies.Length; i++)
            {
                if (jointBodies[i] == null) continue;

                var drive = jointBodies[i].xDrive;
                float actual = drive.target;
                float deseado = targets[i];

                float nuevoValor = Mathf.MoveTowards(actual, deseado, jointSpeed * Time.deltaTime);
                drive.target = nuevoValor;
                jointBodies[i].xDrive = drive;

                if (Mathf.Abs(deseado - nuevoValor) > 0.5f)
                {
                    enPosicion = false;
                }
            }
            yield return null;
        }
    }

    void SetGripper(float targetMetros)
    {
        if (leftFinger != null)
        {
            var dLeft = leftFinger.xDrive;
            dLeft.target = targetMetros;
            leftFinger.xDrive = dLeft;
        }

        if (rightFinger != null)
        {
            var dRight = rightFinger.xDrive;
            dRight.target = targetMetros;
            rightFinger.xDrive = dRight;
        }
    }
}
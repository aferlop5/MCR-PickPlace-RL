using UnityEngine;

// Se coloca en el ArticulationBody de cada dedo; no cambia colliders ni fuerzas.
public class FrankaFingerContactV3 : MonoBehaviour
{
    public Collider cubeCollider;
    private float lastCubeContactTime = float.NegativeInfinity;

    public bool TouchingCube
    {
        get { return Time.fixedTime - lastCubeContactTime <= 2f * Time.fixedDeltaTime; }
    }

    private void OnCollisionEnter(Collision collision) { Record(collision); }
    private void OnCollisionStay(Collision collision) { Record(collision); }

    private void OnCollisionExit(Collision collision)
    {
        if (IsCube(collision)) lastCubeContactTime = float.NegativeInfinity;
    }

    private void Record(Collision collision)
    {
        if (IsCube(collision)) lastCubeContactTime = Time.fixedTime;
    }

    private bool IsCube(Collision collision)
    {
        return cubeCollider != null && collision.collider != null &&
               (collision.collider == cubeCollider ||
                collision.collider.transform.IsChildOf(cubeCollider.transform));
    }

    public void Clear() { lastCubeContactTime = float.NegativeInfinity; }
}

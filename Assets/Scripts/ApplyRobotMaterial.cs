using UnityEngine;

public class ApplyRobotMaterial : MonoBehaviour
{
    public Material robotMaterial;

    [ContextMenu("Apply Material To Robot")]
    public void ApplyMaterial()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer rend in renderers)
        {
            Material[] materials = new Material[rend.sharedMaterials.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = robotMaterial;
            }

            rend.sharedMaterials = materials;
        }

        Debug.Log("Material aplicado a " + renderers.Length + " renderers.");
    }
}
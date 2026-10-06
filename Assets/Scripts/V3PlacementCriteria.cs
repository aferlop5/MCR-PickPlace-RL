using UnityEngine;

// Función pura: el éxito necesita una historia válida y una colocación estable.
public static class V3PlacementCriteria
{
    public static bool CanReleaseInTarget(bool enteredTarget, bool wasClosed, bool nowOpen,
        float horizontalDistance, float targetRadius, float heightError, float linearSpeed,
        float angularSpeed, float heightTolerance, float maxLinearSpeed, float maxAngularSpeed)
    {
        return enteredTarget && wasClosed && nowOpen && horizontalDistance <= targetRadius &&
               Mathf.Abs(heightError) <= heightTolerance &&
               linearSpeed <= maxLinearSpeed && angularSpeed <= maxAngularSpeed;
    }

    public static bool IsStable(bool wasLifted, bool enteredTarget, bool releasedInTarget,
        bool insideTarget, bool gripperOpen, float heightError, float linearSpeed,
        float angularSpeed, float heightTolerance, float maxLinearSpeed, float maxAngularSpeed)
    {
        return wasLifted && enteredTarget && releasedInTarget && insideTarget && gripperOpen &&
               Mathf.Abs(heightError) <= heightTolerance &&
               linearSpeed <= maxLinearSpeed && angularSpeed <= maxAngularSpeed;
    }
}

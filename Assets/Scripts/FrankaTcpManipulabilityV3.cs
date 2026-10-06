using System;
using UnityEngine;

// Jacobiano geométrico espacial 6x7 del TCP. Cada articulación es revoluta alrededor
// del eje X de su anclaje: Jv = a x (p_tcp - p_anclaje), Jw = a.
// Las filas lineales se dividen por 0.5 m para obtener una medida adimensional.
public sealed class FrankaTcpManipulabilityV3
{
    private const int Rows = 6;
    private const int Columns = 7;
    private const double CharacteristicLengthMeters = 0.5;
    private const double Ridge = 1e-8;
    private readonly ArticulationBody[] joints;
    private readonly Transform tcp;
    private readonly double[] jacobian = new double[Rows * Columns];
    private readonly double[] gram = new double[Rows * Rows];
    private readonly double[] cholesky = new double[Rows * Rows];

    public FrankaTcpManipulabilityV3(ArticulationBody[] joints, Transform tcp)
    {
        this.joints = joints;
        this.tcp = tcp;
    }

    public float Measure()
    {
        Vector3 tip = tcp.position;
        for (int column = 0; column < Columns; column++)
        {
            ArticulationBody joint = joints[column];
            Transform link = joint.transform;
            Vector3 origin = link.TransformPoint(joint.anchorPosition);
            Vector3 axis = (link.rotation * joint.anchorRotation) * Vector3.right;
            Vector3 linear = Vector3.Cross(axis, tip - origin);
            jacobian[column] = linear.x / CharacteristicLengthMeters;
            jacobian[Columns + column] = linear.y / CharacteristicLengthMeters;
            jacobian[2 * Columns + column] = linear.z / CharacteristicLengthMeters;
            jacobian[3 * Columns + column] = axis.x;
            jacobian[4 * Columns + column] = axis.y;
            jacobian[5 * Columns + column] = axis.z;
        }

        // G = J J^T + lambda I. Cholesky evita calcular un determinante directo
        // y conserva precisión cuando dos ejes se vuelven casi dependientes.
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column <= row; column++)
            {
                double value = row == column ? Ridge : 0.0;
                for (int joint = 0; joint < Columns; joint++)
                    value += jacobian[row * Columns + joint] * jacobian[column * Columns + joint];
                gram[row * Rows + column] = value;
            }
        }

        double logManipulability = 0.0;
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column <= row; column++)
            {
                double value = gram[row * Rows + column];
                for (int previous = 0; previous < column; previous++)
                    value -= cholesky[row * Rows + previous] *
                             cholesky[column * Rows + previous];
                if (row == column)
                {
                    double diagonal = Math.Sqrt(Math.Max(value, Ridge * 1e-6));
                    cholesky[row * Rows + column] = diagonal;
                    logManipulability += Math.Log(diagonal);
                }
                else
                {
                    cholesky[row * Rows + column] = value /
                                                     cholesky[column * Rows + column];
                }
            }
        }
        double result = Math.Exp(logManipulability);
        return double.IsNaN(result) || double.IsInfinity(result)
            ? float.NaN : (float)result;
    }
}

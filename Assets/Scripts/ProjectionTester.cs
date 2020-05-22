using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProjectionTester : MonoBehaviour
{

    public Spline spline;

    private void OnDrawGizmosSelected()
    {
        if (spline == null) return;

        var position = transform.position;

        var splinePoint = spline.ProjectOnSpline(position);
        var projectedPosition = splinePoint.position;

        Gizmos.DrawLine(position, projectedPosition); 
    }

}

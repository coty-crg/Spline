using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CorgiSpline
{
    public class ProjectionTester : MonoBehaviour
    {
        [Header("Settings")]
        public Spline spline;
        public bool AlwaysDraw;

        [Header("Results")]
        public float ResultPercentage;


        private void OnDrawGizmosSelected()
        {
            if (!AlwaysDraw) DrawGizmos();
        }

        private void OnDrawGizmos()
        {
            if (AlwaysDraw) DrawGizmos();
        }

        private void DrawGizmos()
        {
            if (spline == null) return;

            var position = transform.position;

            ResultPercentage = spline.ProjectOnSpline_t(position);

            var splinePoint = spline.GetPoint(ResultPercentage);
            var projectedPosition = splinePoint.position;
            var distance = Vector3.Distance(projectedPosition, position);

            Gizmos.color = Color.green * 0.25f;
            Gizmos.DrawSphere(transform.position, distance);

            Gizmos.color = new Color(0f, 1f, 1f);
            Gizmos.DrawLine(position, projectedPosition);


        }
    }
}

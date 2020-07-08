using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CorgiSpline
{
    public class TransformFollowSpline : MonoBehaviour
    {
        public Spline FollowSpline;
        public float FollowSpeed = 1f;
        public bool FollowRotation;

        private void Update()
        {
            if (FollowSpline == null)
            {
                return;
            }

            var t = FollowSpline.ProjectOnSpline_t(transform.position);

            var projectedForward = FollowSpline.GetForward(t);
            var projectedPoint = FollowSpline.GetPoint(t);
            projectedPoint.position += projectedForward * (FollowSpeed * Time.deltaTime);

            transform.position = projectedPoint.position;

            if (FollowRotation)
            {
                var up = projectedPoint.rotation * Vector3.forward;
                transform.rotation = Quaternion.LookRotation(projectedForward, up);
            }
        }
    }
}

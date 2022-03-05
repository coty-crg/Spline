using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CorgiSpline
{
    public class TransformFollowSpline : MonoBehaviour
    {
        [Header("References")]
        public Spline FollowSpline;

        [Header("Settings")]
        public float FollowSpeed = 1f;
        public bool FollowRotation;
        public bool UpdateSplineEveryFrame;

        [System.NonSerialized] private float d;
        [System.NonSerialized] private float _splineLength;

        private void Start()
        {
            d = 0f;
            _splineLength = FollowSpline.UpdateDistanceProjectionsData(); 
        }

        private void Update()
        {
            if (FollowSpline == null)
            {
                return;
            }

            if (UpdateSplineEveryFrame)
            {
                _splineLength = FollowSpline.UpdateDistanceProjectionsData();
            }

            d += FollowSpeed * Time.deltaTime;
            d = Mathf.Repeat(d, _splineLength);

            var t = FollowSpline.ProjectDistance(d);
            var projectedPoint = FollowSpline.GetPoint(t);

            transform.position = projectedPoint.position;

            if (FollowRotation)
            {
                transform.rotation = transform.rotation;
            }
        }
    }
}

//#define CORGI_DETECTED_DOTWEEN // no way to automatically detect this?! 

namespace CorgiSpline
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

#if CORGI_DETECTED_DOTWEEN
    using DG.Tweening;
#endif

    public static class SplineExtensions
    {
#if CORGI_DETECTED_DOTWEEN

        /// <summary>
        /// Creates a tween to move a transform along a spline over a given duration. 
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="spline"></param>
        /// <param name="duration"></param>
        /// <returns></returns>
        public static Tweener DoFollowSplinePercent(this Transform transform, Spline spline, float duration)
        {
            var t = 0f;
            return DOTween.To(() => t, x => t = x, 1f, duration).OnUpdate(() =>
            {
                if (transform != null && spline != null)
                {
                    var splinePoint = spline.GetPoint(t);
                    transform.position = splinePoint.position;
                }
            });
        }

        /// <summary>
        /// Creates a tween to move a transform along a spline at a consistent speed. 
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="spline"></param>
        /// <param name="speed"></param>
        /// <returns></returns>
        public static Tweener DoFollowSplineConsistent(this Transform transform, Spline spline, float speed)
        {
            var totalDistance = spline.UpdateDistanceProjectionsData();
            var duration = totalDistance / Mathf.Max(0.01f, speed);

            var d = 0f;
            return DOTween.To(() => d, x => d = x, totalDistance, duration).OnUpdate(() =>
            {
                if (transform != null && spline != null)
                {
                    var t = spline.ProjectDistance(d); 
                    var splinePoint = spline.GetPoint(t);
                    transform.position = splinePoint.position;
                }
            });
        }
#endif
    }
}
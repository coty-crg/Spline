// #define CORGI_DETECTED_DOTWEEN // no way to automatically detect this?! 

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
#endif
    }
}
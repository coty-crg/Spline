using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System;
using System.Text.RegularExpressions;

[System.Serializable]
public struct SplinePoint
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public SplinePoint(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        this.position = position;
        this.rotation = rotation;
        this.scale = scale; 
    }

    public override bool Equals(object obj)
    {
        var otherPoint = (SplinePoint)obj;
        var matchPosition = (position - otherPoint.position).sqrMagnitude < 0.001f;
        var matchUp = (rotation.eulerAngles - otherPoint.rotation.eulerAngles).sqrMagnitude < 0.001f;
        return matchPosition && matchUp;
    }

    public override int GetHashCode()
    {
        int hashCode = -1959666502;
        hashCode = hashCode * -1521134295 + position.GetHashCode();
        hashCode = hashCode * -1521134295 + rotation.GetHashCode();
        return hashCode;
    }
}

public enum SplineMode
{
    Linear,
    Bezier,
}

public class Spline : MonoBehaviour
{
    public SplinePoint[] Points;
    public SplineMode Mode;

    public bool EditorAlwaysDraw;

    public SplinePoint ProjectOnSpline(Vector3 position)
    {
        var interpolatedPoint = new SplinePoint(position, Quaternion.identity, Vector3.one);

        if(Points.Length == 0)
        {
            return interpolatedPoint;
        }

        if (Points.Length == 1)
        {
            return Points[0];
        }

        var length = Points.Length;

        if(Mode == SplineMode.Linear)
        {
            // find closest point 
            var closestDistance = float.MaxValue;
            var closestIndex = -1;

            for (var i = 0; i < length; ++i)
            {
                var point = Points[i];

                var toPoint = point.position - position;
                var toPointDistance = toPoint.magnitude;
                if (toPointDistance < closestDistance)
                {
                    closestDistance = toPointDistance;
                    closestIndex = i;
                }
            }

            SplinePoint point0;
            SplinePoint point1;

            if (closestIndex <= 0)
            {
                var index_a = closestIndex;
                var index_b = closestIndex + 1;

                point0 = Points[index_a];
                point1 = Points[index_b];
            }

            else if (closestIndex == Points.Length - 1)
            {
                var index_a = closestIndex;
                var index_b = closestIndex - 1;

                point0 = Points[index_a];
                point1 = Points[index_b];
            }

            else
            {
                var index_a = closestIndex;
                var index_b = closestIndex - 1;
                var index_c = closestIndex + 1;

                var point_a = Points[index_a];
                var point_b = Points[index_b];
                var point_c = Points[index_c];

                var projected_ab = ProjectLinear(point_a, point_b, position);
                var projected_ac = ProjectLinear(point_a, point_c, position);

                var distance_ab = Vector3.Distance(position, projected_ab);
                var distance_ac = Vector3.Distance(position, projected_ac);

                if (distance_ab < distance_ac)
                {
                    point0 = point_a;
                    point1 = point_b;
                }
                else
                {
                    point0 = point_a;
                    point1 = point_c;
                }
            }

            var projectedPosition = ProjectLinear(point0, point1, position);
            var percentageBetweenPoints = GetPercentageLinear(point0, point1, projectedPosition);
            var projectedUp = InterpolateRotation(point0, point1, Mode, percentageBetweenPoints);

            interpolatedPoint.position = projectedPosition;
            interpolatedPoint.rotation = projectedUp;

            return interpolatedPoint;
        }
        else
        {



            // find closest point 
            var closestDistance = float.MaxValue;
            var closestProjectedPosition = Vector3.zero;
            
            for (var i = 0; i < length - 3; i += 3)
            {
                var p0 = Points[i + 0];
                var p1 = Points[i + 1];
                var p2 = Points[i + 2];
                var p3 = Points[i + 3];
            
                var t = QuadraticProject(p0.position, p1.position, p2.position, p3.position, position);
                var projected = QuadraticInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                var distance = Vector3.Distance(projected, position); 
            
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestProjectedPosition = projected; 
                }
            }
            
            
            interpolatedPoint.position = closestProjectedPosition;
            return interpolatedPoint;









            // find closest point 
            // var closestDistance = float.MaxValue;
            // var closestIndex = -1;
            // 
            // for (var i = 0; i < length - 3; i += 3)
            // {
            //     var point = Points[i];
            // 
            //     var toPoint = point.position - position;
            //     var toPointDistance = toPoint.magnitude;
            //     if (toPointDistance < closestDistance)
            //     {
            //         closestDistance = toPointDistance;
            //         closestIndex = i;
            //     }
            // }
            // 
            // if(closestIndex == -1)
            // {
            //     return interpolatedPoint;
            // }
            // 
            // var index0 = closestIndex;
            // var q0_point0 = Points[index0 + 0];
            // var q0_point1 = Points[index0 + 1];
            // var q0_point2 = Points[index0 + 2];
            // var q0_point3 = Points[index0 + 3];
            // 
            // var t0 = QuadraticProject(q0_point0.position, q0_point1.position, q0_point2.position, q0_point3.position, position);
            // var projectedPosition0 = QuadraticInterpolate(q0_point0.position, q0_point1.position, q0_point2.position, q0_point3.position, t0);
            // 
            // var bestProjectedPosition = projectedPosition0;
            // 
            // if (index0 != 0)
            // {
            //     var index1 = closestIndex - 3;
            //     var q1_point0 = Points[index1 + 0];
            //     var q1_point1 = Points[index1 + 1];
            //     var q1_point2 = Points[index1 + 2];
            //     var q1_point3 = Points[index1 + 3];
            // 
            //     var t1 = QuadraticProject(q1_point0.position, q1_point1.position, q1_point2.position, q1_point3.position, position);
            //     var projectedPosition1 = QuadraticInterpolate(q1_point0.position, q1_point1.position, q1_point2.position, q1_point3.position, t1);
            // 
            // 
            //     var distance0 = (projectedPosition0 - position).magnitude;
            //     var distance1 = (projectedPosition1 - position).magnitude;
            // 
            //     bestProjectedPosition = distance0 < distance1 ? projectedPosition0 : projectedPosition1;
            // }
            // 
            // interpolatedPoint.position = bestProjectedPosition;
            // interpolatedPoint.up = Vector3.up;
            // 
            // return interpolatedPoint;
        }
    }

    public static Vector3 QuadraticInterpolate(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, float t)
    {
        var oneMinusT = 1f - t;
        var result = 
            oneMinusT * oneMinusT * oneMinusT * point0 +
            3f * oneMinusT * oneMinusT * t * point1 +
            3f * oneMinusT * t * t * point2 +
            t * t * t * point3;

        return result;

        // var ab = Vector3.Lerp(point0, point1, t);
        // var bc = Vector3.Lerp(point1, point2, t);
        // var cd = Vector3.Lerp(point2, point3, t);
        // 
        // var ac = Vector3.Lerp(ab, bc, t);
        // var bd = Vector3.Lerp(bc, cd, t);
        // 
        // var ad = Vector3.Lerp(ac, bd, t);
        // 
        // return ad; 
    }

    public static Quaternion QuadraticInterpolate(Quaternion point0, Quaternion point1, Quaternion point2, Quaternion point3, float t)
    {
        // var oneMinusT = 1f - t;
        // var result =
        //     oneMinusT * oneMinusT * oneMinusT * point0 +
        //     3f * oneMinusT * oneMinusT * t * point1 +
        //     3f * oneMinusT * t * t * point2 +
        //     t * t * t * point3;
        // 
        // return result;

        var ab = Quaternion.Slerp(point0, point1, t);
        var bc = Quaternion.Slerp(point1, point2, t);
        var cd = Quaternion.Slerp(point2, point3, t);

        var ac = Quaternion.Slerp(ab, bc, t);
        var bd = Quaternion.Slerp(bc, cd, t);

        var ad = Quaternion.Slerp(ac, bd, t);
        
        return ad; 
    }

    public static SplinePoint CalculateBezierPoint(SplinePoint point0, SplinePoint point1, SplinePoint point2, SplinePoint point3, float t)
    {
        var result = new SplinePoint();

        // var pos_ab = Vector3.Lerp(point0.position, point1.position, t);
        // var pos_bc = Vector3.Lerp(point1.position, point2.position, t);
        // var pos_cd = Vector3.Lerp(point2.position, point3.position, t);
        // var pos_ac = Vector3.Lerp(pos_ab, pos_bc, t);
        // var pos_bd = Vector3.Lerp(pos_bc, pos_cd, t);
        // var pos_ad = Vector3.Lerp(pos_ac, pos_bd, t);

        result.position = QuadraticInterpolate(point0.position, point1.position, point2.position, point3.position, t);

        // var up_ab = Vector3.Slerp(point0.up, point1.up, t);
        // var up_bc = Vector3.Slerp(point1.up, point2.up, t);
        // var up_cd = Vector3.Slerp(point2.up, point3.up, t);
        // var up_ac = Vector3.Slerp(up_ab, up_bc, t);
        // var up_bd = Vector3.Slerp(up_bc, up_cd, t);
        // var up_ad = Vector3.Slerp(up_ac, up_bd, t);

        result.rotation = QuadraticInterpolate(point0.rotation, point1.rotation, point2.rotation, point3.rotation, t).normalized;

        return result;
    }

    public static float QuadraticProject(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, Vector3 projectPoint)
    {

        
        // var smallestDistance = float.MaxValue;
        // var smallest_t = 0f;
        // 
        // var max_test_steps = 256;
        // 
        // for(var step = 0; step < max_test_steps; ++step)
        // {
        //     var test_t = (float)step / max_test_steps;
        // 
        //     var test_point = QuadraticInterpolate(point0, point1, point2, point3, test_t);
        // 
        //     var sqrDistance = (test_point - projectPoint).sqrMagnitude;
        //     if(sqrDistance < smallestDistance)
        //     {
        //         smallestDistance = sqrDistance;
        //         smallest_t = test_t; 
        //     }
        // }

        // return smallest_t;

        var max_steps = 128;
        var i = 0;
        
        var t = 0.5f;
        var delta = 1f;
        
        var threshold = 0.00001f;
        
        while(i < max_steps)
        {
            var t0 = t - delta;
            var t1 = t + delta;

            t0 = Mathf.Clamp01(t0);
            t1 = Mathf.Clamp01(t1);

            var p0 = QuadraticInterpolate(point0, point1, point2, point3, (float) t0);
            var p1 = QuadraticInterpolate(point0, point1, point2, point3, (float) t1);
            
            var d0 = (p0 - projectPoint).sqrMagnitude;
            var d1 = (p1 - projectPoint).sqrMagnitude;
            
            if(d0 < d1)
            {
                t = t0;
            }
            else
            {
                t = t1;
            }
            
            i += 1;
            delta *= 0.60f;
        
            if(delta < threshold)
            {
                break; 
            }
        }

        // Debug.Log($"{i} / {max_steps}");
        
        return Mathf.Clamp01(t); 

        // var delta = 1f / max_steps;
        // for(var t = 0f; t < 1f; t += delta)
        // {
        // 
        // }

        // var t = (3 * point2 - 2 * point3 - projectPoint).magnitude / (3 * (point2 - point3)).magnitude;
        // 
        // 
        // 
        // 
        // var t_x = 1f / (point0 - 3 * point1 + 3 * point2 - point3).magnitude; 

        // var t_y = 1f / (point0.y - 3 * point1.y + 3 * point2.y - point3.y); 
        // var t_z = 1f / (point0.z - 3 * point1.z + 3 * point2.z - point3.z);
        // 
        // var p_x = QuadraticInterpolate(point0, point1, point2, point3, t_x);
        // var p_y = QuadraticInterpolate(point0, point1, point2, point3, t_y);
        // var p_z = QuadraticInterpolate(point0, point1, point2, point3, t_z);
        // 
        // var distance_x = Vector3.Distance(p_x, projectPoint);
        // var distance_y = Vector3.Distance(p_y, projectPoint);
        // var distance_z = Vector3.Distance(p_z, projectPoint);
        // 
        // if (distance_x < distance_y && distance_x < distance_z)
        //     return t_x;
        // else if (distance_y < distance_x && distance_y < distance_z)
        //     return t_y;
        // else if (distance_z < distance_y && distance_z < distance_x)
        //     return t_z;
        // else 

        //    return t_x; 
    }

    public static Vector3 InterpolatePosition(SplinePoint a, SplinePoint b, SplineMode mode, float t)
    {
        switch(mode)
        {
            default:
            case SplineMode.Linear: 
                return Vector3.Lerp(a.position, b.position, t);
        }
    }

    public static Quaternion InterpolateRotation(SplinePoint a, SplinePoint b, SplineMode mode, float t)
    {
        switch (mode)
        {
            default:
            case SplineMode.Linear:
                return Quaternion.Slerp(a.rotation, b.rotation, t);
        }
    }

    public static Vector3 ProjectLinear(SplinePoint a, SplinePoint b, Vector3 point)
    {
        var direction = b.position - a.position;
        var toPoint = point - a.position;

        var dot = Vector3.Dot(direction, toPoint);
        if (dot < 0) return a.position;

        var projected = Vector3.Project(point - a.position, direction) + a.position; 
        return projected;
    }

    public SplinePoint GetPoint(float t)
    {
        if(Points.Length == 0)
        {
            return new SplinePoint(); 
        }

        t = Mathf.Clamp01(t); 

        if (Mode == SplineMode.Linear)
        {
            var delta_t = 1f / Points.Length;
            var mod_t = Mathf.Repeat(t, delta_t);
            var inner_t = mod_t / delta_t;

            var index0 = Mathf.FloorToInt(t * Points.Length);
            var index1 = index0 + 1;

            index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
            index1 = Mathf.Clamp(index1, 0, Points.Length - 1);

            if (index0 == Points.Length - 1)
            {
                return Points[index0];
            }

            var point0 = Points[index0];
            var point1 = Points[index1];

            var result = new SplinePoint();
            result.position = Vector3.Lerp(point0.position, point1.position, inner_t);
            result.rotation = Quaternion.Slerp(point0.rotation, point1.rotation, inner_t);

            return result; 
        }
        else if (Mode == SplineMode.Bezier)
        {

            var delta_t = 3f / Points.Length;
            var mod_t = Mathf.Repeat(t, delta_t);
            var inner_t = mod_t / delta_t;


            var index0 = Mathf.FloorToInt(t * Points.Length);
                index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                index0 = index0 - index0 % 3;

            var index1 = index0 + 1;
            var index2 = index0 + 2;
            var index3 = index0 + 3;

            // index1 = Mathf.Clamp(index1, 0, Points.Length - 1);
            // index2 = Mathf.Clamp(index2, 0, Points.Length - 1);
            // index3 = Mathf.Clamp(index3, 0, Points.Length - 1);

            if (index0 > Points.Length - 4)
            {
                return Points[Points.Length - 1];
            }

            var point0 = Points[index0];
            var point1 = Points[index1];
            var point2 = Points[index2];
            var point3 = Points[index3];

            var result = CalculateBezierPoint(point0, point1, point2, point3, inner_t);
            return result; 
        }

        // not implemented 
        else
        {
            return new SplinePoint(); 
        }
    }

    public Vector3 GetForward(float t)
    {
        var delta_t = 1f / 256f;

        var p0 = GetPoint(t - delta_t * 1);
        var p1 = GetPoint(t + delta_t * 1);

        var forward = (p1.position - p0.position).normalized;
        return forward;
    }

    public static float GetPercentageLinear(SplinePoint a, SplinePoint b, Vector3 point)
    {
        var betweenAB = b.position - a.position;
        var distanceAB = betweenAB.magnitude;

        var toPoint = point - a.position;
        var distanceToPoint = toPoint.magnitude;

        var percentage = distanceToPoint / distanceAB;
        return percentage;
    }

#if UNITY_EDITOR
    public void OnDrawGizmosSelected()
    {
        if (EditorAlwaysDraw) return;
        DrawGizmos();
    }

    private void OnDrawGizmos()
    {
        if (!EditorAlwaysDraw) return;
        DrawGizmos();
    }

    private void DrawGizmos()
    {
        if (Mode == SplineMode.Linear)
        {
            for (var i = 1; i < Points.Length; ++i)
            {
                var previous = Points[i - 1];
                var current = Points[i];

                Gizmos.DrawLine(previous.position, current.position);
            }
        }
        else
        {
            int quality = 512;

            for (var r = 0; r <= quality; ++r)
            {

                var t0 = (float)r / quality - (1f / quality);
                var t1 = (float)r / quality;

                var p0 = GetPoint(t0);
                var p1 = GetPoint(t1);

                Gizmos.DrawLine(p0.position, p1.position);
            }
        }
    }
#endif

}

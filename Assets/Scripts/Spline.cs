using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(Spline))]
public class SplineEditor : Editor
{
    public int SelectedPoint = -1;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var instance = (Spline) target;

        if(GUILayout.Button("Add Point"))
        {
            Undo.RegisterCompleteObjectUndo(instance, "Add Point");

            var newArray = new SplinePoint[instance.Points.Length + 1];
            for(var i = 0; i < instance.Points.Length; ++i)
            {
                newArray[i] = instance.Points[i];
            }

            instance.Points = newArray;
            instance.Points[instance.Points.Length - 1] = new SplinePoint(instance.transform.position + Vector3.up, Vector3.up);

            EditorUtility.SetDirty(instance);
        }


        var optionsStr = new string[instance.Points.Length + 1];
        var optionsInt = new int [instance.Points.Length + 1];

        optionsStr[0] = "none";
        optionsInt[0] = -1;

        for (var i = 0; i < instance.Points.Length; ++i)
        {
            optionsStr[i + 1] = $"point {i:N0}";
            optionsInt[i + 1] = i;
        }

        SelectedPoint = EditorGUILayout.IntPopup(SelectedPoint, optionsStr, optionsInt);

    }

    private void OnSceneGUI()
    {
        if(SelectedPoint >= 0)
        {
            var instance = (Spline)target;

            var splinePoint = instance.Points[SelectedPoint];

            var startPosition = splinePoint.position;
            splinePoint.position = Handles.PositionHandle(startPosition, Quaternion.identity);

            if((startPosition - splinePoint.position).sqrMagnitude > 0)
            {
                Undo.RegisterCompleteObjectUndo(instance, "Move Point");
                instance.Points[SelectedPoint] = splinePoint;
            }
        }
    }
}

#endif

[System.Serializable]
public struct SplinePoint
{
    public Vector3 position;
    public Vector3 up;

    public SplinePoint(Vector3 position, Vector3 up)
    {
        this.position = position;
        this.up = up;
    }
}

public enum SplineMode
{
    Linear,
}

public class Spline : MonoBehaviour
{
    public SplinePoint[] Points;
    public SplineMode Mode;

    public bool EditorAlwaysDraw;

    public SplinePoint ProjectOnSpline(Vector3 position)
    {
        var interpolatedPoint = new SplinePoint(position, new Vector3(0, 1, 0));

        if(Points.Length == 0)
        {
            return interpolatedPoint;
        }

        if (Points.Length == 1)
        {
            return Points[0];
        }

        var length = Points.Length;

        // find closest point 
        var closestDistance = float.MaxValue;
        var closestIndex = -1;

        for(var i = 0; i < length; ++i)
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

        if(closestIndex <= 0)
        {
            var index_a = closestIndex;
            var index_b = closestIndex + 1;

            point0 = Points[index_a];
            point1 = Points[index_b];
        }

        else if(closestIndex == Points.Length - 1)
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

            var projected_ab = Project(point_a, point_b, Mode, position);
            var projected_ac = Project(point_a, point_c, Mode, position);

            var distance_ab = Vector3.Distance(position, projected_ab);
            var distance_ac = Vector3.Distance(position, projected_ac);

            if(distance_ab < distance_ac)
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

        var projectedPosition = Project(point0, point1, Mode, position);
        var percentageBetweenPoints = GetPercentage(point0, point1, Mode, projectedPosition);
        var projectedUp = InterpolateUp(point0, point1, Mode, percentageBetweenPoints);

        interpolatedPoint.position = projectedPosition;
        interpolatedPoint.up = projectedUp;

        return interpolatedPoint;
    }

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
            int quality = 4;

            for (var i = 1; i < Points.Length; ++i)
            {
                var previous = Points[i - 1];
                var current = Points[i];

                for (var r = 0; r < quality; ++r)
                {

                    var t0 = (float)r / quality - (1f / quality);
                    var t1 = (float)r / quality;

                    var p0 = InterpolatePosition(previous, current, Mode, t0);
                    var p1 = InterpolatePosition(previous, current, Mode, t1);

                    Gizmos.DrawLine(p0, p1);
                }
            }
        }
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

    public static Vector3 InterpolateUp(SplinePoint a, SplinePoint b, SplineMode mode, float t)
    {
        switch (mode)
        {
            default:
            case SplineMode.Linear:
                return Vector3.Slerp(a.up, b.up, t);
        }
    }

    public static Vector3 Project(SplinePoint a, SplinePoint b, SplineMode mode, Vector3 point)
    {
        switch (mode)
        {
            default:
            case SplineMode.Linear:

                var direction = b.position - a.position;
                var toPoint = point - a.position;

                var dot = Vector3.Dot(direction, toPoint);
                if (dot < 0) return a.position;

                var projected = Vector3.Project(point - a.position, direction) + a.position; 
                return projected;
        }
    }

    public static float GetPercentage(SplinePoint a, SplinePoint b, SplineMode mode, Vector3 point)
    {
        switch (mode)
        {
            default:
            case SplineMode.Linear:
                var betweenAB = b.position - a.position;
                var distanceAB = betweenAB.magnitude;

                var toPoint = point - a.position;
                var distanceToPoint = toPoint.magnitude;

                var percentage = distanceToPoint / distanceAB;
                return percentage;
        }
    }
}

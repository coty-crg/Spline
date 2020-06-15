
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(Spline))]
public class SplineEditor : Editor
{
    public enum SplinePlacePointMode
    {
        CameraPlane,
        Plane,
        MeshSurface,
        CollisionSurface,
    }

    public int SelectedPoint = -1;
    public bool MirrorAnchors = true;

    private bool PlacingPoint;
    public SplinePlacePointMode PlaceMode;
    public LayerMask PlaceLayerMask;
    public Vector3 PlacePlaneOffset;
    public Vector3 PlacePlaneNormal;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var instance = (Spline)target;

        if (GUILayout.Button("Append Point"))
        {
            var lastPos = instance.GetPoint(1f);
            AppendPoint(instance, lastPos.position, lastPos.up);
        }


        if (!PlacingPoint && GUILayout.Button("Start Placing Points"))
        {
            PlacingPoint = !PlacingPoint;
        }
        else if (PlacingPoint && GUILayout.Button("Stop Placing Points"))
        {
            PlacingPoint = !PlacingPoint;
        }

        if (PlacingPoint)
        {
            PlaceMode = (SplinePlacePointMode)EditorGUILayout.EnumPopup("Place Point Mode", PlaceMode);

            if (PlaceMode == SplinePlacePointMode.CollisionSurface)
            {
                LayerMask tempMask = EditorGUILayout.MaskField("Surface Layers", InternalEditorUtility.LayerMaskToConcatenatedLayersMask(PlaceLayerMask), InternalEditorUtility.layers);
                PlaceLayerMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(tempMask);
            }

            if (PlaceMode == SplinePlacePointMode.Plane)
            {
                PlacePlaneOffset = EditorGUILayout.Vector3Field("Plane Offset", PlacePlaneOffset);
                PlacePlaneNormal = EditorGUILayout.Vector3Field("Plane Normal", PlacePlaneNormal);
            }
        }

        var optionsStr = new string[instance.Points.Length + 1];
        var optionsInt = new int[instance.Points.Length + 1];

        optionsStr[0] = "none";
        optionsInt[0] = -1;

        for (var i = 0; i < instance.Points.Length; ++i)
        {
            if (instance.Mode == SplineMode.Linear)
            {
                optionsStr[i + 1] = $"point {i:N0}";
            }
            else if (instance.Mode == SplineMode.Bezier)
            {
                if (i % 3 == 0)
                {
                    optionsStr[i + 1] = $"[point] {i:N0}";
                }
                else
                {
                    optionsStr[i + 1] = $"[handle] {i:N0}";
                }
            }

            optionsInt[i + 1] = i;
        }

        SelectedPoint = EditorGUILayout.IntPopup(SelectedPoint, optionsStr, optionsInt);
        MirrorAnchors = EditorGUILayout.Toggle("Mirror Anchors", MirrorAnchors);

    }


    private void AppendPoint(Spline instance, Vector3 position, Vector3 up)
    {
        Undo.RegisterCompleteObjectUndo(instance, "Append Point");

        if (instance.Mode == SplineMode.Linear)
        {
            var newArray = new SplinePoint[instance.Points.Length + 1];
            for (var i = 0; i < instance.Points.Length; ++i)
            {
                newArray[i] = instance.Points[i];
            }

            instance.Points = newArray;

            instance.Points[instance.Points.Length - 1] = new SplinePoint(position, up);
        }
        else if (instance.Mode == SplineMode.Bezier)
        {

            var newArray = new SplinePoint[instance.Points.Length + 3];
            for (var i = 0; i < instance.Points.Length; ++i)
            {
                newArray[i] = instance.Points[i];
            }

            instance.Points = newArray;

            var last0 = instance.Points[instance.Points.Length - 5];
            var last1 = instance.Points[instance.Points.Length - 4];
            var lastDirection = (last1.position - last0.position).normalized;

            instance.Points[instance.Points.Length - 3] = new SplinePoint(position - lastDirection * 2, up);
            instance.Points[instance.Points.Length - 2] = new SplinePoint(position - lastDirection * 1, up);
            instance.Points[instance.Points.Length - 1] = new SplinePoint(position - lastDirection * 0, up);
        }

        EditorUtility.SetDirty(instance);
    }

    private void OnSceneGUI()
    {
        // if moving camera with mouse, dont draw all our gizmos.. 
        if((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && Event.current.button != 0)
        {
            return;
        }

        var instance = (Spline)target;

        if (PlacingPoint)
        {
            DrawPlacePointView(instance);
        }
        else
        {
            if (SelectedPoint >= 0)
            {
                DrawSelectedSplineHandle(instance);
            }

            DrawSelectablePoints(instance);
        }

    }


    private SplinePoint previousMeshSurfacePoint;
    private bool hasPreviousMeshSurfacePoint;

    private bool TryGetPointFromMouse(Spline instance, out SplinePoint point)
    {
        var mousePosition = Event.current.mousePosition;
        var worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);

        switch (PlaceMode)
        {
            case SplinePlacePointMode.CameraPlane:
                var position = worldRay.origin + worldRay.direction * 1f; // * Camera.current.nearClipPlane;
                var up = Vector3.up;
                point = new SplinePoint(position, up);
                return true; 
            case SplinePlacePointMode.Plane:

                if (PlacePlaneNormal.sqrMagnitude < 0.01f)
                {
                    PlacePlaneNormal = Vector3.up;
                }

                PlacePlaneNormal = PlacePlaneNormal.normalized;

                var projectedOnPlane = Vector3.ProjectOnPlane(worldRay.origin - PlacePlaneOffset, PlacePlaneNormal) + PlacePlaneOffset;
                point = new SplinePoint(projectedOnPlane, PlacePlaneNormal);

                return true; 
            case SplinePlacePointMode.MeshSurface:

                // HandleUtility.PickGameObject only works in certain event types, so we need to cache the result to use between event types 
                if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDown)
                {
                    var go = HandleUtility.PickGameObject(mousePosition, false);
                    if(go != null)
                    {
                        var hit = RXLookingGlass.IntersectRayGameObject(worldRay, go, out RaycastHit info);
                        if (hit)
                        {
                            point = new SplinePoint(info.point, info.normal);
                            previousMeshSurfacePoint = point;
                            hasPreviousMeshSurfacePoint = true; 
                            return true;
                        }
                    }
                    else
                    {
                        previousMeshSurfacePoint = new SplinePoint();
                        hasPreviousMeshSurfacePoint = false; 
                    }
                }

                point = previousMeshSurfacePoint;
                return hasPreviousMeshSurfacePoint; 
            case SplinePlacePointMode.CollisionSurface:
                RaycastHit collisionInfo;
                var collisionHit = Physics.Raycast(worldRay, out collisionInfo, 256f, PlaceLayerMask, QueryTriggerInteraction.Ignore);
                if(collisionHit)
                {
                    point = new SplinePoint(collisionInfo.point, collisionInfo.normal); 
                    return true; 
                }
                else
                {
                    point = new SplinePoint(); 
                    return false; 
                }
        }

        point = new SplinePoint(); 
        return false; 
    }

    private void DrawPlacePointView(Spline instance)
    {
        Handles.color = Color.red;

        var mousePosition = Event.current.mousePosition;
        var worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);

        // per place type handles? 
        if (PlaceMode == SplinePlacePointMode.Plane)
        {
            DrawHandle(Vector3.zero, ref PlacePlaneOffset, out Vector3 offsetDelta);
        }

        // try finding point 
        var validPoint = TryGetPointFromMouse(instance, out SplinePoint placingPoint);
        if (!validPoint) return; 

        // try placing it 
        var selected = Handles.Button(placingPoint.position, Quaternion.identity, 0.05f, 0.05f, Handles.DotHandleCap);
        if (selected)
        {
            AppendPoint(instance, placingPoint.position, placingPoint.up); 
        }
    }

    private void DrawSelectedSplineHandle(Spline instance)
    {

        var splinePoint = instance.Points[SelectedPoint];


        Handles.color = Color.white;
        var anyMoved = DrawHandle(Vector3.zero, ref splinePoint.position, out Vector3 splinePointDelta);

        if (instance.Mode == SplineMode.Bezier)
        {
            var pointIsHandle = SelectedPoint % 3 != 0;
            if (pointIsHandle)
            {
                var pointIndex0 = SelectedPoint - SelectedPoint % 3 + 0;
                var pointIndex1 = SelectedPoint - SelectedPoint % 3 + 3;

                var index = SelectedPoint % 3 == 1 ? pointIndex0 : pointIndex1;

                var anchorPoint = instance.Points[index];

                Handles.color = Color.gray;
                Handles.DrawLine(splinePoint.position, anchorPoint.position);


                if (MirrorAnchors && SelectedPoint != 1 && SelectedPoint != instance.Points.Length - 2)
                {
                    var otherHandleIndex = index == pointIndex0
                        ? pointIndex0 - 1
                        : pointIndex1 + 1;

                    var otherHandlePoint = instance.Points[otherHandleIndex];

                    if (anyMoved)
                    {
                        var toAnchorPoint = anchorPoint.position - splinePoint.position;
                        var otherHandlePosition = anchorPoint.position + toAnchorPoint;
                        otherHandlePoint.position = otherHandlePosition;
                        instance.Points[otherHandleIndex] = otherHandlePoint;
                    }

                    Handles.DrawLine(otherHandlePoint.position, anchorPoint.position);
                }
            }
            else
            {
                if (instance.Points.Length > 1)
                {
                    var handleIndex0 = SelectedPoint != 0 ? SelectedPoint - 1 : SelectedPoint + 1;
                    var handleIndex1 = SelectedPoint != instance.Points.Length - 1 ? SelectedPoint + 1 : SelectedPoint - 1;

                    var handle0 = instance.Points[handleIndex0];
                    var handle1 = instance.Points[handleIndex1];

                    handle0.position = handle0.position + splinePointDelta;
                    handle1.position = handle1.position + splinePointDelta;

                    instance.Points[handleIndex0] = handle0;
                    instance.Points[handleIndex1] = handle1;

                    Handles.color = Color.gray;
                    Handles.DrawLine(splinePoint.position, handle0.position);
                    Handles.DrawLine(splinePoint.position, handle1.position);
                }
            }
        }

        if (anyMoved)
        {
            Undo.RegisterCompleteObjectUndo(instance, "Move Point");
            instance.Points[SelectedPoint] = splinePoint;
        }
    }

    private void DrawSelectablePoints(Spline instance)
    {

        for (var p = 0; p < instance.Points.Length; ++p)
        {
            if (p == SelectedPoint)
            {
                continue;
            }

            var point = instance.Points[p];
            var position = point.position;
            var isHandle = instance.Mode == SplineMode.Bezier && p % 3 != 0;



            if (isHandle)
            {
                // when nothing is selected, do not draw handles 
                if (SelectedPoint == -1)
                {
                    continue;
                }

                // when a handle is selected, we only want to draw the other handle touching our center point 
                var isSelectedHandle = SelectedPoint % 3 != 0;
                if (isSelectedHandle)
                {
                    var handleIndex = p % 3;
                    if (handleIndex == 1)
                    {
                        var parentPoint = p - 2;
                        if (parentPoint != SelectedPoint)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        var parentPoint = p + 2;
                        if (parentPoint != SelectedPoint)
                        {
                            continue;
                        }
                    }
                }

                // but if its a point selected, we want to draw both handles 
                else
                {
                    if (p < SelectedPoint - 1 || p > SelectedPoint + 1)
                    {
                        continue;
                    }
                }
            }

            Handles.color = isHandle ? Color.green : Color.blue;

            var selected = Handles.Button(position, Quaternion.identity, 0.25f, 0.25f, Handles.DotHandleCap);
            if (selected)
            {
                Undo.RegisterCompleteObjectUndo(this, "Selected Point");
                SelectedPoint = p;
            }
        }
    }

    private bool DrawHandle(Vector3 offset, ref Vector3 positionRef, out Vector3 delta)
    {
        var startPosition = positionRef;
        positionRef = Handles.PositionHandle(startPosition + offset, Quaternion.identity) - offset;

        delta = positionRef - startPosition;
        var changed = delta.sqrMagnitude > 0;
        return changed;
    }
}

#endif

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(Spline))]
[DefaultExecutionOrder(-10000)]
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

    public bool SnapToNearestVert;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();


        var instance = (Spline)target;

        GUILayout.BeginVertical("GroupBox");
        
        // if (GUILayout.Button("Append Point"))
        // {
        //     var lastPos = instance.GetPoint(1f);
        //     AppendPoint(instance, lastPos.position, lastPos.up);
        // }
        
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

                SnapToNearestVert = EditorGUILayout.Toggle("Snap To Nearest Vertex", SnapToNearestVert); 
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

        GUILayout.EndVertical();
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
            
            var index = instance.Points.Length - 4;
            var previousHandle = instance.Points[instance.Points.Length - 5];
            var anchorPoint = instance.Points[instance.Points.Length - 4];

            var toAnchorPoint = anchorPoint.position - previousHandle.position;
            var toNewHandlePoint = anchorPoint.position + toAnchorPoint;
            
            var last0 = instance.Points[instance.Points.Length - 7]; 
            var last1 = instance.Points[instance.Points.Length - 4];   
            var lastDirection = (last1.position - last0.position).normalized; 
            
            instance.Points[instance.Points.Length - 3] = new SplinePoint(toNewHandlePoint, up);         // handle 1
            instance.Points[instance.Points.Length - 2] = new SplinePoint(position - lastDirection, up); // handle 2 
            instance.Points[instance.Points.Length - 1] = new SplinePoint(position, up);                 // point 
        }

        EditorUtility.SetDirty(instance);
    }

    private void OnSceneGUI()
    {
        DrawToolbar();

        // if moving camera with mouse, dont draw all our gizmos.. (they would block trying to click the handles) 
        if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && Event.current.button != 0)
        {
            return;
        }

        var instance = (Spline)target;

        if (PlacingPoint)
        {
            // block scene input 
            if (Event.current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));
            }

            if(Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
            {
                PlacingPoint = false;
                return; 
            }

            DrawPlacePointView(instance);
        }
        else
        {
            if (SelectedPoint >= 0)
            {
                DrawSelectedSplineHandle(instance);
            }

            // todo, draw these in PlacingPoint too, but non selectable 
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
                    if(SnapToNearestVert && collisionInfo.triangleIndex >= 0)
                    {
                        var meshFilter = collisionInfo.collider.GetComponent<MeshFilter>();
                        var mesh = meshFilter.sharedMesh;

                        var localToWorld = meshFilter.transform.localToWorldMatrix;

                        var vertices = mesh.vertices;
                        var normals = mesh.normals;
                        
                        var triangles = mesh.triangles;
                        var triIndex = collisionInfo.triangleIndex;
                        
                        var vertIndex0 = triangles[triIndex * 3 + 0];
                        var vertIndex1 = triangles[triIndex * 3 + 1];
                        var vertIndex2 = triangles[triIndex * 3 + 2];

                        var vertex0 = localToWorld.MultiplyPoint(vertices[vertIndex0]);
                        var vertex1 = localToWorld.MultiplyPoint(vertices[vertIndex1]);
                        var vertex2 = localToWorld.MultiplyPoint(vertices[vertIndex2]);

                        var normal0 = localToWorld.MultiplyVector(normals[vertIndex0]);
                        var normal1 = localToWorld.MultiplyVector(normals[vertIndex1]);
                        var normal2 = localToWorld.MultiplyVector(normals[vertIndex2]);

                        var distance0 = Vector3.Distance(vertex0, collisionInfo.point);
                        var distance1 = Vector3.Distance(vertex1, collisionInfo.point);
                        var distance2 = Vector3.Distance(vertex2, collisionInfo.point);

                        

                        if(distance0 < distance1 && distance0 < distance2)
                        {
                            point = new SplinePoint(vertex0, normal0);
                        }
                        else if(distance1 < distance0 && distance1 < distance2)
                        {

                            point = new SplinePoint(vertex1, normal1);
                        }
                        else
                        {
                            point = new SplinePoint(vertex2, normal2);

                        }

                        return true; 
                    }
                    else
                    {
                        point = new SplinePoint(collisionInfo.point, collisionInfo.normal); 
                        return true; 
                    }
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

        // per place type handles? 
        if (PlaceMode == SplinePlacePointMode.Plane)
        {
            DrawHandle(Vector3.zero, ref PlacePlaneOffset, out Vector3 offsetDelta);
        }

        // try finding point 
        var validPoint = TryGetPointFromMouse(instance, out SplinePoint placingPoint);
        if (!validPoint) return;

        DrawSquareGUI(placingPoint.position, Color.white); 

        // try placing it 
        if (IsLeftMouseClicked())
        {
            AppendPoint(instance, placingPoint.position, placingPoint.up); 
            Event.current.Use();
        }
    }

    private bool IsLeftMouseClicked()
    {
        return Event.current.type == EventType.MouseDown && Event.current.button == 0;
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

                if(index < 0 || index >= instance.Points.Length)
                {
                    return;
                }

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
            
            var sceneView = SceneView.currentDrawingSceneView;
            var sceneCamera = sceneView.camera;
            var screenPoint = sceneCamera.WorldToScreenPoint(position);
            screenPoint.z = 10f;

            var cameraPoint = sceneCamera.ScreenToWorldPoint(screenPoint);

            var selected = Handles.Button(cameraPoint, Quaternion.identity, .1f, .1f, Handles.DotHandleCap);
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

    private void DrawSquareGUI(Vector3 worldPosition, Color color)
    {
        Handles.BeginGUI();
        var sceneView = SceneView.currentDrawingSceneView;
        var screenPoint = sceneView.camera.WorldToScreenPoint(worldPosition);
        screenPoint.y = Screen.height - screenPoint.y - 40; // 40 is for scene view offset? 

        var iconWidth = 8;
        var iconRect = new Rect(screenPoint.x - iconWidth * 0.5f, screenPoint.y - iconWidth * 0.5f, iconWidth, iconWidth);
        EditorGUI.DrawRect(iconRect, color);
        Handles.EndGUI();
    }

    private void DrawToolbar()
    {

        Handles.BeginGUI();

        GUILayout.BeginArea(new Rect(0, 0, 256 + 64, 256));

        // anything within this vertical group will be stacked on top of one another 
        var vertical_rect = EditorGUILayout.BeginVertical();

        // anything within this horizontal group will be displayed horizontal to one another 
        var horizontal_rect_row_0 = EditorGUILayout.BeginHorizontal(); 

            GUI.color = Color.black;
            GUI.Box(horizontal_rect_row_0, GUIContent.none);

            GUI.color = Color.white; 
            GUILayout.Label("Spline Editor");

        GUILayout.EndHorizontal();


        if(PlacingPoint)
        {
            var horizontal_rect_row_1 = EditorGUILayout.BeginHorizontal();

                GUI.color = Color.black;
                GUI.Box(horizontal_rect_row_1, GUIContent.none);

                GUI.color = Color.white;
                GUILayout.Label("Place Mode (escape to quit)");

                if (PlaceMode == SplinePlacePointMode.CollisionSurface && SnapToNearestVert)
                {
                    GUI.color = Color.white;
                    GUILayout.Label("[Snapping to vertex]");
                }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();

        GUILayout.EndArea();

        Handles.EndGUI(); 

    }

    // helper gui draw functions
    // https://forum.unity.com/threads/draw-a-simple-rectangle-filled-with-a-color.116348/#post-2751340
    private static Texture2D backgroundTexture;
    private static GUIStyle textureStyle;

    private void OnEnable()
    {
        backgroundTexture = Texture2D.whiteTexture;
        textureStyle = new GUIStyle { normal = new GUIStyleState { background = backgroundTexture } };
    }

    public static void DrawRect(Rect position, Color color, GUIContent content = null)
    {
        // EditorGUI.DrawRect(position, color);

        var backgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = color;
        GUI.Box(position, content ?? GUIContent.none, textureStyle);
        GUI.backgroundColor = backgroundColor;
    }

    public static void LayoutBox(Color color, GUIContent content = null)
    {
        var backgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = color;
        GUILayout.Box(content ?? GUIContent.none, textureStyle);
        GUI.backgroundColor = backgroundColor;
    }
}

#endif
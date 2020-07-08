
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace CorgiSpline
{
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
            InsertBetweenPoints,
        }

        public enum SplinePlacePosition
        {
            Beginning,
            End,
        }

        [SerializeField] private List<int> SelectedPoints = new List<int>();
        [SerializeField] private bool MirrorAnchors = true;
        [SerializeField] private bool LockHandleLength = false;
        [SerializeField] private float LockedHandleLength = 1.0f;
        [SerializeField] private bool PlacingPoint;
        [SerializeField] private SplinePlacePointMode PlaceMode = SplinePlacePointMode.MeshSurface;
        [SerializeField] private SplinePlacePosition PlacePosition = SplinePlacePosition.End;
        [SerializeField] private LayerMask PlaceLayerMask;
        [SerializeField] private Vector3 PlacePlaneOffset;
        [SerializeField] private Quaternion PlacePlaneNormalRotation;

        public bool SnapToNearestVert;

        private void OnPointSelected(object index)
        {
            var intIndex = (int)index;

            if (SelectedPoints.Contains(intIndex))
            {
                SelectedPoints.Remove(intIndex);
            }
            else
            {
                SelectedPoints.Add(intIndex);
            }
        }

        private void OnAllPointsSelected(object splineObj)
        {
            var spline = (Spline)splineObj;

            SelectedPoints.Clear();

            for (var i = 0; i < spline.Points.Length; ++i)
            {
                SelectedPoints.Add(i);
            }
        }

        private void OnNoPointsSelected()
        {
            SelectedPoints.Clear();
        }

        private void DrawPointSelectorInspector(Spline spline)
        {
            var selectedPointButtonSb = new System.Text.StringBuilder();

            var selected_none = SelectedPoints.Count == 0;
            var selected_all = SelectedPoints.Count == spline.Points.Length;

            if (selected_none)
            {
                selectedPointButtonSb.Append("No points selected.");
            }
            else if (selected_all)
            {
                selectedPointButtonSb.Append("All points selected.");
            }
            else
            {
                selectedPointButtonSb.Append("Selected: ");

                for (var i = 0; i < SelectedPoints.Count; ++i)
                {
                    var pointIndex = SelectedPoints[i];
                    var pointName = GetPointName(spline, pointIndex);
                    selectedPointButtonSb.Append($"{pointName}");

                    if (i < SelectedPoints.Count - 1)
                    {
                        selectedPointButtonSb.Append($", ");
                    }
                }
            }

            if (GUILayout.Button(selectedPointButtonSb.ToString()))
            {
                Undo.RecordObject(this, "Point Selection");

                var selectedMenu = new GenericMenu();

                selectedMenu.AddItem(new GUIContent("Select All"), selected_all, OnAllPointsSelected, spline);
                selectedMenu.AddItem(new GUIContent("Select None"), selected_none, OnNoPointsSelected);

                var length = spline.Points.Length;
                if (spline.ClosedSpline)
                {
                    switch (spline.GetSplineMode())
                    {
                        case SplineMode.Linear:
                            length -= 1;
                            break;
                        case SplineMode.Bezier:
                            length -= 3;
                            break;
                    }
                }

                for (var i = 0; i < length; ++i)
                {
                    var splinePoint = spline.Points[i];

                    var menuString = GetPointName(spline, i);
                    var pointSelected = SelectedPoints.Contains(i);

                    selectedMenu.AddItem(new GUIContent(menuString), pointSelected, OnPointSelected, i);
                }

                selectedMenu.ShowAsContext();



            }
        }

        private static string GetPointName(Spline spline, int i)
        {
            var name = $"point {i:N0}";

            if (spline.GetSplineMode() == SplineMode.Bezier)
            {
                if (i % 3 == 0)
                {
                    name = $"[point] {i:N0}";
                }
                else
                {
                    name = $"[handle] {i:N0}";
                }
            }

            return name;
        }

        public override void OnInspectorGUI()
        {
            // base.OnInspectorGUI();


            var instance = (Spline)target;


            GUILayout.BeginVertical("GroupBox");
            instance.SetSplineMode((SplineMode)EditorGUILayout.EnumPopup("Curve Type", instance.GetSplineMode()));
            instance.SetSplineSpace((Space)EditorGUILayout.EnumPopup("Spline Space", instance.GetSplineSpace()), true);

            instance.EditorAlwaysDraw = EditorGUILayout.Toggle("EditorAlwaysDraw", instance.EditorAlwaysDraw);
            instance.EditorDrawThickness = EditorGUILayout.Toggle("EditorDrawThickness", instance.EditorDrawThickness);

            EditorGUILayout.Space();

            // var propertyOnSplineModified = serializedObject.FindProperty("OnSplineModified");
            // EditorGUILayout.PropertyField(propertyOnSplineModified, true);

            GUILayout.EndVertical();

            GUILayout.BeginVertical("GroupBox");

            // if (GUILayout.Button("Append Point"))
            // {
            //     var lastPos = instance.GetPoint(1f);
            //     AppendPoint(instance, lastPos.position, lastPos.up);
            // }

            if (instance.ClosedSpline)
            {
                if (GUILayout.Button("Open Spline"))
                {
                    Undo.RecordObjects(new UnityEngine.Object[] { instance, this }, "Open Spline");

                    instance.ClosedSpline = false;

                    PlacingPoint = false;
                    SelectedPoints.Clear();

                    switch (instance.GetSplineMode())
                    {
                        case SplineMode.Linear:
                            instance.ResizePointArray(instance.Points.Length - 1); // remove extra point 
                            break;
                        case SplineMode.Bezier:
                            instance.ResizePointArray(instance.Points.Length - 3); // remove extra points
                            break;
                        default:
                            // not implemented 
                            break;
                    }

                    instance.UpdateNative();
                }
            }
            else
            {
                if (!PlacingPoint)
                {
                    if (GUILayout.Button("Start Placing Points"))
                    {
                        SelectedPoints.Clear();
                        PlacingPoint = !PlacingPoint;
                    }
                    else if (GUILayout.Button("Close Spline"))
                    {
                        Undo.RecordObjects(new UnityEngine.Object[] { instance, this }, "Close Spline");

                        instance.ClosedSpline = true;

                        PlacingPoint = false;
                        SelectedPoints.Clear();

                        switch (instance.GetSplineMode())
                        {
                            case SplineMode.Linear:
                                instance.ResizePointArray(instance.Points.Length + 1);
                                break;
                            case SplineMode.Bezier:
                                instance.ResizePointArray(instance.Points.Length + 3);
                                break;
                            default:
                                // not implemented 
                                break;
                        }

                        instance.EnsureSplineStaysClosed();
                        instance.UpdateNative();
                    }
                }
                else if (PlacingPoint && GUILayout.Button("Stop Placing Points"))
                {
                    PlacingPoint = !PlacingPoint;
                }

            }

            if (PlacingPoint)
            {
                PlaceMode = (SplinePlacePointMode)EditorGUILayout.EnumPopup("Place Point Mode", PlaceMode);
                PlacePosition = (SplinePlacePosition)EditorGUILayout.EnumPopup("Append To Side", PlacePosition);

                if (PlaceMode == SplinePlacePointMode.CollisionSurface)
                {
                    LayerMask tempMask = EditorGUILayout.MaskField("Surface Layers", InternalEditorUtility.LayerMaskToConcatenatedLayersMask(PlaceLayerMask), InternalEditorUtility.layers);
                    PlaceLayerMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(tempMask);

                    SnapToNearestVert = EditorGUILayout.Toggle("Snap To Nearest Vertex", SnapToNearestVert);
                }

                if (PlaceMode == SplinePlacePointMode.Plane)
                {
                    PlacePlaneOffset = EditorGUILayout.Vector3Field("Plane Offset", PlacePlaneOffset);
                    PlacePlaneNormalRotation.eulerAngles = EditorGUILayout.Vector3Field("Plane Rotation", PlacePlaneNormalRotation.eulerAngles);
                }
            }
            else
            {
                GUILayout.BeginVertical("GroupBox");

                GUILayout.Label("Point Editor", UnityEditor.EditorStyles.largeLabel);

                MirrorAnchors = EditorGUILayout.Toggle("Mirror Anchors", MirrorAnchors);

                LockHandleLength = EditorGUILayout.Toggle("Lock Handles Length", LockHandleLength);

                if (LockHandleLength)
                {
                    LockedHandleLength = EditorGUILayout.FloatField("Locked Handles Length", LockedHandleLength);
                }

                DrawPointSelectorInspector(instance);

                if (SelectedPoints.Count > 0)
                {
                    GUILayout.BeginVertical("GroupBox");

                    var first_point_index = SelectedPoints[0];
                    var point = instance.Points[first_point_index];

                    var editPoint = point;
                    editPoint.position = EditorGUILayout.Vector3Field("point position", editPoint.position);
                    editPoint.rotation.eulerAngles = EditorGUILayout.Vector3Field("point rotation", editPoint.rotation.eulerAngles);
                    editPoint.scale = EditorGUILayout.Vector3Field("point scale", editPoint.scale);

                    if (!point.Equals(editPoint))
                    {
                        Undo.RecordObject(instance, "Point Edited");
                        instance.Points[first_point_index] = editPoint;

                        // update handles if necessary 
                        UpdateHandlesWhenPointMoved(instance, first_point_index, editPoint.position - point.position);

                        instance.UpdateNative();
                    }

                    GUILayout.EndVertical();
                }

                GUILayout.EndVertical();
            }


            GUILayout.EndVertical();

            if (GUI.changed)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void AppendPoint(Spline instance, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Undo.RegisterCompleteObjectUndo(instance, "AppendPoint");

            // if we want to place the point at the beginning, 
            // just reverse the array, place, then reverse again 
            if (PlacePosition == SplinePlacePosition.Beginning)
            {
                instance.ReversePoints();
            }

            instance.AppendPoint(position, rotation, scale);

            // un-reverses the previous reverse
            if (PlacePosition == SplinePlacePosition.Beginning)
            {
                instance.ReversePoints();
            }

            EditorUtility.SetDirty(instance);
        }

        private void OnSceneGUI()
        {
            var instance = (Spline)target;

            // pointless for now 
            // DrawToolbar();

            // if moving camera with mouse, dont draw all our gizmos.. (they would block trying to click the handles) 
            if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && Event.current.button != 0)
            {
                return;
            }

            // try consuming the delete key (delete points) 
            if ((Event.current.type == EventType.KeyDown) && Event.current.keyCode == KeyCode.Delete)
            {
                if (SelectedPoints.Count == 0)
                {
                    return;
                }

                else
                {
                    // sort, 0, 1, 2, etc 
                    SelectedPoints.Sort();

                    // copy points into easier to modify list 
                    var pointList = instance.Points.ToList();

                    // then delete from highest to lowest 
                    for (var i = SelectedPoints.Count - 1; i >= 0; --i)
                    {
                        var point_index = SelectedPoints[i];

                        // don't allow deleting handles directly? 
                        var isHandle = SplinePoint.IsHandle(instance.GetSplineMode(), point_index);
                        if (isHandle) continue;

                        // if we have neighbor handles, find them and delete them too.. 
                        if (instance.GetSplineMode() == SplineMode.Bezier)
                        {
                            SplinePoint.GetHandleIndexes(instance.GetSplineMode(), instance.ClosedSpline, instance.Points.Length,
                                point_index, out int handleIndex0, out int handleIndex1);

                            if (pointList.Count > 0 && pointList.Count > handleIndex1) pointList.RemoveAt(handleIndex1);
                            if (pointList.Count > 0 && pointList.Count > point_index) pointList.RemoveAt(point_index);
                            if (pointList.Count > 0 && pointList.Count > handleIndex0) pointList.RemoveAt(handleIndex0);
                        }
                        else if (instance.GetSplineMode() == SplineMode.Linear)
                        {
                            if (pointList.Count > 0 && pointList.Count > point_index) pointList.RemoveAt(point_index);
                        }
                    }

                    // update original points with modified list 
                    instance.Points = pointList.ToArray();

                    // deselect all points 
                    SelectedPoints.Clear();

                    // consume event and exit 
                    Event.current.Use();
                    return;
                }
            }



            if (PlacingPoint)
            {
                // block scene input 
                if (Event.current.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));
                }

                if (Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
                {
                    PlacingPoint = false;
                    return;
                }

                DrawPlacePointView(instance);
            }
            else
            {
                if (SelectedPoints.Count > 0)
                {
                    var first_selected_point = SelectedPoints[0];
                    DrawSelectedSplineHandle(instance, first_selected_point);
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
                    point = new SplinePoint(position, Quaternion.LookRotation(worldRay.direction, Vector3.up), Vector3.one);
                    return true;
                case SplinePlacePointMode.Plane:
                    var placePlaneNormal = PlacePlaneNormalRotation * Vector3.forward;
                    var projectedOnPlane = Vector3.ProjectOnPlane(worldRay.origin - PlacePlaneOffset, placePlaneNormal) + PlacePlaneOffset;
                    point = new SplinePoint(projectedOnPlane, PlacePlaneNormalRotation, Vector3.one);

                    return true;
                case SplinePlacePointMode.MeshSurface:

                    // HandleUtility.PickGameObject only works in certain event types, so we need to cache the result to use between event types 
                    if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDown)
                    {
                        var go = HandleUtility.PickGameObject(mousePosition, false);
                        if (go != null)
                        {
                            var hit = RXLookingGlass.IntersectRayGameObject(worldRay, go, out RaycastHit info);
                            if (hit)
                            {
                                point = new SplinePoint(info.point, Quaternion.LookRotation(info.normal, Vector3.up), Vector3.one);
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
                    if (collisionHit)
                    {
                        if (SnapToNearestVert && collisionInfo.triangleIndex >= 0)
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

                            var rotation0 = Quaternion.LookRotation(normal0, Vector3.up);
                            var rotation1 = Quaternion.LookRotation(normal1, Vector3.up);
                            var rotation2 = Quaternion.LookRotation(normal2, Vector3.up);

                            if (distance0 < distance1 && distance0 < distance2)
                            {
                                point = new SplinePoint(vertex0, rotation0, Vector3.one);
                            }
                            else if (distance1 < distance0 && distance1 < distance2)
                            {

                                point = new SplinePoint(vertex1, rotation1, Vector3.one);
                            }
                            else
                            {
                                point = new SplinePoint(vertex2, rotation2, Vector3.one);
                            }

                            return true;
                        }
                        else
                        {
                            point = new SplinePoint(collisionInfo.point, Quaternion.LookRotation(collisionInfo.normal, Vector3.up), Vector3.one);
                            return true;
                        }
                    }
                    else
                    {
                        point = new SplinePoint();
                        return false;
                    }

                case SplinePlacePointMode.InsertBetweenPoints:

                    var sceneCam = SceneView.currentDrawingSceneView.camera;
                    point = instance.ProjectOnSpline(sceneCam, sceneCam.WorldToScreenPoint(worldRay.origin));
                    return true;
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
                switch (Tools.current)
                {
                    case Tool.Move:
                        DrawHandle(Vector3.zero, ref PlacePlaneOffset, out Vector3 offsetDelta);
                        PlacePlaneOffset += offsetDelta;
                        break;
                    case Tool.Rotate:
                        DrawHandleRotation(PlacePlaneOffset, ref PlacePlaneNormalRotation);

                        var normal = PlacePlaneNormalRotation * Vector3.forward;

                        Handles.color = Color.green;
                        Handles.DrawLine(PlacePlaneOffset, PlacePlaneOffset + normal);

                        break;
                }
            }

            // try finding point 
            var validPoint = TryGetPointFromMouse(instance, out SplinePoint placingPoint);
            if (!validPoint) return;

            // draw preview spline
            if (PlaceMode != SplinePlacePointMode.InsertBetweenPoints)
            {
                // keep a copy so we can revert the operation 
                var points_clone = (SplinePoint[])instance.Points.Clone();

                // perform the operation
                AppendPoint(instance, placingPoint.position, placingPoint.rotation, placingPoint.scale);

                // draw 
                Handles.color = Color.white * 0.9f;
                instance.DrawAsHandles();

                // then undo it 
                instance.Points = points_clone;
            }

            // show button 
            DrawSquareGUI(placingPoint.position, Color.white);

            // try placing it 
            if (IsLeftMouseClicked())
            {
                if (PlaceMode == SplinePlacePointMode.InsertBetweenPoints)
                {
                    InsertPoint(instance, placingPoint);
                }
                else
                {
                    AppendPoint(instance, placingPoint.position, placingPoint.rotation, placingPoint.scale);
                }

                Event.current.Use();
            }
        }

        private void InsertPoint(Spline instance, SplinePoint placingPoint)
        {
            Undo.RecordObject(instance, "Inserting point.");
            instance.InsertPoint(placingPoint);
        }

        private bool IsLeftMouseClicked()
        {
            return Event.current.type == EventType.MouseDown && Event.current.button == 0;
        }

        private void DrawSelectedSplineHandle(Spline instance, int point_index)
        {

            var splinePoint = instance.Points[point_index];


            Handles.color = Color.white;

            var anyMoved = false;
            var anyRotated = false;
            var anyScaled = false;
            var splinePointDelta = Vector3.zero;

            if (instance.GetSplineSpace() == Space.Self)
            {
                splinePoint = instance.TransformSplinePoint(splinePoint);
            }

            switch (Tools.current)
            {

                case Tool.Move:
                    anyMoved = DrawHandle(Vector3.zero, ref splinePoint.position, out splinePointDelta);
                    break;
                case Tool.Rotate:
                    anyRotated = DrawHandleRotation(splinePoint.position, ref splinePoint.rotation);
                    break;
                case Tool.Scale:
                    anyScaled = DrawHandleScale(splinePoint.position, ref splinePoint.scale);
                    break;
            }

            if (instance.GetSplineSpace() == Space.Self)
            {
                splinePoint = instance.InverseTransformSplinePoint(splinePoint);
            }

            if (anyMoved || anyRotated || anyScaled)
            {
                Undo.RegisterCompleteObjectUndo(instance, "Move Point");

                var original_point = instance.Points[point_index];
                instance.Points[point_index] = splinePoint;
                instance.EnsureSplineStaysClosed();

                if (LockHandleLength)
                {
                    var pointIsHandle = SplinePoint.IsHandle(instance.GetSplineMode(), point_index);
                    if (pointIsHandle)
                    {
                        var anchor_index = SplinePoint.GetAnchorIndex(instance.GetSplineMode(), point_index);
                        var anchor_point = instance.Points[anchor_index];

                        var to_anchor = splinePoint.position - anchor_point.position;
                        var dir_anchor = to_anchor.normalized;

                        splinePoint.position = anchor_point.position + dir_anchor * LockedHandleLength;
                        instance.Points[point_index] = splinePoint;
                    }
                }

                UpdateHandlesWhenPointMoved(instance, point_index, splinePointDelta);

                if (anyMoved)
                {
                    var delta_move = splinePoint.position - original_point.position;

                    for (var i = 0; i < SelectedPoints.Count; ++i)
                    {
                        var other_index = SelectedPoints[i];
                        if (other_index == point_index) continue;

                        UpdateHandlesWhenPointMoved(instance, other_index, splinePointDelta);

                        instance.Points[other_index].position += delta_move;
                    }
                }

                if (anyRotated)
                {
                    var delta_rotation = Quaternion.Inverse(original_point.rotation) * splinePoint.rotation;

                    for (var i = 0; i < SelectedPoints.Count; ++i)
                    {
                        var other_index = SelectedPoints[i];
                        if (other_index == point_index) continue;
                        instance.Points[other_index].rotation *= delta_rotation;
                    }
                }

                if (anyScaled)
                {
                    var delta_scale = splinePoint.scale - original_point.scale;

                    for (var i = 0; i < SelectedPoints.Count; ++i)
                    {
                        var other_index = SelectedPoints[i];
                        if (other_index == point_index) continue;
                        instance.Points[other_index].scale += delta_scale;
                    }
                }

                instance.UpdateNative();
            }
        }

        private void UpdateHandlesWhenPointMoved(Spline instance, int point_index, Vector3 move_delta)
        {
            var splinePoint = instance.Points[point_index];

            if (instance.GetSplineMode() == SplineMode.Bezier)
            {
                var pointIsHandle = point_index % 3 != 0;
                if (pointIsHandle)
                {
                    var pointIndex0 = point_index - point_index % 3 + 0;
                    var pointIndex1 = point_index - point_index % 3 + 3;

                    var index = point_index % 3 == 1 ? pointIndex0 : pointIndex1;

                    if (index < 0 || index >= instance.Points.Length)
                    {
                        return;
                    }

                    var anchorPoint = instance.Points[index];

                    Handles.color = Color.gray;
                    Handles.DrawLine(splinePoint.position, anchorPoint.position);


                    if (MirrorAnchors && point_index != 1 && point_index != instance.Points.Length - 2)
                    {
                        var otherHandleIndex = index == pointIndex0
                            ? pointIndex0 - 1
                            : pointIndex1 + 1;

                        var otherHandlePoint = instance.Points[otherHandleIndex];

                        var toAnchorPoint = anchorPoint.position - splinePoint.position;
                        var otherHandlePosition = anchorPoint.position + toAnchorPoint;
                        otherHandlePoint.position = otherHandlePosition;
                        instance.Points[otherHandleIndex] = otherHandlePoint;

                        Handles.DrawLine(otherHandlePoint.position, anchorPoint.position);
                    }
                }
                else
                {
                    if (instance.Points.Length > 1)
                    {
                        var handleIndex0 = point_index != 0 ? point_index - 1 : point_index + 1;
                        var handleIndex1 = point_index != instance.Points.Length - 1 ? point_index + 1 : point_index - 1;

                        var handle0 = instance.Points[handleIndex0];
                        var handle1 = instance.Points[handleIndex1];

                        handle0.position = handle0.position + move_delta;
                        handle1.position = handle1.position + move_delta;

                        // only update these if they are not also selected 
                        if (!SelectedPoints.Contains(handleIndex0)) instance.Points[handleIndex0] = handle0;
                        if (!SelectedPoints.Contains(handleIndex1)) instance.Points[handleIndex1] = handle1;

                        Handles.color = Color.gray;
                        Handles.DrawLine(splinePoint.position, handle0.position);
                        Handles.DrawLine(splinePoint.position, handle1.position);
                    }
                }
            }
        }

        private void DrawSelectablePoints(Spline instance)
        {

            var first_selected_point = SelectedPoints.Count > 0 ? SelectedPoints[0] : -1;

            var length = instance.Points.Length;
            if (instance.ClosedSpline)
            {
                switch (instance.GetSplineMode())
                {
                    case SplineMode.Linear:
                        length -= 1;
                        break;
                    case SplineMode.Bezier:
                        length -= 3;
                        break;
                }
            }

            for (var p = 0; p < length; ++p)
            {
                if (p == first_selected_point)
                {
                    continue;
                }

                var point = instance.Points[p];

                if (instance.GetSplineSpace() == Space.Self)
                {
                    point = instance.TransformSplinePoint(point);
                }

                var position = point.position;
                var isHandle = instance.GetSplineMode() == SplineMode.Bezier && p % 3 != 0;

                if (isHandle)
                {
                    // when nothing is selected, do not draw handles 
                    if (first_selected_point == -1)
                    {
                        continue;
                    }

                    // when a handle is selected, we only want to draw the other handle touching our center point 
                    var isSelectedHandle = first_selected_point % 3 != 0;
                    if (isSelectedHandle)
                    {
                        var handleIndex = p % 3;
                        if (handleIndex == 1)
                        {
                            var parentPoint = p - 2;
                            if (parentPoint != first_selected_point)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            var parentPoint = p + 2;
                            if (parentPoint != first_selected_point)
                            {
                                continue;
                            }
                        }
                    }

                    // but if its a point selected, we want to draw both handles 
                    else
                    {
                        if (p < first_selected_point - 1 || p > first_selected_point + 1)
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

                Handles.color = SelectedPoints.Contains(p) ? Color.white : isHandle ? Color.green : Color.blue;
                var selected = Handles.Button(cameraPoint, Quaternion.identity, .1f, .1f, Handles.DotHandleCap);
                if (selected)
                {
                    Undo.RegisterCompleteObjectUndo(this, "Selected Point");

                    // select a new point
                    var select_multiple = Event.current.modifiers == EventModifiers.Control;

                    if (select_multiple)
                    {
                        if (!SelectedPoints.Contains(p))
                        {
                            SelectedPoints.Add(p);
                        }
                        else
                        {
                            SelectedPoints.Remove(p);
                        }
                    }
                    else
                    {
                        SelectedPoints.Clear();
                        SelectedPoints.Add(p);
                    }
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

        private bool DrawHandleRotation(Vector3 position, ref Quaternion rotation)
        {
            var normal = rotation * Vector3.forward;
            Handles.color = Color.green;
            Handles.DrawLine(position, position + normal);

            rotation = Handles.RotationHandle(rotation, position);
            var new_normal = rotation * Vector3.forward;

            var delta_normal = new_normal - normal;
            var changed = delta_normal.sqrMagnitude > 0;
            return changed;
        }

        private bool DrawHandleScale(Vector3 position, ref Vector3 scale)
        {
            var startScale = scale;
            scale = Handles.ScaleHandle(scale, position, Quaternion.identity, HandleUtility.GetHandleSize(position));

            var delta_scale = startScale - scale;
            var changed = delta_scale.sqrMagnitude > 0;
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


            if (PlacingPoint)
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
}

#endif
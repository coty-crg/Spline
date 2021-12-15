using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace CorgiSpline
{
#if UNITY_EDITOR
    using UnityEditor;

    public class SplineMeshBuilder_RepeatingMeshEditor_GUI : EditorWindow
    {
        public SplineMeshBuilder_RepeatingMesh instance;

        private List<Vector3> drawnPositions = new List<Vector3>();
        private Vector3 _position = new Vector3(128f, 128f, 0f);
        private Vector3 _rotation = new Vector3(0f, 25f, 0f);
        private float _scale = 256f;

        [System.NonSerialized] private int _selected_vert_0 = -1;

        public static SplineMeshBuilder_RepeatingMeshEditor_GUI ShowWindow()
        {
            return GetWindow<SplineMeshBuilder_RepeatingMeshEditor_GUI>("RepeatingMeshEditor");
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (instance == null)
            {
                GUILayout.Label("Select an instance of SplineMeshBuilder_RepeatingMesh to edit.");
                return;
            }

            HandlEvents();
            DrawMeshEditor();
        }

        private void DrawToolbar()
        {
            instance = (SplineMeshBuilder_RepeatingMesh) EditorGUILayout.ObjectField("Instance", instance, typeof(SplineMeshBuilder_RepeatingMesh), true);

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label("Tools");
                if (GUILayout.Button("swap overrides order"))
                {
                    Undo.RecordObject(instance, "Swapped start/end stitches.");

                    var tempa = new int[instance.override_stich_start.Count];
                    var tempb = new int[instance.override_stich_end.Count];

                    for (var i = 0; i < instance.override_stich_start.Count; ++i)
                    {
                        tempa[i] = instance.override_stich_start[i];
                    }

                    for (var i = 0; i < instance.override_stich_end.Count; ++i)
                    {
                        tempb[i] = instance.override_stich_end[i];
                    }

                    for (var i = 0; i < instance.override_stich_start.Count; ++i)
                    {
                        instance.override_stich_start[i] = tempa[instance.override_stich_start.Count - 1 - i];
                    }

                    for (var i = 0; i < instance.override_stich_end.Count; ++i)
                    {
                        instance.override_stich_end[i] = tempb[instance.override_stich_end.Count - 1 - i];
                    }
                }

                if (GUILayout.Button("swap overrides starts and ends"))
                {
                    Undo.RecordObject(instance, "Swapped wind direction of stitches.");

                    var temp = new int[instance.override_stich_start.Count];

                    for (var i = 0; i < instance.override_stich_start.Count; ++i)
                    {
                        temp[i] = instance.override_stich_start[i];
                    }

                    for (var i = 0; i < instance.override_stich_start.Count; ++i)
                    {
                        instance.override_stich_start[i] = instance.override_stich_end[i];
                    }

                    for (var i = 0; i < instance.override_stich_start.Count; ++i)
                    {
                        instance.override_stich_end[i] = temp[i];
                    }
                }

                if(GUILayout.Button("Clear stitches"))
                {
                    Undo.RecordObject(instance, "Cleared stitches.");
                    instance.override_stich_end.Clear();
                    instance.override_stich_start.Clear();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label("Stats");
                GUILayout.Label($"verts: {instance.RepeatableMesh.vertexCount}");
                GUILayout.Label($"stitched: {instance.override_stich_start.Count}");
            }
            GUILayout.EndHorizontal();
        }

        private void HandlEvents()
        {
            if (Event.current.type == EventType.MouseDrag && Event.current.button == 0)
            {
                _rotation.y += Event.current.delta.x;
                _rotation.x += Event.current.delta.y;
                Event.current.Use();
            }

            else if (Event.current.type == EventType.MouseDrag && Event.current.button == 2)
            {
                _position.x += Event.current.delta.x;
                _position.y += Event.current.delta.y;

                Event.current.Use();
            }

            else if (Event.current.type == EventType.ScrollWheel)
            {
                _scale += Event.current.delta.y * 10f;

                Event.current.Use();
            }
        }

        private void DrawMeshEditor()
        {
            var mesh = instance.RepeatableMesh;
            var verts = mesh.vertices;
            var triangles = mesh.triangles;

            Handles.BeginGUI();

            var localToWorld = Matrix4x4.TRS(_position, Quaternion.Euler(_rotation), new Vector3(_scale, _scale, _scale)); ;

            for (var s = 0; s < 2; ++s)
            {
                var offset = Vector3.zero;
                if (s == 1)
                {
                    offset = new Vector3(0, 0, -2);
                }

                drawnPositions.Clear();

                Handles.color = Color.white;

                for (var v = 0; v < verts.Length; ++v)
                {
                    if (s == 0 && _selected_vert_0 >= 0)
                    {
                        continue;
                    }

                    if (s > 0 && _selected_vert_0 == -1)
                    {
                        continue;
                    }

                    var vertex = verts[v] + offset;
                    var worldPos = localToWorld.MultiplyPoint(vertex);
                    worldPos.z = 0f;

                    var skip = false;
                    foreach (var prevPos in drawnPositions)
                    {
                        if (Vector3.Distance(prevPos, worldPos) < 0.01f)
                        {
                            skip = true;
                            break;
                        }
                    }

                    if (skip) continue;
                    drawnPositions.Add(worldPos);

                    if (_selected_vert_0 >= 0)
                    {
                        Handles.color = Color.white;
                    }

                    if (_selected_vert_0 == v)
                    {
                        Handles.color = Color.green;
                    }

                    var buttonSize = 4f;
                    if (Handles.Button(worldPos, Quaternion.identity, buttonSize, buttonSize, Handles.DotHandleCap))
                    {
                        if (_selected_vert_0 == -1)
                        {
                            _selected_vert_0 = v;
                        }
                        else
                        {
                            Undo.RecordObject(instance, "Adding point");

                            // remove any duplicate stitched points.. 
                            var existingIndex = instance.override_stich_start.IndexOf(_selected_vert_0);
                            if (existingIndex >= 0)
                            {
                                instance.override_stich_start.RemoveAt(existingIndex);
                                instance.override_stich_end.RemoveAt(existingIndex);
                            }

                            existingIndex = instance.override_stich_end.IndexOf(_selected_vert_0);
                            if (existingIndex >= 0)
                            {
                                instance.override_stich_start.RemoveAt(existingIndex);
                                instance.override_stich_end.RemoveAt(existingIndex);
                            }

                            existingIndex = instance.override_stich_start.IndexOf(v);
                            if (existingIndex >= 0)
                            {
                                instance.override_stich_start.RemoveAt(existingIndex);
                                instance.override_stich_end.RemoveAt(existingIndex);
                            }

                            existingIndex = instance.override_stich_end.IndexOf(v);
                            if (existingIndex >= 0)
                            {
                                instance.override_stich_start.RemoveAt(existingIndex);
                                instance.override_stich_end.RemoveAt(existingIndex);
                            }

                            // add the stitched points 
                            instance.override_stich_start.Add(_selected_vert_0);
                            instance.override_stich_end.Add(v);

                            _selected_vert_0 = -1;
                        }
                    }
                }

                // draw the wireframe (preview) 
                Handles.color = new Color(0.75f, 0.75f, 0.75f, 1f);

                for (var t = 0; t < triangles.Length - 3; t += 3)
                {
                    var v0 = triangles[t + 0];
                    var v1 = triangles[t + 1];
                    var v2 = triangles[t + 2];

                    var vert0 = verts[v0] + offset;
                    var vert1 = verts[v1] + offset;
                    var vert2 = verts[v2] + offset;

                    var worldPos0 = localToWorld.MultiplyPoint(vert0);
                    var worldPos1 = localToWorld.MultiplyPoint(vert1);
                    var worldPos2 = localToWorld.MultiplyPoint(vert2);

                    worldPos0.z = 0;
                    worldPos1.z = 0;
                    worldPos2.z = 0;

                    Handles.DrawLine(worldPos0, worldPos1);
                    Handles.DrawLine(worldPos1, worldPos2);
                    Handles.DrawLine(worldPos2, worldPos0);
                }
            }

            // draw stitching 
            Handles.color = Color.green;
            for (var o = 0; o < instance.override_stich_start.Count; ++o)
            {
                var offset = new Vector3(0, 0, -2);

                var v0 = instance.override_stich_start[o];
                var v1 = instance.override_stich_end[o];

                var vertex0 = verts[v0];
                var vertex1 = verts[v1] + offset;

                var worldPos0 = localToWorld.MultiplyPoint(vertex0);
                var worldPos1 = localToWorld.MultiplyPoint(vertex1);

                worldPos0.z = 0;
                worldPos1.z = 0;

                Handles.DrawLine(worldPos0, worldPos1);
            }

            Handles.EndGUI();
        }
    }

    [CustomEditor(typeof(SplineMeshBuilder_RepeatingMesh))]
    public class SplineMeshBuilder_RepeatingMeshEditor : Editor
    {
        private SplineMeshBuilder_RepeatingMeshEditor_GUI customEditor;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder_RepeatingMesh) target;

            if(GUILayout.Button("Mesh Stitching Editor"))
            {
                var window = SplineMeshBuilder_RepeatingMeshEditor_GUI.ShowWindow();
                window.instance = instance; 
            }
        }
    }
#endif


    [System.Serializable]
    public enum RepeatMode
    {
        PasteAndStitch = 0,
        PasteAndBend = 1,
    }

    public class SplineMeshBuilder_RepeatingMesh : SplineMeshBuilder
    {
        public Mesh RepeatableMesh;
        public Vector3 MeshLocalOffsetVertices;
        public bool UseRepeatingMeshUVs;
        public RepeatMode MeshRepeatMode;

        private List<int> cache_tris = new List<int>();
        private List<Vector3> cache_verts = new List<Vector3>();
        private List<Vector3> cache_normals = new List<Vector3>();
        private List<Vector4> cache_tangents = new List<Vector4>();
        private List<Vector4> cache_uv0 = new List<Vector4>();
        private List<Color> cache_colors = new List<Color>();

        private NativeList<int> native_tris;
        private NativeList<Vector3> native_verts;
        private NativeList<Vector3> native_normals;
        private NativeList<Vector4> native_tangents;
        private NativeList<Vector4> native_uv0;
        private NativeList<Vector4> native_colors;

        private NativeList<int> native_stitch_start;
        private NativeList<int> native_stitch_end;

        [HideInInspector] public List<int> override_stich_start = new List<int>(); 
        [HideInInspector] public List<int> override_stich_end = new List<int>(); 

        protected override void OnEnable()
        {
            native_tris = new NativeList<int>(Allocator.Persistent);
            native_verts = new NativeList<Vector3>(Allocator.Persistent);
            native_normals = new NativeList<Vector3>(Allocator.Persistent);
            native_tangents = new NativeList<Vector4>(Allocator.Persistent);
            native_uv0 = new NativeList<Vector4>(Allocator.Persistent);
            native_colors = new NativeList<Vector4>(Allocator.Persistent);

            native_stitch_start = new NativeList<int>(Allocator.Persistent);
            native_stitch_end = new NativeList<int>(Allocator.Persistent);

            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            native_tris.Dispose();
            native_verts.Dispose();
            native_normals.Dispose();
            native_tangents.Dispose();
            native_colors.Dispose();

            native_stitch_start.Dispose();
            native_stitch_end.Dispose();
        }

        protected override JobHandle ScheduleMeshingJob(JobHandle dependency = default)
        {
            if(RepeatableMesh == null)
            {
                return dependency;
            }

            // todo: cache 
            cache_tris.Clear();
            cache_verts.Clear();
            cache_normals.Clear();
            cache_tangents.Clear();
            cache_uv0.Clear();
            cache_colors.Clear();

            native_tris.Clear();
            native_verts.Clear();
            native_uv0.Clear();
            native_stitch_start.Clear();
            native_stitch_end.Clear();
            native_colors.Clear();

            RepeatableMesh.GetTriangles(cache_tris, 0);
            RepeatableMesh.GetVertices(cache_verts);
            RepeatableMesh.GetNormals(cache_normals);
            RepeatableMesh.GetTangents(cache_tangents);
            RepeatableMesh.GetUVs(0, cache_uv0);
            RepeatableMesh.GetColors(cache_colors);

            for (var t = 0; t < cache_tris.Count; ++t)
                native_tris.Add(cache_tris[t]);

            for (var v = 0; v < cache_verts.Count; ++v)
                native_verts.Add(cache_verts[v]);

            for (var v = 0; v < cache_normals.Count; ++v)
                native_normals.Add(cache_normals[v]);

            for (var v = 0; v < cache_tangents.Count; ++v)
                native_tangents.Add(cache_tangents[v]);

            for (var v = 0; v < cache_uv0.Count; ++v)
                native_uv0.Add(cache_uv0[v]);

            for (var v = 0; v < cache_colors.Count; ++v)
                native_colors.Add(new Vector4(cache_colors[v].r, cache_colors[v].g, cache_colors[v].b, cache_colors[v].a));

            // try and find start and end z values 
            var z_min = float.MaxValue;
            var z_max = float.MinValue;
            for(var v = 0; v < native_verts.Length; ++v)
            {
                var vertex = native_verts[v];
                if(vertex.z < z_min)
                {
                    z_min = vertex.z;
                }

                if (vertex.z > z_max)
                {
                    z_max = vertex.z;
                }
            }

            // use those values to determine which indices belong where 
            for (var v = 0; v < native_verts.Length; ++v)
            {
                var vertex = native_verts[v];
                if (vertex.z <= z_min + 0.0001f)
                {
                    native_stitch_start.Add(v);
                }

                if (vertex.z >= z_max - 0.0001f)
                {
                    native_stitch_end.Add(v);
                }
            }

            // re-order the vert stitch set to wrap in a circle.. 
            var center = RepeatableMesh.bounds.center;
            center.z = 0f; // flatten z 

            var angles = new float[native_stitch_start.Length];
            for (var v = 0; v < native_stitch_start.Length; ++v)
            {
                var tri = native_stitch_start[v];
                var vert = native_verts[tri];
                vert.z = 0f; // flatten z 

                var fromCenter = vert - center;
                var angle = Mathf.Atan2(fromCenter.y, fromCenter.x);
                angles[v] = angle;
            }

            for (var a = 0; a < angles.Length; ++a)
            {
                var angle_a = angles[a];

                var swap_index = -1;

                for(var b = a + 1; b < angles.Length; ++b)
                {
                    var angle_b = angles[b];

                    if(angle_a < angle_b)
                    {
                        swap_index = b;
                        break;
                    }
                }


                // swap.. 
                if(swap_index != -1)
                {
                    var b = swap_index;
                    var angle_b = angles[b];

                    angles[a] = angle_b;
                    angles[b] = angle_a;

                    var tri_a = native_stitch_start[a];
                    var tri_b = native_stitch_start[b];

                    native_stitch_start[a] = tri_b;
                    native_stitch_start[b] = tri_a;

                    // start over.. 
                    a = -1;
                }
            }

            // re-order the stitched indices such that the start and end match? 
            for (var v = 0; v < native_stitch_start.Length; ++v)
            {
                var tri_a = native_stitch_start[v];
                var vert_a = native_verts[tri_a];

                for(var j = 0; j < native_stitch_end.Length; ++j)
                {
                    var tri_b = native_stitch_end[j];
                    var vert_b = native_verts[tri_b];

                    if(vert_a.x == vert_b.x && vert_a.y == vert_b.y)
                    {
                        // swap 
                        var tri_end = native_stitch_end[v];
                        native_stitch_end[v] = tri_b;
                        native_stitch_end[j] = tri_end;
                        break; 
                    }
                }
            }

            // optional override step 
            if (override_stich_start != null && override_stich_end != null && override_stich_start.Count > 0 && override_stich_end.Count > 0 && override_stich_start.Count == override_stich_end.Count)
            {
                native_stitch_start.Clear();
                native_stitch_end.Clear();

                for (var i = 0; i < override_stich_start.Count; ++i)
                {
                    native_stitch_start.Add(override_stich_start[i]);
                    native_stitch_end.Add(override_stich_end[i]);
                }
            }

            var job = new BuildMeshFromSpline_RepeatingMesh()
            {
                repeatingMesh_tris = native_tris,
                repeatingMesh_verts = native_verts,
                repeatingMesh_normals = native_normals,
                repeatingMesh_tangents = native_tangents,
                repeatingMesh_uv0 = native_uv0,
                repeatingMesh_colors = native_colors,
                repeatingMesh_bounds = RepeatableMesh.bounds,

                repeatingMesh_has_colors = native_colors.Length  == native_verts.Length,
                repeatingMesh_has_uv0 = native_uv0.Length  == native_verts.Length,

                repeatingMesh_stitchVertsStart = native_stitch_start,
                repeatingMesh_stitchVertsEnd = native_stitch_end,
                MeshLocalOffsetVertices = MeshLocalOffsetVertices,
                UseRepeatingMeshUVs = UseRepeatingMeshUVs,
                MeshRepeatMode = MeshRepeatMode,
                
                built_to_t = built_to_t,
                quality = quality,
                uv_tile_scale = uv_tile_scale,
                uv_stretch_instead_of_tile = uv_stretch_instead_of_tile,

                verts = _nativeVertices,
                normals = _nativeNormals,
                tangents = _nativeTangents,
                bounds = _nativeBounds,
                colors = _nativeColors,
                
                uvs = _nativeUVs,
                tris = _nativeTris,

                Points = SplineReference.NativePoints,
                Mode = SplineReference.GetSplineMode(),
                SplineSpace = SplineReference.GetSplineSpace(),
                worldToLocalMatrix = SplineReference.transform.worldToLocalMatrix,
                localToWorldMatrix = SplineReference.transform.localToWorldMatrix,
                ClosedSpline = SplineReference.GetSplineClosed(),
            };

            return job.Schedule(dependency);
        }

        [BurstCompile]
        private struct BuildMeshFromSpline_RepeatingMesh : IJob
        {
            // settings
            public int quality;
            public float built_to_t;
            public bool uv_stretch_instead_of_tile;
            public float uv_tile_scale;

            public NativeArray<int> repeatingMesh_tris;
            public NativeArray<Vector3> repeatingMesh_verts;
            public NativeArray<Vector3> repeatingMesh_normals;
            public NativeArray<Vector4> repeatingMesh_tangents;
            public NativeArray<Vector4> repeatingMesh_uv0;
            public NativeArray<Vector4> repeatingMesh_colors;
            public Bounds repeatingMesh_bounds;

            public bool repeatingMesh_has_uv0;
            public bool repeatingMesh_has_colors;

            public NativeArray<int> repeatingMesh_stitchVertsStart;
            public NativeArray<int> repeatingMesh_stitchVertsEnd;
            public bool UseRepeatingMeshUVs;
            public RepeatMode MeshRepeatMode;

            // mesh data 
            public NativeList<Vector3> verts;
            public NativeList<Vector3> normals;
            public NativeList<Vector4> tangents;
            public NativeList<Vector4> uvs;
            public NativeList<int> tris;
            public NativeList<Vector4> colors;
            public NativeArray<Bounds> bounds;
            public Vector3 MeshLocalOffsetVertices;

            // Spline data
            [ReadOnly]
            public NativeArray<SplinePoint> Points;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 worldToLocalMatrix;
            public Matrix4x4 localToWorldMatrix;
            public bool ClosedSpline;

            public void Execute()
            {
                var trackedBounds = new Bounds();

                // reset data 
                verts.Clear();
                normals.Clear();
                uvs.Clear();
                tris.Clear();
                tangents.Clear();
                colors.Clear();

                // track
                var current_uv_step = 0f;

                // setup 
                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var previousPosition = firstPoint.position;

                // closed splines overlap a bit so we dont have to stitch 
                var full_loop = ClosedSpline  && built_to_t >= 1f;
                var first_set = false;


                var repeatingBoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var repeatingBoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                for(var ri = 0; ri < repeatingMesh_verts.Length; ++ri)
                {
                    var vert = repeatingMesh_verts[ri];
                    repeatingBoundsMin = Vector3.Min(repeatingBoundsMin, vert);
                    repeatingBoundsMax = Vector3.Max(repeatingBoundsMax, vert);
                }

                var boundsDistance = Vector3.Distance(repeatingBoundsMin, repeatingBoundsMax);
                var repeatCount = 0;

                if (MeshRepeatMode == RepeatMode.PasteAndBend)
                {
                    var meshBoundsZ = (repeatingMesh_bounds.max.z - repeatingMesh_bounds.min.z);
                    var totalMeshZ = meshBoundsZ * quality;

                    for (var meshIndex = 0; meshIndex < quality; ++meshIndex)
                    {
                        var currentMeshZ = meshIndex * meshBoundsZ;

                        // pasted the mesh over and over, bending the verts to be along the spline 
                        for (var ri = 0; ri < repeatingMesh_verts.Length; ++ri)
                        {
                            var repeating_vertex = repeatingMesh_verts[ri];
                            var normal = repeatingMesh_normals[ri];
                            var tangent = repeatingMesh_tangents[ri];

                            if(repeatingMesh_has_colors)
                            {
                                var color = repeatingMesh_colors[ri];
                                colors.Add(color);
                            }

                            var meshBoundsWithInnerZ = currentMeshZ + (repeating_vertex.z - repeatingMesh_bounds.min.z); // / meshBoundsZ;
                            var innerMesh_t = meshBoundsWithInnerZ / totalMeshZ;

                            // var trs = Spline.GetLocalToWorldAtT(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, innerMesh_t);
                            var vertex_splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, innerMesh_t);
                            var trs = Matrix4x4.TRS(vertex_splinePoint.position, vertex_splinePoint.rotation, vertex_splinePoint.scale);
                            var vertex = trs.MultiplyPoint(new Vector3(repeating_vertex.x, repeating_vertex.y, 0));
                            normal = trs.MultiplyVector(normal);
                            tangent = trs.MultiplyVector(tangent);
                            
                            verts.Add(vertex);
                            normals.Add(normal);
                            tangents.Add(tangent);

                            if (UseRepeatingMeshUVs && repeatingMesh_has_uv0)
                            {
                                uvs.Add(repeatingMesh_uv0[ri]);
                            }
                            else
                            {
                                uvs.Add(new Vector4(current_uv_step, 0f));
                            }

                            // track bounds.. 
                            trackedBounds.min = Vector3.Min(trackedBounds.min, vertex);
                            trackedBounds.max = Vector3.Max(trackedBounds.max, vertex);
                        }

                        // copy/paste tris from repeatable mesh 
                        var tri_offset = repeatingMesh_verts.Length * repeatCount;
                        for (var ri = 0; ri < repeatingMesh_tris.Length; ++ri)
                        {
                            tris.Add(repeatingMesh_tris[ri] + tri_offset);
                        }

                        repeatCount++;
                    }
                }
                else
                {
                    // step through 
                    for (var step = 0; step < quality; ++step)
                    {
                        var t = (float)step / (quality - 1);

                        var final_point_from_t = false;
                        if (t > built_to_t)
                        {
                            t = built_to_t;
                            final_point_from_t = true;
                        }

                        var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                        var position = splinePoint.position;

                        // don't allow repeating to intersect, if possible..
                        if (first_set && Vector3.Distance(position, previousPosition) <= boundsDistance)
                        {
                            continue;
                        }


                        var point_trs = Spline.GetLocalToWorldAtT(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                        var point_trs_i = point_trs.inverse;

                        // uvs 
                        if (uv_stretch_instead_of_tile)
                        {
                            current_uv_step = t;
                        }
                        else
                        {
                            current_uv_step += Vector3.Distance(previousPosition, position) * uv_tile_scale;
                            current_uv_step = current_uv_step % 1.0f;
                        }

                        // copy/paste verts from repeatable mesh
                        for (var ri = 0; ri < repeatingMesh_verts.Length; ++ri)
                        {
                            var repeating_vertex = repeatingMesh_verts[ri];


                            var vertex = repeating_vertex + MeshLocalOffsetVertices;
                            vertex = point_trs.MultiplyPoint(new Vector4(vertex.x, vertex.y, vertex.z, 1.0f));

                            var normal = repeatingMesh_normals[ri];
                            normal = point_trs.MultiplyVector(normal);

                            var tangent = repeatingMesh_tangents[ri];
                            tangent = point_trs.MultiplyVector(tangent);

                            if(repeatingMesh_has_colors)
                            {
                                var color = repeatingMesh_colors[ri];
                                colors.Add(color);
                            }

                            verts.Add(new Vector3(vertex.x, vertex.y, vertex.z));
                            normals.Add(normal);
                            tangents.Add(tangent);

                            if (UseRepeatingMeshUVs && repeatingMesh_has_uv0)
                            {
                                uvs.Add(repeatingMesh_uv0[ri]);
                            }
                            else
                            {
                                uvs.Add(new Vector4(current_uv_step, 0f));
                            }

                            // track bounds.. 
                            trackedBounds.min = Vector3.Min(trackedBounds.min, vertex);
                            trackedBounds.max = Vector3.Max(trackedBounds.max, vertex);
                        }

                        // copy/paste tris from repeatable mesh 
                        var tri_offset = repeatingMesh_verts.Length * repeatCount;
                        for (var ri = 0; ri < repeatingMesh_tris.Length; ++ri)
                        {
                            tris.Add(repeatingMesh_tris[ri] + tri_offset);
                        }

                        // stitch 
                        if (first_set)
                        {

                            var tri_offset_a = repeatingMesh_verts.Length * (repeatCount - 1);
                            var tri_offset_b = repeatingMesh_verts.Length * (repeatCount - 0);

                            for (var ri = 0; ri < repeatingMesh_stitchVertsStart.Length; ri += 1)
                            {
                                var triIndex_a_0 = repeatingMesh_stitchVertsEnd[ri + 0] + tri_offset_a;
                                var triIndex_a_1 = repeatingMesh_stitchVertsEnd[(ri + 1) % repeatingMesh_stitchVertsEnd.Length] + tri_offset_a;

                                var triIndex_b_0 = repeatingMesh_stitchVertsStart[ri + 0] + tri_offset_b;
                                var triIndex_b_1 = repeatingMesh_stitchVertsStart[(ri + 1) % repeatingMesh_stitchVertsStart.Length] + tri_offset_b;

                                tris.Add(triIndex_b_0);
                                tris.Add(triIndex_b_1);
                                tris.Add(triIndex_a_0);

                                tris.Add(triIndex_a_0);
                                tris.Add(triIndex_b_1);
                                tris.Add(triIndex_a_1);
                            }
                        }

                        // stitch the current mesh copy to the previous mesh copy 
                        // dont let meshes intersect, if possible 
                        // repeat 

                        previousPosition = position;

                        first_set = true;
                        repeatCount++;

                        if (final_point_from_t)
                        {
                            break;
                        }
                    }
                }

                if(ClosedSpline && built_to_t >= 1f)
                {
                    var tri_offset_a = repeatingMesh_verts.Length * (repeatCount - 1);
                    var tri_offset_b = 0;

                    for (var ri = 0; ri < repeatingMesh_stitchVertsStart.Length; ri += 1)
                    {
                        var triIndex_a_0 = repeatingMesh_stitchVertsEnd[ri + 0] + tri_offset_a;
                        var triIndex_a_1 = repeatingMesh_stitchVertsEnd[(ri + 1) % repeatingMesh_stitchVertsEnd.Length] + tri_offset_a;

                        var triIndex_b_0 = repeatingMesh_stitchVertsStart[ri + 0] + tri_offset_b;
                        var triIndex_b_1 = repeatingMesh_stitchVertsStart[(ri + 1) % repeatingMesh_stitchVertsStart.Length] + tri_offset_b;

                        tris.Add(triIndex_b_0);
                        tris.Add(triIndex_b_1);
                        tris.Add(triIndex_a_0);

                        tris.Add(triIndex_a_0);
                        tris.Add(triIndex_b_1);
                        tris.Add(triIndex_a_1);
                    }
                }

                bounds[0] = trackedBounds;
            }
        }
    }
}

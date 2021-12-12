using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.UIElements;

namespace CorgiSpline
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter))]
    [DefaultExecutionOrder(1000)] // intended to execute AFTER spline executes 
    public class SplineMeshBuilder : MonoBehaviour
    {
        // references 
        public Spline SplineReference;

        // settings
        public bool RebuildEveryFrame;
        public bool RebuildOnEnable;
        public bool AllowAsyncRebuild;

        [Range(0f, 1f)] public float built_to_t = 1f;
        [Range(32, 1024)] public int quality = 256;
        [Range(0.001f, 10f)] public float width = 1f;
        [Range(0f, 10f)] public float height = 1f;
        public float uv_tile_scale = 1f;
        public bool cover_ends_with_quads = true;
        public bool uv_stretch_instead_of_tile;
        public bool use_splinepoint_rotations = false;
        public bool use_splinepoint_scale = false;

        // internal 
        protected Mesh _mesh;
        protected NativeList<Vector3> _nativeVertices;
        protected NativeList<Vector3> _nativeNormals;
        protected NativeList<Vector4> _nativeTangents;
        protected NativeList<Vector4> _nativeUVs;
        protected NativeArray<Bounds> _nativeBounds;
        protected NativeList<int> _nativeTris;
        protected JobHandle _previousHandle;

        protected virtual void OnEnable()
        {
            _mesh = new Mesh();

            _nativeVertices = new NativeList<Vector3>(Allocator.Persistent);
            _nativeNormals = new NativeList<Vector3>(Allocator.Persistent);
            _nativeTangents = new NativeList<Vector4>(Allocator.Persistent);
            _nativeUVs = new NativeList<Vector4>(Allocator.Persistent);
            _nativeTris = new NativeList<int>(Allocator.Persistent);
            _nativeBounds = new NativeArray<Bounds>(1, Allocator.Persistent);

            if (RebuildOnEnable)
            {
                Rebuild_Jobified();
                CompleteJob();
            }
        }

        protected virtual void OnDisable()
        {
            CompleteJob();

            _nativeVertices.Dispose();
            _nativeNormals.Dispose();
            _nativeTangents.Dispose();
            _nativeUVs.Dispose();
            _nativeTris.Dispose();
            _nativeBounds.Dispose();

            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
            }
        }

        [System.NonSerialized] private bool _asyncReadyToRebuild = true;

        protected virtual void Update()
        {
            if (RebuildEveryFrame)
            {
                if (AllowAsyncRebuild && !_asyncReadyToRebuild)
                {
                    return;
                }

                Rebuild_Jobified();
            }
        }

        protected virtual void LateUpdate()
        {
            // note: cant be as async in the editor, for editing purposes
#if UNITY_EDITOR
            if (RebuildEveryFrame && AllowAsyncRebuild)
            {
                CompleteJob();
            }
#else
            if (RebuildEveryFrame && AllowAsyncRebuild && _previousHandle.IsCompleted)
            {
                CompleteJob();
            }
#endif
        }

        public void Rebuild_Jobified()
        {
            if (SplineReference == null)
            {
                return;
            }

            if (!SplineReference.NativePoints.IsCreated)
            {
                return;
            }


            _previousHandle = ScheduleMeshingJob();
            _asyncReadyToRebuild = false;

            if (!AllowAsyncRebuild)
            {
                CompleteJob();
            }
        }

        public float _prevCompleteMs;

        public void CompleteJob()
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            _previousHandle.Complete();

            _prevCompleteMs = (float)stopwatch.ElapsedTicks / System.TimeSpan.TicksPerMillisecond;
            stopwatch.Stop();

            _mesh.Clear();

            if (_nativeVertices.Length > 3 && _nativeTris.Length > 0)
            {

                _mesh.SetVertices(_nativeVertices.AsArray());
                _mesh.SetNormals(_nativeNormals.AsArray());
                _mesh.SetUVs(0, _nativeUVs.AsArray());
                _mesh.SetTangents(_nativeTangents.AsArray());
                _mesh.SetIndices(_nativeTris.AsArray(), MeshTopology.Triangles, 0);

                // _mesh.RecalculateBounds();
                // _mesh.RecalculateTangents();

                _mesh.bounds = _nativeBounds[0];
            }

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = _mesh;

            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.sharedMesh = _mesh;
            }

            _asyncReadyToRebuild = true;
        }

        protected virtual JobHandle ScheduleMeshingJob(JobHandle dependency = default)
        {
            var job = new BuildMeshFromSpline()
            {
                quality = quality,
                width = width,
                height = height,
                uv_tile_scale = uv_tile_scale,
                uv_stretch_instead_of_tile = uv_stretch_instead_of_tile,
                use_splinepoint_rotations = use_splinepoint_rotations,
                use_splinepoint_scale = use_splinepoint_scale,

                verts = _nativeVertices,
                normals = _nativeNormals,
                tangents = _nativeTangents,
                uvs = _nativeUVs,
                tris = _nativeTris,
                bounds = _nativeBounds,

                Points = SplineReference.NativePoints,
                Mode = SplineReference.GetSplineMode(),
                SplineSpace = SplineReference.GetSplineSpace(),
                worldToLocalMatrix = SplineReference.transform.worldToLocalMatrix,
                localToWorldMatrix = SplineReference.transform.localToWorldMatrix,
                ClosedSpline = SplineReference.GetSplineClosed(),

                built_to_t = built_to_t,
                cover_ends_with_quads = cover_ends_with_quads,
            };

            return job.Schedule(dependency);
        }

        [BurstCompile]
        private struct BuildMeshFromSpline : IJob
        {
            // settings
            public int quality;
            public float width;
            public float height;
            public float uv_tile_scale;
            public float built_to_t;
            public bool cover_ends_with_quads;
            public bool uv_stretch_instead_of_tile;
            public bool use_splinepoint_rotations;
            public bool use_splinepoint_scale;

            // mesh data 
            public NativeList<Vector3> verts;
            public NativeList<Vector3> normals;
            public NativeList<Vector4> tangents;
            public NativeList<Vector4> uvs;
            public NativeList<int> tris;

            public NativeArray<Bounds> bounds;

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
                // reset data 
                verts.Clear();
                normals.Clear();
                tangents.Clear();
                uvs.Clear();
                tris.Clear();

                // track
                var trackedBounds = new Bounds();

                var current_uv_step = 0f;

                // setup 
                var start_forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var end_forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, Mathf.Max(0f, built_to_t - 0.1f));

                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var lastPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, built_to_t);
                var previousPosition = firstPoint.position;

                // ensure even quality 
                quality = quality - quality % 2;

                // closed splines overlap a bit so we dont have to stitch 
                var until_quality = quality;

                var full_loop = ClosedSpline && built_to_t >= 1f;
                var first_set = false;

                // hack for overlapping bezier when using a closed spline.. 
                if (ClosedSpline && Mode == SplineMode.BSpline)
                {
                    built_to_t = Mathf.Clamp(built_to_t, 0, 0.95f);
                }

                // step through 
                for (var step = 0; step < until_quality; ++step)
                {
                    var t = (float)step / quality;
                    //    t *= built_to_t;

                    var final_point_from_t = false;
                    if (t > built_to_t)
                    {
                        t = built_to_t;
                        final_point_from_t = true;
                    }

                    var up = Vector3.up;
                    var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var position = splinePoint.position;
                    var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var right = Vector3.Cross(forward, up);

                    if (use_splinepoint_rotations)
                    {
                        up = splinePoint.rotation * Vector3.up;
                        forward = splinePoint.rotation * Vector3.forward;

                        right = Vector3.Cross(forward, up);
                    }

                    var localWidth = width;

                    if (use_splinepoint_scale)
                    {
                        localWidth *= splinePoint.scale.x;
                    }

                    // skip if too close.. 
                    if (first_set && step != 0 && step != until_quality - 1 && Vector3.Distance(previousPosition, position) < 0.2f)
                    {
                        continue;
                    }

                    // verts 
                    var vert0 = position - right * localWidth;
                    var vert1 = position + right * localWidth;

                    verts.Add(vert0);
                    verts.Add(vert1);

                    // normals 
                    var normal0 = up;
                    var normal1 = up;

                    normals.Add(normal0);
                    normals.Add(normal1);

                    var tangent0 = new Vector4(right.x, right.y, right.z, 1.0f);
                    var tangent1 = new Vector4(right.x, right.y, right.z, 1.0f);

                    tangents.Add(tangent0);
                    tangents.Add(tangent1);

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

                    uvs.Add(new Vector2(0f, current_uv_step));
                    uvs.Add(new Vector2(1f, current_uv_step));

                    previousPosition = position;
                    first_set = true;

                    // track bounds.. 
                    trackedBounds.min = Vector3.Min(trackedBounds.min, vert0);
                    trackedBounds.min = Vector3.Min(trackedBounds.min, vert1);

                    trackedBounds.max = Vector3.Max(trackedBounds.max, vert0);
                    trackedBounds.max = Vector3.Max(trackedBounds.max, vert1);

                    if (final_point_from_t)
                    {
                        break;
                    }
                }

                // stich
                if (cover_ends_with_quads && full_loop)
                {
                    var offset_end = verts.Length - 2;

                    verts.Add(verts[0]);
                    verts.Add(verts[1]);

                    normals.Add(normals[0]);
                    normals.Add(normals[1]);

                    tangents.Add(tangents[0]);
                    tangents.Add(tangents[1]);

                    uvs.Add(uvs[offset_end + 0]);
                    uvs.Add(uvs[offset_end + 1]);
                }

                // generate tris 
                for (var v = 0; v < verts.Length - 2; v += 4)
                {
                    tris.Add(v + 0);
                    tris.Add(v + 1);
                    tris.Add(v + 3);
                    tris.Add(v + 0);
                    tris.Add(v + 3);
                    tris.Add(v + 2);

                    if (v < verts.Length - 4)
                    {
                        tris.Add(v + 2);
                        tris.Add(v + 3);
                        tris.Add(v + 4);
                        tris.Add(v + 4);
                        tris.Add(v + 3);
                        tris.Add(v + 5);
                    }
                }

                var floor_vert_index = verts.Length;

                if (height > 0f)
                {

                    // floor 
                    for (var v = 0; v < floor_vert_index; ++v)
                    {
                        var new_vert = verts[v];
                        var new_normal = normals[v] * -1f;
                        var new_uv = uvs[v];
                        var new_tagent = tangents[v] * -1f;
                        new_tagent.w = 1.0f;

                        new_uv.x = 1.0f - new_uv.x;
                        // new_uv.y = 1.0f - new_uv.y;

                        var localHeight = height;

                        if (use_splinepoint_scale)
                        {
                            var splinePoint_t = Spline.JobSafe_ProjectOnSpline_t(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, new_vert);
                            var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, splinePoint_t);
                            var splineScale = splinePoint.scale;
                            localHeight *= splineScale.y;
                        }

                        new_vert += new_normal * localHeight;

                        verts.Add(new_vert);
                        normals.Add(new_normal);
                        tangents.Add(new_tagent);
                        uvs.Add(new_uv);


                        // track bounds.. 
                        trackedBounds.min = Vector3.Min(trackedBounds.min, new_vert);
                        trackedBounds.max = Vector3.Max(trackedBounds.max, new_vert);
                    }

                    // generate triangles
                    for (var v = floor_vert_index; v < verts.Length - 2; v += 4)
                    {
                        tris.Add(v + 2);
                        tris.Add(v + 3);
                        tris.Add(v + 0);

                        tris.Add(v + 3);
                        tris.Add(v + 1);
                        tris.Add(v + 0);

                        if (v < verts.Length - 4)
                        {
                            tris.Add(v + 5);
                            tris.Add(v + 3);
                            tris.Add(v + 4);

                            tris.Add(v + 4);
                            tris.Add(v + 3);
                            tris.Add(v + 2);
                        }
                    }

                    // wall triangles 
                    for (var v = 0; v < floor_vert_index - 2; v += 4)
                    {
                        // right wall 
                        tris.Add(v + floor_vert_index + 1);
                        tris.Add(v + 3);
                        tris.Add(v + 1);

                        tris.Add(v + 3);
                        tris.Add(v + floor_vert_index + 1);
                        tris.Add(v + floor_vert_index + 3);

                        // left wall
                        tris.Add(v + floor_vert_index + 0);
                        tris.Add(v + 0);
                        tris.Add(v + 2);

                        tris.Add(v + floor_vert_index + 2);
                        tris.Add(v + floor_vert_index + 0);
                        tris.Add(v + 2);

                        if (v < floor_vert_index - 4)
                        {

                            // right wall 
                            tris.Add(v + floor_vert_index + 1 + 2);
                            tris.Add(v + 3 + 2);
                            tris.Add(v + 1 + 2);
                            tris.Add(v + 3 + 2);
                            tris.Add(v + floor_vert_index + 1 + 2);
                            tris.Add(v + floor_vert_index + 3 + 2);

                            // left wall
                            tris.Add(v + floor_vert_index + 0 + 2);
                            tris.Add(v + 0 + 2);
                            tris.Add(v + 2 + 2);
                            tris.Add(v + floor_vert_index + 2 + 2);
                            tris.Add(v + floor_vert_index + 0 + 2);
                            tris.Add(v + 2 + 2);
                        }
                    }

                    // end cap triangles 
                    if (cover_ends_with_quads)
                    {
                        var start_index = 0;

                        // left cap 
                        tris.Add(floor_vert_index + 0);
                        tris.Add(start_index + 1);
                        tris.Add(start_index + 0);
                        tris.Add(floor_vert_index + 0);
                        tris.Add(floor_vert_index + 1);
                        tris.Add(start_index + 1);

                        // right cap 
                        var end_cap_top = floor_vert_index - 2;
                        var end_cap_bottom = verts.Length - 2;

                        tris.Add(end_cap_top + 0);
                        tris.Add(end_cap_top + 1);
                        tris.Add(end_cap_bottom + 0);
                        tris.Add(end_cap_top + 1);
                        tris.Add(end_cap_bottom + 1);
                        tris.Add(end_cap_bottom + 0);
                    }
                }

                // remember bounds 
                bounds[0] = trackedBounds;
            }
        }
    }
}

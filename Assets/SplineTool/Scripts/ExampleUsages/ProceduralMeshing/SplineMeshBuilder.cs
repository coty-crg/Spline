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
        // [Header("Spline")]
        public Spline SplineReference;

        // settings
        // [Header("SplineMeshBuilder Settings")]
        [Tooltip("Re-run this script every frame. Useful for dynamic splines.")] 
        public bool RebuildEveryFrame;

        [Tooltip("Have this script run at least once, when the object is enabled.")] 
        public bool RebuildOnEnable;

        [Tooltip("Attempts to run the mesh builder script off the main thread, so the game is not slowed down.")] 
        public bool AllowAsyncRebuild;

        [Tooltip("Stop building the mesh at this % of the spline.")]
        [Range(0f, 1f)] public float built_to_t = 1f;

        [Tooltip("Visual quality of the mesh along the spline. Higher values look nicer but are slower to compute.")]
        [Range(32, 1024)] public int quality = 256;

        // [Tooltip("Width of the mesh along each spline node's local x axis.")]
        // [Range(0.001f, 10f)] public float width = 1f;
        // 
        // [Tooltip("Height of the mesh along each spline node's local y axis.")]
        // [Range(0f, 10f)] public float height = 1f;

        [Tooltip("Scale the local space spline samples during meshing. Affects different mesh builders in different ways.")] 
        public Vector3 scaleMult = Vector3.one;

        [Tooltip("Offset all vertices from the spline locally by this offset")]
        public Vector3 vertexOffset = Vector3.zero;

        [Tooltip("UV tiling scale of the mesh along the spline, if applicable.")]
        public float uv_tile_scale = 1f;

        [Tooltip("If true, non-closed splines will have their ends covered with a cap, when applicable.")]
        public bool cover_ends_with_quads = true;

        // [Tooltip("Instead of tiling the UVs, stretch them along the spline.")]
        // public bool uv_stretch_instead_of_tile;

        [Tooltip("Should the UVs of the procedural mesh stretch or tile. Note: tiled UVs can only tile as much as the mesh quality can allow (tiles can only happen at each new segment).")]
        public MeshBuilderUVs UVsMode = MeshBuilderUVs.Stretch;

        [Tooltip("When calculating rotations, use the spline point data.")]
        public bool use_splinepoint_rotations = false;

        [Tooltip("When calculating scale, use the spline point data.")]
        public bool use_splinepoint_scale = false;

        [HideInInspector] public Mesh _serializedMesh;


        [System.Serializable]
        public enum MeshBuilderNormals
        {
            Smooth = 0,
            Hard = 1,
        }

        [System.Serializable]
        public enum MeshBuilderUVs
        {
            Stretch     = 0,
            Tile        = 1,
        }

        [Tooltip("Should the normals be smooth or hard? Note: hard normals require more vertices.")]
        public MeshBuilderNormals MeshNormalsMode = MeshBuilderNormals.Smooth;

        // internal 
        protected Mesh _mesh;
        protected NativeList<Vector3> _nativeVertices;
        protected NativeList<Vector3> _nativeNormals;
        protected NativeList<Vector4> _nativeTangents;
        protected NativeList<Vector4> _nativeUV0;
        protected NativeList<Vector4> _nativeUV1;
        protected NativeArray<Bounds> _nativeBounds;
        protected NativeList<Vector4> _nativeColors;
        protected NativeList<int> _nativeTris;
        protected JobHandle _previousHandle;

        private float _prevCompleteMs;
        private bool _asyncReadyToRebuild = true;
        private bool _hasScheduledJob;

        protected virtual void OnEnable()
        {
            Debug.Assert(!(Application.isPlaying && SplineReference == null), "SplineReference is null", gameObject);

            if (_serializedMesh != null)
            {
                _mesh = _serializedMesh;

                if(Application.isPlaying)
                {
                    this.enabled = false; 
                    return; 
                }
            }
            else
            {
                _mesh = new Mesh();
            }

            _nativeVertices = new NativeList<Vector3>(Allocator.Persistent);
            _nativeNormals = new NativeList<Vector3>(Allocator.Persistent);
            _nativeTangents = new NativeList<Vector4>(Allocator.Persistent);
            _nativeUV0 = new NativeList<Vector4>(Allocator.Persistent);
            _nativeUV1 = new NativeList<Vector4>(Allocator.Persistent);
            _nativeTris = new NativeList<int>(Allocator.Persistent);
            _nativeBounds = new NativeArray<Bounds>(1, Allocator.Persistent);
            _nativeColors = new NativeList<Vector4>(Allocator.Persistent);

            if(RebuildOnEnable)
            {
                Rebuild_Jobified();
                CompleteJob();
            }
        }

        protected virtual void OnDisable()
        {
            CompleteJob();

            if(_nativeVertices.IsCreated)
            {
                _nativeVertices.Dispose();
                _nativeNormals.Dispose();
                _nativeTangents.Dispose(); 
                _nativeUV0.Dispose();
                _nativeUV1.Dispose();
                _nativeTris.Dispose();
                _nativeBounds.Dispose();
                _nativeColors.Dispose();
            }

            if(_serializedMesh == null)
            {
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
        }

        protected virtual void Update()
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)
            {
                if(UnityEditor.Selection.activeGameObject == gameObject)
                {
                    Rebuild_Jobified();
                    CompleteJob(); 
                }

                return;
            }
#endif

            if (RebuildEveryFrame)
            {
                if(AllowAsyncRebuild && !_asyncReadyToRebuild)
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
            if(!Application.isPlaying)
            {
                return;
            }

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

        /// <summary>
        /// Used by the custom inspectors to force a main thread rebuild.
        /// </summary>
        public void ForceImmediateRebuild()
        {
            // finish any ongoing stuff
            CompleteJob();

            // force remesh and complete immediately 
            Rebuild_Jobified();
            CompleteJob(); 
        }

        /// <summary>
        /// Schedules the meshing job. If async, it will be completed in CompleteJob() later.
        /// </summary>
        public void Rebuild_Jobified()
        {
            if (SplineReference == null || SplineReference.GetPointCountIgnoreHandles() == 0)
            {
                return;
            }

            if (!SplineReference.NativePoints.IsCreated)
            {
                return;
            }

            _previousHandle = ScheduleMeshingJob();
            _asyncReadyToRebuild = false;
            _hasScheduledJob = true; 

            if (!AllowAsyncRebuild)
            {
                CompleteJob();
            }
        }

        /// <summary>
        /// Completes a meshing job. 
        /// </summary>
        public void CompleteJob()
        {
            if(!_hasScheduledJob)
            {
                return;
            }

            _hasScheduledJob = false;

            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            _previousHandle.Complete();

            _prevCompleteMs = (float) stopwatch.ElapsedTicks / System.TimeSpan.TicksPerMillisecond;
            stopwatch.Stop();

            _mesh.Clear();

            if(_nativeVertices.Length > 3 && _nativeTris.Length > 0)
            {
                _mesh.SetVertices(_nativeVertices.AsArray());
                _mesh.SetNormals(_nativeNormals.AsArray());
                _mesh.SetTangents(_nativeTangents.AsArray()); 
                _mesh.SetIndices(_nativeTris.AsArray(), MeshTopology.Triangles, 0);

                if (_nativeUV0.Length > 0)
                {
                    _mesh.SetUVs(0, _nativeUV0.AsArray());
                }

                if (_nativeUV1.Length > 0)
                {
                    _mesh.SetUVs(1, _nativeUV1.AsArray());
                }

                if (_nativeColors.Length > 0)
                {
                    _mesh.SetColors(_nativeColors.AsArray());
                }

                // _mesh.RecalculateBounds();
                // _mesh.RecalculateTangents();

                _mesh.bounds = _nativeBounds[0];
            }

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = _mesh;

            var meshCollider = GetComponent<MeshCollider>();
            if(meshCollider != null)
            {
                meshCollider.sharedMesh = _mesh;
            }

            _asyncReadyToRebuild = true;
        }

        protected void DetermineSplineSettings(out Space splineSpace, out Matrix4x4 localToWorldMatrix, out Matrix4x4 worldToLocalMatrix)
        {
            var splineOnGameobject = GetComponent<Spline>() != null;
            if (splineOnGameobject)
            {
                splineSpace = Space.World;
                localToWorldMatrix = Matrix4x4.TRS(Vector3.zero, SplineReference.transform.rotation, SplineReference.transform.localScale);
                worldToLocalMatrix = localToWorldMatrix.inverse;
            }
            else
            {
                splineSpace = SplineReference.GetSplineSpace();
                worldToLocalMatrix = SplineReference.transform.worldToLocalMatrix;
                localToWorldMatrix = SplineReference.transform.localToWorldMatrix;
            }
        }

        /// <summary>
        /// This is what schedules the actual IJob for the meshing. 
        /// Override this to easily implement new meshing algorithms.
        /// </summary>
        /// <param name="dependency"></param>
        /// <returns></returns>
        protected virtual JobHandle ScheduleMeshingJob(JobHandle dependency = default)
        {
            DetermineSplineSettings(out Space splineSpace, out Matrix4x4 localToWorldMatrix, out Matrix4x4 worldToLocalMatrix);

            var job = new BuildMeshFromSpline()
            {
                quality = quality,
                width = scaleMult.x,
                height = scaleMult.y,
                uv_tile_scale = uv_tile_scale,
                use_splinepoint_rotations = use_splinepoint_rotations,
                use_splinepoint_scale = use_splinepoint_scale,
                vertexOffset = vertexOffset,
                normalsMode = MeshNormalsMode,
                uvsMode = UVsMode,

                verts = _nativeVertices,
                normals = _nativeNormals,
                tangents = _nativeTangents,
                uvs0 = _nativeUV0,
                uvs1 = _nativeUV1,
                tris = _nativeTris,
                bounds = _nativeBounds,
                colors = _nativeColors,

                Points = SplineReference.NativePoints,
                Mode = SplineReference.GetSplineMode(),
                ClosedSpline = SplineReference.GetSplineClosed(),

                SplineSpace = splineSpace,
                worldToLocalMatrix = worldToLocalMatrix,
                localToWorldMatrix = localToWorldMatrix,

                built_to_t = built_to_t,
                cover_ends_with_quads = cover_ends_with_quads,
            };

            return job.Schedule(dependency);
        }

        /// <summary>
        /// Returns the current mesh created from this script. 
        /// </summary>
        /// <returns></returns>
        public Mesh GetMesh()
        {
            if(_serializedMesh != null)
            {
                return _serializedMesh;
            }

            return _mesh; 
        }

        /// <summary>
        /// Returns the duration in ms for how long it took to generate the mesh, the last time this script was used. 
        /// </summary>
        /// <returns></returns>
        public float GetPreviousMeshingDurationMs()
        {
            return _prevCompleteMs;
        }

        /// <summary>
        /// Discard the current mesh. 
        /// </summary>
        public void ResetMesh()
        {
            _serializedMesh = null;

            if (_mesh != null)
            {
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(_mesh)))
                {
                    _mesh = null;
                    _serializedMesh = null;
                }

                if(!Application.isPlaying)
                {
                    DestroyImmediate(_mesh);
                }
                else
                {
                    Destroy(_mesh);
                }
#else
                    Destroy(_mesh);
#endif
            }

            _mesh = new Mesh(); 
        }

        /// <summary>
        /// Sets our _sharedMesh and configures anything necessary from this change. 
        /// </summary>
        /// <param name="mesh"></param>
        public void ConfigureSerializedMesh(Mesh mesh)
        {
            _serializedMesh = mesh;
            _mesh = mesh;

            RebuildEveryFrame = false;
            RebuildOnEnable = false; 
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
            public bool use_splinepoint_rotations;
            public bool use_splinepoint_scale;
            public Vector3 vertexOffset;
            public MeshBuilderNormals normalsMode;
            public MeshBuilderUVs uvsMode;

            // mesh data 
            public NativeList<Vector3> verts;
            public NativeList<Vector3> normals;
            public NativeList<Vector4> tangents;
            public NativeList<Vector4> uvs0;
            public NativeList<Vector4> uvs1;
            public NativeList<int> tris;
            public NativeList<Vector4> colors;

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
                uvs0.Clear();
                uvs1.Clear();
                tris.Clear();
                colors.Clear();

                // track
                var trackedBounds = new Bounds();

                // setup 
                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var lastPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 1f);

                var firstForward = firstPoint.rotation * Vector3.forward;
                var firstRight = firstPoint.rotation * Vector3.right;
                var lastForward = lastPoint.rotation * Vector3.forward;
                var lastRight = lastPoint.rotation * Vector3.right;

                var previousPosition = firstPoint.position;

                // ensure even quality 
                quality = quality - quality % 2;

                // closed splines overlap a bit so we dont have to stitch 
                var until_quality = quality;

                var full_loop = ClosedSpline && built_to_t >= 1f;
                var first_set = false;

                // hack for overlapping bezier when using a closed spline.. 
                // if(ClosedSpline && Mode == SplineMode.BSpline)
                // {
                //     built_to_t = Mathf.Clamp(built_to_t, 0, 0.95f);
                // }

                var groupVertCount = normalsMode == MeshBuilderNormals.Hard ? 8 : 4;

                // step through 
                for (var step = 0; step <= until_quality; ++step)
                {
                    var t = (float) step / quality;
                    //    t *= built_to_t;

                    var final_point_from_t = false;
                    if(t > built_to_t)
                    {
                        t = built_to_t;
                        final_point_from_t = true; 
                    }

                    var up = Vector3.up;
                    var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var position = splinePoint.position + vertexOffset;
                    var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var right = Vector3.Cross(forward, up); 

                    if(use_splinepoint_rotations)
                    {
                        up = splinePoint.rotation * Vector3.up;
                        forward = splinePoint.rotation * Vector3.forward;
                        right = Vector3.Cross(forward, up);
                    }

                    var localWidth = width * 0.5f;
                    var localHeight = height * 0.5f;

                    if(use_splinepoint_scale)
                    {
                        localWidth *= splinePoint.scale.x;
                        localHeight *= splinePoint.scale.y; 
                    }


                    // skip if too close.. 
                    // if(first_set && step != 0 && step != until_quality - 1 && Vector3.Distance(previousPosition, position) < 0.2f)
                    // {
                    //     continue; 
                    // }

                    // [idea from Wave Break]
                    // note: repeating some verts so we can have hard edges (dont interpolate normals)
                    //                  ____________
                    //                 /           /|
                    //                /           / |
                    //            v0 /________v1_/  |
                    //              |           |   |
                    //              |           |   |
                    //              |           |  / 
                    //              |           | /
                    //           v2 |________v3_|/
                    //

                    var vert0 = position + right * localWidth + up * localHeight;
                    var vert1 = position - right * localWidth + up * localHeight; 
                    var vert2 = position + right * localWidth - up * localHeight;
                    var vert3 = position - right * localWidth - up * localHeight;
                    var vert4 = vert0;
                    var vert5 = vert2;
                    var vert6 = vert1;
                    var vert7 = vert3;

                    var normal0 = up;
                    var normal1 = up;
                    var normal2 = -up;
                    var normal3 = -up;
                    var normal4 = -right;
                    var normal5 = -right;
                    var normal6 = right;
                    var normal7 = right;

                    var tangent0 = right;
                    var tangent1 = right;
                    var tangent2 = -right;
                    var tangent3 = -right;
                    var tangent4 = up;
                    var tangent5 = up;
                    var tangent6 = -up;
                    var tangent7 = -up;

                    var uv_x = t;

                    if (uvsMode == MeshBuilderUVs.Tile)
                    {
                        uv_x = (t * uv_tile_scale) % 1.0f;
                    }

                    var uv0_0 = new Vector2(uv_x, 0f);
                    var uv0_1 = new Vector2(uv_x, 1f);
                    var uv0_2 = new Vector2(uv_x, 1f);
                    var uv0_3 = new Vector2(uv_x, 0f);
                    var uv0_4 = uv0_0;
                    var uv0_5 = uv0_2;
                    var uv0_6 = uv0_1;
                    var uv0_7 = uv0_3;

                    var uv1_0 = new Vector2(t, 0f / 6f);
                    var uv1_1 = new Vector2(t, 1f / 6f);
                    var uv1_2 = new Vector2(t, 1f / 6f);
                    var uv1_3 = new Vector2(t, 2f / 6f);
                    var uv1_4 = new Vector2(t, 2f / 6f);
                    var uv1_5 = new Vector2(t, 3f / 6f);
                    var uv1_6 = new Vector2(t, 3f / 6f);
                    var uv1_7 = new Vector2(t, 4f / 6f);

                    verts.Add(vert0);
                    verts.Add(vert1);
                    verts.Add(vert2);
                    verts.Add(vert3);

                    normals.Add(normal0);
                    normals.Add(normal1);
                    normals.Add(normal2);
                    normals.Add(normal3);

                    tangents.Add(tangent0);
                    tangents.Add(tangent1);
                    tangents.Add(tangent2);
                    tangents.Add(tangent3);

                    uvs0.Add(uv0_0);
                    uvs0.Add(uv0_1);
                    uvs0.Add(uv0_2);
                    uvs0.Add(uv0_3);

                    uvs1.Add(uv1_0);
                    uvs1.Add(uv1_1);
                    uvs1.Add(uv1_2);
                    uvs1.Add(uv1_3);

                    if (normalsMode == MeshBuilderNormals.Hard)
                    {
                        verts.Add(vert4);
                        verts.Add(vert5);
                        verts.Add(vert6);
                        verts.Add(vert7);

                        normals.Add(normal4);
                        normals.Add(normal5);
                        normals.Add(normal6);
                        normals.Add(normal7);

                        tangents.Add(tangent4);
                        tangents.Add(tangent5);
                        tangents.Add(tangent6);
                        tangents.Add(tangent7);

                        uvs0.Add(uv0_4);
                        uvs0.Add(uv0_5);
                        uvs0.Add(uv0_6);
                        uvs0.Add(uv0_7);

                        uvs1.Add(uv1_4);
                        uvs1.Add(uv1_5);
                        uvs1.Add(uv1_6);
                        uvs1.Add(uv1_7);
                    }

                    // connect the dots 
                    if (step > 0)
                    {
                        // gather indices 
                        var i = verts.Length - groupVertCount * 2;

                        var prev_t0 = i + 0;
                        var prev_t1 = i + 1;
                        var prev_b0 = i + 2;
                        var prev_b1 = i + 3;
                        var prev_l0 = i + 0;
                        var prev_l1 = i + 2;
                        var prev_r0 = i + 1;
                        var prev_r1 = i + 3;

                        var curr_t0 = i + groupVertCount + 0;
                        var curr_t1 = i + groupVertCount + 1;
                        var curr_b0 = i + groupVertCount + 2;
                        var curr_b1 = i + groupVertCount + 3;
                        var curr_l0 = i + groupVertCount + 0;
                        var curr_l1 = i + groupVertCount + 2;
                        var curr_r0 = i + groupVertCount + 1;
                        var curr_r1 = i + groupVertCount + 3;

                        if(normalsMode == MeshBuilderNormals.Hard)
                        {
                            prev_l0 = i + 4;
                            prev_l1 = i + 5;
                            prev_r0 = i + 6;
                            prev_r1 = i + 7;

                            curr_l0 = i + groupVertCount + 4;
                            curr_l1 = i + groupVertCount + 5;
                            curr_r0 = i + groupVertCount + 6;
                            curr_r1 = i + groupVertCount + 7;
                        }

                        // top quad 
                        tris.Add(curr_t1);
                        tris.Add(prev_t1);
                        tris.Add(prev_t0);

                        tris.Add(curr_t0);
                        tris.Add(curr_t1);
                        tris.Add(prev_t0);

                        // bottom quad
                        tris.Add(prev_b0);
                        tris.Add(prev_b1);
                        tris.Add(curr_b1);

                        tris.Add(prev_b0);
                        tris.Add(curr_b1);
                        tris.Add(curr_b0);

                        // left wall 
                        tris.Add(prev_l0);
                        tris.Add(prev_l1);
                        tris.Add(curr_l1);

                        tris.Add(prev_l0);
                        tris.Add(curr_l1);
                        tris.Add(curr_l0);

                        // right wall 
                        tris.Add(curr_r1);
                        tris.Add(prev_r1);
                        tris.Add(prev_r0);

                        tris.Add(curr_r0);
                        tris.Add(curr_r1);
                        tris.Add(prev_r0);
                    }

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

                // remember bounds 
                bounds[0] = trackedBounds;

                var first_index = 0;
                var last_index = verts.Length - groupVertCount;

                // caps 
                if(!ClosedSpline && cover_ends_with_quads)
                {
                    // start cap 
                    var front_vertex_t0 = verts[first_index + 0];
                    var front_vertex_t1 = verts[first_index + 1];
                    var front_vertex_b0 = verts[first_index + 2];
                    var front_vertex_b1 = verts[first_index + 3];

                    var front_normal_0 = -firstForward;
                    var front_normal_1 = -firstForward;
                    var front_normal_2 = -firstForward;
                    var front_normal_3 = -firstForward;

                    var front_tangent_0 = firstRight;
                    var front_tangent_1 = firstRight;
                    var front_tangent_2 = firstRight;
                    var front_tangent_3 = firstRight;

                    var front_uv0_0 = new Vector4(0f, 0f, 0f, 0f);
                    var front_uv0_1 = new Vector4(1f, 0f, 0f, 0f);
                    var front_uv0_2 = new Vector4(0f, 1f, 0f, 0f);
                    var front_uv0_3 = new Vector4(1f, 1f, 0f, 0f);

                    var front_uv1_0 = new Vector4(0f, 4f / 6f, 0f, 0f);
                    var front_uv1_1 = new Vector4(1f, 4f / 6f, 0f, 0f);
                    var front_uv1_2 = new Vector4(0f, 5f / 6f, 0f, 0f);
                    var front_uv1_3 = new Vector4(1f, 5f / 6f, 0f, 0f);

                    var f_t0 = last_index + groupVertCount + 0;
                    var f_t1 = last_index + groupVertCount + 1;
                    var f_b0 = last_index + groupVertCount + 2;
                    var f_b1 = last_index + groupVertCount + 3;

                    verts.Add(front_vertex_t0);
                    verts.Add(front_vertex_t1);
                    verts.Add(front_vertex_b0);
                    verts.Add(front_vertex_b1);

                    normals.Add(front_normal_0);
                    normals.Add(front_normal_1);
                    normals.Add(front_normal_2);
                    normals.Add(front_normal_3);

                    tangents.Add(front_tangent_0);
                    tangents.Add(front_tangent_1);
                    tangents.Add(front_tangent_2);
                    tangents.Add(front_tangent_3);

                    uvs0.Add(front_uv0_0);
                    uvs0.Add(front_uv0_1);
                    uvs0.Add(front_uv0_2);
                    uvs0.Add(front_uv0_3);

                    uvs1.Add(front_uv1_0);
                    uvs1.Add(front_uv1_1);
                    uvs1.Add(front_uv1_2);
                    uvs1.Add(front_uv1_3);

                    tris.Add(f_b0);
                    tris.Add(f_t1);
                    tris.Add(f_b1);
                    tris.Add(f_b0);
                    tris.Add(f_t0);
                    tris.Add(f_t1);

                    // end cap 
                    var back_vertex_t0 = verts[last_index + 0];
                    var back_vertex_t1 = verts[last_index + 1];
                    var back_vertex_b0 = verts[last_index + 2];
                    var back_vertex_b1 = verts[last_index + 3];

                    var back_normal_0 = lastForward;
                    var back_normal_1 = lastForward;
                    var back_normal_2 = lastForward;
                    var back_normal_3 = lastForward;

                    var back_tangent_0 = lastRight;
                    var back_tangent_1 = lastRight;
                    var back_tangent_2 = lastRight;
                    var back_tangent_3 = lastRight;

                    var back_uv0_0 = new Vector4(0f, 0f, 0f, 0f);
                    var back_uv0_1 = new Vector4(1f, 0f, 0f, 0f);
                    var back_uv0_2 = new Vector4(0f, 1f, 0f, 0f);
                    var back_uv0_3 = new Vector4(1f, 1f, 0f, 0f);

                    var back_uv1_0 = new Vector4(0f, 5f / 6f, 0f, 0f);
                    var back_uv1_1 = new Vector4(1f, 5f / 6f, 0f, 0f);
                    var back_uv1_2 = new Vector4(0f, 6f / 6f, 0f, 0f);
                    var back_uv1_3 = new Vector4(1f, 6f / 6f, 0f, 0f);

                    var b_t0 = last_index + groupVertCount + 4 + 0;
                    var b_t1 = last_index + groupVertCount + 4 + 1;
                    var b_b0 = last_index + groupVertCount + 4 + 2;
                    var b_b1 = last_index + groupVertCount + 4 + 3;

                    verts.Add(back_vertex_t0);
                    verts.Add(back_vertex_t1);
                    verts.Add(back_vertex_b0);
                    verts.Add(back_vertex_b1);

                    normals.Add(back_normal_0);
                    normals.Add(back_normal_1);
                    normals.Add(back_normal_2);
                    normals.Add(back_normal_3);

                    tangents.Add(back_tangent_0);
                    tangents.Add(back_tangent_1);
                    tangents.Add(back_tangent_2);
                    tangents.Add(back_tangent_3);

                    uvs0.Add(back_uv0_0);
                    uvs0.Add(back_uv0_1);
                    uvs0.Add(back_uv0_2);
                    uvs0.Add(back_uv0_3);

                    uvs1.Add(back_uv1_0);
                    uvs1.Add(back_uv1_1);
                    uvs1.Add(back_uv1_2);
                    uvs1.Add(back_uv1_3); 

                    tris.Add(b_b1);
                    tris.Add(b_t1);
                    tris.Add(b_b0);
                    tris.Add(b_t1);
                    tris.Add(b_t0);
                    tris.Add(b_b0);
                }
            }
        }
    }
}

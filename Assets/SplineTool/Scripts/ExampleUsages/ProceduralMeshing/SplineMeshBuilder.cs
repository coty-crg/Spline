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
        public bool AllowAsyncRebuild;

        [Range(0f, 1f)] public float built_to_t = 1f;
        [Range(32, 1024)] public int quality = 256;
        [Range(0.001f, 10f)] public float width = 1f;
        [Range(0f, 10f)] public float height = 1f;
        public float uv_tile_scale = 1f;
        public bool cover_ends_with_quads = true;
        public bool uv_stretch_instead_of_tile;

        // internal 
        protected Mesh _mesh;
        protected NativeList<Vector3> _nativeVertices;
        protected NativeList<Vector3> _nativeNormals;
        protected NativeList<Vector4> _nativeUVs;
        protected NativeList<int> _nativeTris;
        protected JobHandle _previousHandle;

        protected virtual void OnEnable()
        {
            _mesh = new Mesh();

            _nativeVertices = new NativeList<Vector3>(Allocator.Persistent);
            _nativeNormals = new NativeList<Vector3>(Allocator.Persistent);
            _nativeUVs = new NativeList<Vector4>(Allocator.Persistent);
            _nativeTris = new NativeList<int>(Allocator.Persistent);

            // Rebuild_Jobified();
        }

        protected virtual void OnDisable()
        {
            CompleteJob();

            _nativeVertices.Dispose();
            _nativeNormals.Dispose();
            _nativeUVs.Dispose();
            _nativeTris.Dispose();

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

        protected virtual void Update()
        {
            if (RebuildEveryFrame)
            {
                Rebuild_Jobified();
            }
        }

        protected virtual void LateUpdate()
        {
            if (RebuildEveryFrame && AllowAsyncRebuild)
            {
                CompleteJob();
            }
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

            if (!AllowAsyncRebuild)
            {
                CompleteJob();
            }
        }

        public void CompleteJob()
        {
            _previousHandle.Complete();

            _mesh.Clear();

            if(_nativeVertices.Length > 3 && _nativeTris.Length > 0)
            {
                _mesh.SetVertices(_nativeVertices.AsArray());
                _mesh.SetNormals(_nativeNormals.AsArray());
                _mesh.SetUVs(0, _nativeUVs.AsArray());
                _mesh.SetIndices(_nativeTris.AsArray(), MeshTopology.Triangles, 0);
                _mesh.RecalculateBounds();
                _mesh.RecalculateTangents();
            }

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = _mesh;
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

                verts = _nativeVertices,
                normals = _nativeNormals,
                uvs = _nativeUVs,
                tris = _nativeTris,

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

            // mesh data 
            public NativeList<Vector3> verts;
            public NativeList<Vector3> normals;
            public NativeList<Vector4> uvs;
            public NativeList<int> tris;

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
                uvs.Clear();
                tris.Clear();

                // track
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
                if(ClosedSpline && Mode == SplineMode.BSpline)
                {
                    built_to_t = Mathf.Clamp(built_to_t, 0, 0.95f);
                }

                // step through 
                for (var step = 0; step < until_quality; ++step)
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
                    var position = splinePoint.position;
                    var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var right = Vector3.Cross(forward, up); 

                    // skip if too close.. 
                    if(first_set && step != 0 && step != until_quality - 1 && Vector3.Distance(previousPosition, position) < 0.2f)
                    {
                        continue; 
                    }

                    // verts 
                    var vert0 = position - right * width;
                    var vert1 = position + right * width;

                    verts.Add(vert0);
                    verts.Add(vert1);

                    // normals 
                    var normal0 = up;
                    var normal1 = up;

                    normals.Add(normal0);
                    normals.Add(normal1);

                    // uvs 
                    if(uv_stretch_instead_of_tile)
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

                    if (final_point_from_t)
                    {
                        break; 
                    }
                }

                // stich
                if(cover_ends_with_quads && full_loop)
                {
                    var offset_end = verts.Length - 2;

                    verts.Add(verts[0]);
                    verts.Add(verts[1]);

                    normals.Add(normals[0]);
                    normals.Add(normals[1]);

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

                        new_uv.x = 1.0f - new_uv.x;
                        // new_uv.y = 1.0f - new_uv.y;

                        new_vert += new_normal * height;

                        verts.Add(new_vert);
                        normals.Add(new_normal);
                        uvs.Add(new_uv);
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
                    if(cover_ends_with_quads)
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
            }
        }
    }
}

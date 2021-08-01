using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace CorgiSpline
{
    public class SplineMeshBuilder_RepeatingMesh : SplineMeshBuilder
    {
        public Mesh RepeatableMesh;

        private List<int> cache_tris = new List<int>();
        private List<Vector3> cache_verts = new List<Vector3>();
        private List<Vector3> cache_normals = new List<Vector3>();

        private NativeList<int> native_tris;
        private NativeList<Vector3> native_verts;
        private NativeList<Vector3> native_normals;

        private NativeList<int> native_stitch_start;
        private NativeList<int> native_stitch_end;

        protected override void OnEnable()
        {
            base.OnEnable();

            native_tris = new NativeList<int>(Allocator.Persistent);
            native_verts = new NativeList<Vector3>(Allocator.Persistent);

            native_stitch_start = new NativeList<int>(Allocator.Persistent);
            native_stitch_end = new NativeList<int>(Allocator.Persistent);
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            native_tris.Dispose();
            native_verts.Dispose();

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

            native_tris.Clear();
            native_verts.Clear();
            native_stitch_start.Clear();
            native_stitch_end.Clear();

            RepeatableMesh.GetTriangles(cache_tris, 0);
            RepeatableMesh.GetVertices(cache_verts);
            RepeatableMesh.GetNormals(cache_normals);

            for (var t = 0; t < cache_tris.Count; ++t)
                native_tris.Add(cache_tris[t]);

            for (var v = 0; v < cache_verts.Count; ++v)
                native_verts.Add(cache_verts[v]);

            for (var v = 0; v < cache_normals.Count; ++v)
                native_normals.Add(cache_normals[v]);

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

            var job = new BuildMeshFromSpline_RepeatingMesh()
            {
                repeatingMesh_tris = native_tris,
                repeatingMesh_verts = native_verts,

                repeatingMesh_stitchVertsStart = native_stitch_start,
                repeatingMesh_stitchVertsEnd = native_stitch_end,

                built_to_t = built_to_t,
                quality = quality,
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

            public NativeArray<int> repeatingMesh_stitchVertsStart;
            public NativeArray<int> repeatingMesh_stitchVertsEnd;

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

                // step through 
                for (var step = 0; step < quality; ++step)
                {
                    var t = (float) step / (quality - 1);

                    var final_point_from_t = false;
                    if (t > built_to_t)
                    {
                        t = built_to_t;
                        final_point_from_t = true;
                    }

                    var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var position = splinePoint.position;

                    // don't allow repeating to intersect, if possible..
                    if(first_set && Vector3.Distance(position, previousPosition) <= boundsDistance)
                    {
                        continue; 
                    }


                    var point_trs = Spline.GetLocalToWorldAtT(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

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


                    // todo:
                    
                    // copy/paste verts from repeatable mesh
                    for(var ri = 0; ri < repeatingMesh_verts.Length; ++ri)
                    {
                        var original = repeatingMesh_verts[ri];

                        var transformed = point_trs.MultiplyPoint(new Vector4(original.x, original.y, original.z, 1.0f));
                        verts.Add(new Vector3(transformed.x, transformed.y, transformed.z));
                        
                        // verts.Add(point_trs.MultiplyPoint(original));
                        uvs.Add(new Vector4(current_uv_step, 0f));
                        normals.Add(new Vector3(0, 1, 0)); 
                    }

                    // copy/paste tris from repeatable mesh 
                    var tri_offset = repeatingMesh_verts.Length * repeatCount;
                    for (var ri = 0; ri < repeatingMesh_tris.Length; ++ri)
                    {
                        tris.Add(repeatingMesh_tris[ri] + tri_offset);
                    }

                    // stitch 
                    if(first_set)
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
            }
        }
    }
}

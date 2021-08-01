using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace CorgiSpline
{
    public class SplineMeshBuilder_Tube : SplineMeshBuilder
    {
        [Header("Tube Settings")]
        [Tooltip("The quality of the loop of the tube generated.")]
        [Range(4, 64)] public int tube_quality = 8;

        [Tooltip("The generated mesh will have less vertices generated over shorter segments of the spline, the higher this value is.")]
        public float minimum_distance_between_points = 0.25f;

        [Tooltip("The generated mesh will have less vertices generated over long continous stretches of the spline, the higher this value is.")]
        public float max_distance_between_points = 2f;

        [Range(0f, 1f)] 
        [Tooltip("The generated mesh will have less vertices generated over long continous stretches of the spline, the lower this value is.")] 
        public float minimum_dot_between_forwards = 0.99f;

        protected override JobHandle ScheduleMeshingJob(JobHandle dependency = default)
        {
            var job = new BuildMeshFromSpline_Tube()
            {
                quality = quality,
                tube_quality = tube_quality,
                width = width,
                height = height,
                uv_tile_scale = uv_tile_scale,
                uv_stretch_instead_of_tile = uv_stretch_instead_of_tile,
                minimum_distance_between_points = minimum_distance_between_points,
                minimum_dot_between_forwards = minimum_dot_between_forwards,
                max_distance_between_points = max_distance_between_points,

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
        private struct BuildMeshFromSpline_Tube : IJob
        {
            // settings
            public int quality;
            public int tube_quality;
            public float width;
            public float height;
            public float uv_tile_scale;
            public float built_to_t;
            public bool cover_ends_with_quads;
            public bool uv_stretch_instead_of_tile;
            public float minimum_distance_between_points;
            public float max_distance_between_points;
            public float minimum_dot_between_forwards;

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

            private Vector3 UnitCirclePlane(Vector3 right, Vector3 up, float radians)
            {
                var position = right * Mathf.Sin(radians) + up * Mathf.Cos(radians);
                var positionOnCircle = position.normalized;
                return positionOnCircle;
            }

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
                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);

                var end_forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, built_to_t);
                var lastPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, built_to_t);

                var previousPosition = firstPoint.position;
                var previousForward = start_forward;

                // closed splines overlap a bit so we dont have to stitch 
                var full_loop = ClosedSpline && built_to_t >= 1f;
                var first_set = false;

                // hack for overlapping bezier when using a closed spline.. 
                // if (ClosedSpline && Mode == SplineMode.BSpline)
                // {
                //     built_to_t = Mathf.Clamp(built_to_t, 0, 0.95f);
                // }


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

                    var up = Vector3.up;
                    var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var position = splinePoint.position;
                    var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var right = Vector3.Cross(forward, up);

                    // skip if too close.. 
                    var delta_dist = Vector3.Distance(previousPosition, position);
                    var deltaDot = Vector3.Dot(forward, previousForward);
                    var tooClose = delta_dist < minimum_distance_between_points;
                    var angleTooSmall = delta_dist < max_distance_between_points && deltaDot > minimum_dot_between_forwards;

                    if (first_set && step > 2 && step < quality - 2 && (tooClose || angleTooSmall))
                    {
                        continue;
                    }

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

                    // go around the circle.. 
                    var pi2 = Mathf.PI * 2f;
                    var tube_delta = 1f / (tube_quality - 1);

                    for (var tube_step = 0; tube_step < tube_quality; ++tube_step)
                    {
                        var radians = tube_step * tube_delta * pi2; 
                        var circleOffset = UnitCirclePlane(right, up, radians);

                        var vert = position + circleOffset * width;
                        verts.Add(vert);

                        var normal = circleOffset;
                        normals.Add(normal);

                        var uv = new Vector2((float) tube_step * tube_delta * uv_tile_scale, current_uv_step);
                        uvs.Add(uv);
                    }
                    
                    previousPosition = position;
                    previousForward = forward;

                    first_set = true;

                    if (final_point_from_t || step == quality - 1)
                    {
                        end_forward = forward;
                        lastPoint = splinePoint;
                        break;
                    }
                }


                var offset_end = verts.Length - tube_quality;

                if (cover_ends_with_quads && full_loop)
                {
                    // copies the start verts, so they get stitched to the end of the end verts
                    for (var v = 0; v < tube_quality; ++v)
                    {
                        var vert = verts[v];
                        var normal = normals[v];
                        var uv = uvs[v + offset_end];

                        verts.Add(vert);
                        normals.Add(normal);
                        uvs.Add(uv);
                    }
                }

                // generate triangles 
                for (var v = 0; v < verts.Length - tube_quality - 1; v += 1)
                {
                    var offset_bot = v;
                    var offset_top = v + tube_quality;

                    var vert_bot0 = offset_bot + 0;
                    var vert_bot1 = offset_bot + 1;
                    var vert_top0 = offset_top + 0;
                    var vert_top1 = offset_top + 1;

                    tris.Add(vert_bot0);
                    tris.Add(vert_bot1);
                    tris.Add(vert_top0);

                    tris.Add(vert_top0);
                    tris.Add(vert_bot1);
                    tris.Add(vert_top1);
                }

                if(cover_ends_with_quads && !full_loop)
                {
                    var v_start_center = verts.Length;

                    verts.Add(firstPoint.position);
                    normals.Add(start_forward);
                    uvs.Add(new Vector4(0f, 0f, 0f, 0f));

                    for(var v = 0; v < tube_quality - 1; ++v)
                    {
                        var vert_bot = v_start_center;
                        var vert_top0 = v;
                        var vert_top1 = v + 1;

                        tris.Add(vert_top1);
                        tris.Add(vert_top0);
                        tris.Add(vert_bot);
                    }

                    var v_end_center = verts.Length;

                    verts.Add(lastPoint.position);
                    normals.Add(end_forward);
                    uvs.Add(new Vector4(0f, 0f, 0f, 0f));

                    for (var v = 0; v < tube_quality - 1; ++v)
                    {
                        var vert_bot = v_end_center;
                        var vert_top0 = v + offset_end;
                        var vert_top1 = v + 1 + offset_end;

                        tris.Add(vert_bot);
                        tris.Add(vert_top0);
                        tris.Add(vert_top1);
                    }
                }
            }
        }
    }
}

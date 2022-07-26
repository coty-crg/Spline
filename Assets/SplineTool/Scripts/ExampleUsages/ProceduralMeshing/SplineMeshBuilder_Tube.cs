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
        // [Header("Tube Settings")]
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
            DetermineSplineSettings(out Space splineSpace, out Matrix4x4 localToWorldMatrix, out Matrix4x4 worldToLocalMatrix);

            var job = new BuildMeshFromSpline_Tube()
            {
                quality = quality,
                tube_quality = tube_quality,
                width = scaleMult.x,
                height = scaleMult.y,
                uv_tile_scale = uv_tile_scale,
                minimum_distance_between_points = minimum_distance_between_points,
                minimum_dot_between_forwards = minimum_dot_between_forwards,
                max_distance_between_points = max_distance_between_points,
                use_splinepoint_rotations = use_splinepoint_rotations,
                use_splinepoint_scale = use_splinepoint_scale,
                vertexOffset = vertexOffset,
                rotationEulorOffset = rotationEulorOffset,
                normalsMode = MeshNormalsMode,
                uvsMode = UVsMode,

                verts = _nativeVertices,
                normals = _nativeNormals,
                tangents = _nativeTangents,
                uvs0 = _nativeUV0,
                uvs1 = _nativeUV1,
                tris = _nativeTris,
                bounds = _nativeBounds,

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

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
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
            public float minimum_distance_between_points;
            public float max_distance_between_points;
            public float minimum_dot_between_forwards;
            public bool use_splinepoint_rotations;
            public bool use_splinepoint_scale;
            public Vector3 vertexOffset;
            public Vector3 rotationEulorOffset;

            public MeshBuilderNormals normalsMode;
            public MeshBuilderUVs uvsMode;

            // mesh data 
            public NativeList<Vector3> verts;
            public NativeList<Vector3> normals;
            public NativeList<Vector4> tangents;
            public NativeList<Vector4> uvs0;
            public NativeList<Vector4> uvs1;
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

            private Vector3 UnitCirclePlane(Vector3 right, Vector3 up, float radians)
            {
                var position = right * Mathf.Sin(radians) + up * Mathf.Cos(radians);
                var positionOnCircle = position.normalized;
                return positionOnCircle;
            }

            public void Execute()
            {
                var trackedBounds = new Bounds();

                // reset data 
                verts.Clear();
                normals.Clear();
                tangents.Clear(); 
                uvs0.Clear();
                uvs1.Clear();
                tris.Clear();

                // setup 
                var start_forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var start_up = Vector3.up; 
                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);

                var end_forward = Vector3.up; //  Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, built_to_t);
                var end_up = Vector3.right; 
                var lastPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, built_to_t);

                var rotation = Quaternion.Euler(rotationEulorOffset);

                start_forward = rotation * start_forward;
                start_up = rotation * start_up;

                end_forward = rotation * end_forward;
                end_up = rotation * end_up;

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
                    var t = (float) step / (quality);

                    var final_point_from_t = false;
                    if (t > built_to_t)
                    {
                        t = built_to_t;
                        final_point_from_t = true;
                    }

                    var up = Vector3.up;
                    var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                    var position = splinePoint.position + vertexOffset;
                    var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

                    if(use_splinepoint_rotations)
                    {
                        up = splinePoint.rotation * Vector3.up;
                        forward = splinePoint.rotation * Vector3.forward;
                    }

                    up = rotation * up;
                    forward = rotation * forward;

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


                    var uv_x = t;

                    if (uvsMode == MeshBuilderUVs.Tile)
                    {
                        uv_x = (t * uv_tile_scale) % 1.0f;
                    }

                    // go around the circle.. 
                    var pi2 = Mathf.PI * 2f;
                    var tube_delta = 1f / (tube_quality - 1);


                    var localWidth = width;

                    if(use_splinepoint_scale)
                    {
                        localWidth *= splinePoint.scale.magnitude;
                    }
                    
                    for (var tube_step = 0; tube_step < tube_quality; ++tube_step)
                    {
                        var radians = tube_step * tube_delta * pi2; 
                        var circleOffset = UnitCirclePlane(right, up, radians);

                        var vert = position + circleOffset * localWidth;
                        verts.Add(vert);

                        var normal = circleOffset;
                        normals.Add(normal);

                        var tangent3 = Vector3.Cross(forward, normal).normalized;
                        var tangent = new Vector4(tangent3.x, tangent3.y, tangent3.z, 1.0f);
                        tangents.Add(tangent);

                        var uv0 = new Vector2(uv_x, (float) tube_step * tube_delta * uv_tile_scale);
                        uvs0.Add(uv0);

                        var uv1 = new Vector2(t, (tube_step * tube_delta) / 2f);
                        uvs1.Add(uv1);

                        // track bounds.. 
                        trackedBounds.min = Vector3.Min(trackedBounds.min, vert);
                        trackedBounds.max = Vector3.Max(trackedBounds.max, vert);
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
                        var uv0 = uvs0[v + offset_end];
                        var uv1 = uvs1[v + offset_end];
                        var tangent = tangents[v + offset_end];

                        verts.Add(vert);
                        normals.Add(normal);
                        uvs0.Add(uv0);
                        uvs1.Add(uv1);
                        tangents.Add(tangent); 
                    }
                }

                // generate triangles 
                var vertexLength = verts.Length;

                for (var v = 0; v < vertexLength - tube_quality - 1; v += 1)
                {
                    var offset_bot = v;
                    var offset_top = v + tube_quality;

                    var vert_bot0 = offset_bot + 0;
                    var vert_bot1 = offset_bot + 1;
                    var vert_top0 = offset_top + 0;
                    var vert_top1 = offset_top + 1;

                    // if we're wanting normals to be hard, duplicate some data and use it for triangle generation instead 
                    if (normalsMode == MeshBuilderNormals.Hard)
                    {
                        var averageNormal = ((normals[vert_bot0] + normals[vert_bot1] + normals[vert_top0] + normals[vert_top1]) / 4).normalized;
                        var averageTangent = ((tangents[vert_bot0] + tangents[vert_bot1] + tangents[vert_top0] + tangents[vert_top1]) / 4).normalized;

                        verts.Add(verts[vert_bot0]);
                        verts.Add(verts[vert_bot1]);
                        verts.Add(verts[vert_top0]);
                        verts.Add(verts[vert_top1]);

                        normals.Add(averageNormal);
                        normals.Add(averageNormal);
                        normals.Add(averageNormal);
                        normals.Add(averageNormal);

                        tangents.Add(averageTangent);
                        tangents.Add(averageTangent);
                        tangents.Add(averageTangent);
                        tangents.Add(averageTangent);

                        uvs0.Add(uvs0[vert_bot0]);
                        uvs0.Add(uvs0[vert_bot1]);
                        uvs0.Add(uvs0[vert_top0]);
                        uvs0.Add(uvs0[vert_top1]);

                        uvs1.Add(uvs1[vert_bot0]);
                        uvs1.Add(uvs1[vert_bot1]);
                        uvs1.Add(uvs1[vert_top0]);
                        uvs1.Add(uvs1[vert_top1]);

                        // replace these for triangles 
                        vert_bot0 = verts.Length - 4 + 0;
                        vert_bot1 = verts.Length - 4 + 1;
                        vert_top0 = verts.Length - 4 + 2;
                        vert_top1 = verts.Length - 4 + 3;
                    }

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
                    var startTangent = new Vector4(start_up.x, start_up.y, start_up.z, 1.0f);

                    verts.Add(firstPoint.position + vertexOffset);
                    normals.Add(start_forward);
                    tangents.Add(startTangent);
                    uvs0.Add(new Vector4(0f, 0f, 0f, 0f));
                    uvs1.Add(new Vector4(0f, 0.75f, 0f, 0f));

                    for(var v = 0; v < tube_quality - 1; ++v)
                    {
                        var vert_bot = v_start_center;
                        var vert_top0 = v;
                        var vert_top1 = v + 1;

                        // always have caps as hard normals 
                        verts.Add(verts[vert_bot]);
                        verts.Add(verts[vert_top0]);
                        verts.Add(verts[vert_top1]);

                        normals.Add(start_forward);
                        normals.Add(start_forward);
                        normals.Add(start_forward);

                        tangents.Add(startTangent);
                        tangents.Add(startTangent);
                        tangents.Add(startTangent);

                        uvs0.Add(uvs0[vert_bot]);
                        uvs0.Add(uvs0[vert_top0]);
                        uvs0.Add(uvs0[vert_top1]);

                        uvs1.Add(uvs1[vert_bot]);
                        uvs1.Add(Vector4.Scale(uvs1[vert_top0], new Vector4(0f, 0.5f, 0f, 0f)) + new Vector4(1f, 0.5f, 0f, 0f));
                        uvs1.Add(Vector4.Scale(uvs1[vert_top1], new Vector4(0f, 0.5f, 0f, 0f)) + new Vector4(1f, 0.5f, 0f, 0f));

                        // replace these for triangles 
                        vert_bot = verts.Length  - 3 + 0;
                        vert_top0 = verts.Length - 3 + 1;
                        vert_top1 = verts.Length - 3 + 2;

                        tris.Add(vert_top1);
                        tris.Add(vert_top0);
                        tris.Add(vert_bot);
                    }

                    var v_end_center = verts.Length;

                    var endTangent = new Vector4(end_up.x, end_up.y, end_up.z, 1.0f);

                    verts.Add(lastPoint.position + vertexOffset);
                    normals.Add(end_forward);
                    tangents.Add(endTangent);
                    uvs0.Add(new Vector4(0f, 0f, 0f, 0f));
                    uvs1.Add(new Vector4(0f, 1.0f, 0f, 0f));

                    for (var v = 0; v < tube_quality - 1; ++v)
                    {
                        var vert_bot = v_end_center;
                        var vert_top0 = v + offset_end;
                        var vert_top1 = v + 1 + offset_end;

                        // always have caps as hard normals 
                        verts.Add(verts[vert_bot]);
                        verts.Add(verts[vert_top0]);
                        verts.Add(verts[vert_top1]);

                        normals.Add(end_forward);
                        normals.Add(end_forward);
                        normals.Add(end_forward);

                        tangents.Add(endTangent);
                        tangents.Add(endTangent);
                        tangents.Add(endTangent);

                        uvs0.Add(uvs0[vert_bot]);
                        uvs0.Add(uvs0[vert_top0]);
                        uvs0.Add(uvs0[vert_top1]);

                        uvs1.Add(uvs1[vert_bot]);
                        uvs1.Add(Vector4.Scale(uvs1[vert_top0], new Vector4(0f, 0.5f, 0f, 0f)) + new Vector4(1f, 0.75f, 0f, 0f));
                        uvs1.Add(Vector4.Scale(uvs1[vert_top1], new Vector4(0f, 0.5f, 0f, 0f)) + new Vector4(1f, 0.75f, 0f, 0f));

                        // replace these for triangles 
                        vert_bot  = verts.Length - 3 + 0;
                        vert_top0 = verts.Length - 3 + 1;
                        vert_top1 = verts.Length - 3 + 2;

                        tris.Add(vert_bot);
                        tris.Add(vert_top0);
                        tris.Add(vert_top1);
                    }
                }

                bounds[0] = trackedBounds;
            }
        }
    }
}

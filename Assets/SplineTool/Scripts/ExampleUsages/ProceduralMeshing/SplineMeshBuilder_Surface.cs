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
    [DefaultExecutionOrder(1)] // this is so the mesh builders' OnEnable run after the spline's OnEnable
    public class SplineMeshBuilder_Surface: SplineMeshBuilder
    {
        protected override JobHandle ScheduleMeshingJob(JobHandle dependency = default)
        {
            DetermineSplineSettings(out Space splineSpace, out Matrix4x4 localToWorldMatrix, out Matrix4x4 worldToLocalMatrix);

            var job = new BuildMeshFromSpline_Surface()
            {
                quality = quality,
                // width = scaleMult.x,
                height = scaleMult.y,
                uv_tile_scale = uv_tile_scale,
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
                colors = _nativeColors,
                tris = _nativeTris,
                bounds = _nativeBounds,
                
                Points = SplineReference.NativePoints,
                Mode = SplineReference.GetSplineMode(),
                ClosedSpline = SplineReference.GetSplineClosed(),

                SplineSpace = splineSpace,
                worldToLocalMatrix = worldToLocalMatrix,
                localToWorldMatrix = localToWorldMatrix,

                built_to_t = built_to_t,
            };

            return job.Schedule(dependency);
        }

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
        private struct BuildMeshFromSpline_Surface : IJob
        {
            // settings
            public int quality;
            public float height;
            public float uv_tile_scale;
            public float built_to_t;
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
            public NativeList<Vector4> colors;
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
                var trackedBounds = new Bounds();

                // reset data 
                verts.Clear();
                normals.Clear();
                tangents.Clear(); 
                uvs0.Clear();
                uvs1.Clear();
                colors.Clear();
                tris.Clear();

                var heights = new NativeList<float>(quality * 2, Allocator.Temp);

                // setup 
                var rotation = Quaternion.Euler(rotationEulorOffset);

                // step through 
                var tile_uv_x = 0f;
                var vertex_count = 0;

                //      closed spline example: 
                //          vertices             triangles 
                //          |1    /0             0, 1, 2
                //          |3    |2             0, 3, 2,
                //          |5    \4             2, 3, 5
                //           \7    \6            2, 5, 4
                //            \9    |8           etc 
                //             |11  |10      
                //             |13  |12      

                // iterate over the spline, from the beginning and the end
                // for each two of those points, generate a vertex 
                for (var step = 0; step <= (quality / 2); ++step)
                {
                    var t_0 = 0.0d + ((double) step / quality);
                    var t_1 = 1.0d - ((double) step / quality);

                    if(t_0 > built_to_t)
                    {
                        break; 
                    }

                    var up_0 = Vector3.up;
                    var up_1 = Vector3.up;

                    var splinePoint_0 = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t_0);
                    var splinePoint_1 = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t_1);

                    var forward_0 = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, (float) t_0);
                    var forward_1 = -Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, (float) t_1);

                    if(use_splinepoint_rotations)
                    {
                        up_0 = splinePoint_0.rotation * Vector3.up;
                        up_1 = splinePoint_1.rotation * Vector3.up;

                        forward_0 = splinePoint_0.rotation * Vector3.forward;
                        forward_1 = splinePoint_1.rotation * Vector3.forward;
                    }

                    up_0 = rotation * up_0;
                    up_1 = rotation * up_1;

                    forward_0 = rotation * forward_0;
                    forward_1 = rotation * forward_1;

                    var right_0 = Vector3.Cross(forward_0, up_0);
                    var right_1 = Vector3.Cross(forward_1, up_1);

                    var uv_x = t_0;

                    if (uvsMode == MeshBuilderUVs.Tile || uvsMode == MeshBuilderUVs.TileSwapXY)
                    {
                        tile_uv_x += uv_tile_scale;
                        uv_x = tile_uv_x;
                    }

                    var position_0 = splinePoint_0.position + vertexOffset;
                    var position_1 = splinePoint_1.position + vertexOffset;

                    var vertex0 = position_0;
                    var vertex1 = position_1;

                    var normal0 = up_0;
                    var normal1 = up_1;

                    var tangent0 = right_0;
                    var tangent1 = right_1;

                    var uv0 = new Vector3((float) uv_x, 0f);
                    var uv1 = new Vector3((float) uv_x, 1f);

                    verts.Add(vertex0);
                    verts.Add(vertex1);

                    normals.Add(normal0);
                    normals.Add(normal1);

                    tangents.Add(tangent0);
                    tangents.Add(tangent1);

                    uvs0.Add(uv0);
                    uvs0.Add(uv1);

                    uvs1.Add(uv0);
                    uvs1.Add(uv1);

                    colors.Add(splinePoint_0.color);
                    colors.Add(splinePoint_1.color);

                    heights.Add(splinePoint_0.scale.y);
                    heights.Add(splinePoint_1.scale.y);

                    vertex_count += 2;

                    // track bounds.. 
                    trackedBounds.min = Vector3.Min(trackedBounds.min, vertex0);
                    trackedBounds.min = Vector3.Min(trackedBounds.min, vertex1);

                    trackedBounds.max = Vector3.Max(trackedBounds.max, vertex0);
                    trackedBounds.max = Vector3.Max(trackedBounds.max, vertex1);

                    if (step == quality - 1)
                    {
                        break;
                    }
                }

                // after generating vertices, go over each pair and connect them to create a quad 
                // from each two triangles 
                for(var vi = 0; vi < vertex_count - 3; vi += 2)
                {
                    tris.Add(vi + 0);
                    tris.Add(vi + 1);
                    tris.Add(vi + 3);

                    tris.Add(vi + 0);
                    tris.Add(vi + 3);
                    tris.Add(vi + 2);
                }

                // if height is more than zero, we need to generate a "floor" and then connect the floor and ceiling together 
                if(height > 0)
                {
                    // copy the original vertices and flip the normals, offset by the normal to create a floor 
                    var originalVertexCount = vertex_count;
                    for(var vi = 0; vi < originalVertexCount; ++vi)
                    {
                        var vertex = verts[vi];
                        var normal = normals[vi];
                        var tangent = tangents[vi];
                        var color = colors[vi];
                        var uv0 = uvs0[vi];
                        var uv1 = uvs1[vi];

                        // flip the normal 
                        normal = -normal;
                        tangent = -tangent;

                        var localHeight = height;
                        if(use_splinepoint_scale)
                        {
                            localHeight *= heights[vi];
                        }

                        vertex += normal * localHeight;

                        // append 
                        verts.Add(vertex);
                        normals.Add(normal);
                        tangents.Add(tangent);
                        colors.Add(color);
                        uvs0.Add(uv0);
                        uvs1.Add(uv1);

                        // keep counting
                        vertex_count++;

                        // track bounds.. 
                        trackedBounds.min = Vector3.Min(trackedBounds.min, vertex);
                        trackedBounds.max = Vector3.Max(trackedBounds.max, vertex);
                    }

                    // create triangles from the copied vertices, winding in the inverse direction to create a floor 
                    for (var vi = originalVertexCount; vi < vertex_count - 3; vi += 2)
                    {
                        tris.Add(vi + 3);
                        tris.Add(vi + 1);
                        tris.Add(vi + 0);

                        tris.Add(vi + 2);
                        tris.Add(vi + 3);
                        tris.Add(vi + 0);
                    }


                    // connect the floor and ceiling to create a wall around the mesh 
                    if (normalsMode == MeshBuilderNormals.Smooth)
                    {
                        //   left: (0,2,2') + (2,2',0)
                        //  right: (3,1',3') + (1,1',3)
                        //                 v1          v3
                        //                  ____________
                        //                 /           /|
                        //                /           / |
                        //            v0 /________v2_/  |
                        //              |           |   |
                        //              |  (v1')    |   | v3'
                        //              |           |  / 
                        //              |           | /
                        //           v0'|________v2'|/
                        // 
                        for (var vi = 0; vi < originalVertexCount - 2; vi += 2)
                        {
                            var vi_t = vi;                          // top
                            var vi_b = vi + originalVertexCount;    // bottom 

                            // left
                            tris.Add(vi_t + 0);
                            tris.Add(vi_b + 2);
                            tris.Add(vi_b + 0);

                            tris.Add(vi_t + 2);
                            tris.Add(vi_b + 2);
                            tris.Add(vi_t + 0);

                            // right 
                            tris.Add(vi_t + 3);
                            tris.Add(vi_b + 1);
                            tris.Add(vi_b + 3);

                            tris.Add(vi_t + 1);
                            tris.Add(vi_b + 1);
                            tris.Add(vi_t + 3);
                        }
                    }

                    // for hard normals, duplicate the verts and connect those instead 
                    else
                    {

                        // dupe pass
                        var dupeVertexStart = vertex_count;

                        for (var vi = 0; vi < vertex_count; vi++)
                        {
                            var vert = verts[vi];
                            var normal = normals[vi];
                            var tangent = tangents[vi];
                            var color = colors[vi];
                            var uv0 = uvs0[vi];
                            var uv1 = uvs1[vi];

                            normal = tangent;
                            tangent = Vector3.up; 

                            verts.Add(vert);
                            normals.Add(normal);
                            tangents.Add(tangent);
                            colors.Add(color);
                            uvs0.Add(uv0);
                            uvs1.Add(uv1);
                        }

                        for (var vi = 0; vi < originalVertexCount - 2; vi += 2)
                        {
                            var vi_t = vi + dupeVertexStart;                          // top
                            var vi_b = vi + originalVertexCount + dupeVertexStart;    // bottom 

                            // left
                            tris.Add(vi_t + 0);
                            tris.Add(vi_b + 2);
                            tris.Add(vi_b + 0);

                            tris.Add(vi_t + 2);
                            tris.Add(vi_b + 2);
                            tris.Add(vi_t + 0);

                            // right 
                            tris.Add(vi_t + 3);
                            tris.Add(vi_b + 1);
                            tris.Add(vi_b + 3);

                            tris.Add(vi_t + 1);
                            tris.Add(vi_b + 1);
                            tris.Add(vi_t + 3);
                        }
                    }


                }

                bounds[0] = trackedBounds;
                heights.Dispose(); 
            }
        }
    }
}

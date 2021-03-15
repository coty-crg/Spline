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

        [Range(32, 1024)] public int quality = 256;
        [Range(0.001f, 10f)] public float width = 1f;
        [Range(0f, 10f)] public float height = 1f;
        public float uv_tile_scale = 1f;

        // internal 
        private Mesh mesh;
        private NativeList<Vector3> verts;
        private NativeList<Vector3> normals;
        private NativeList<Vector4> uvs;
        private NativeList<int> tris;

        private void OnEnable()
        {
            mesh = new Mesh();

            verts = new NativeList<Vector3>(Allocator.Persistent);
            normals = new NativeList<Vector3>(Allocator.Persistent);
            uvs = new NativeList<Vector4>(Allocator.Persistent);
            tris = new NativeList<int>(Allocator.Persistent);

            Rebuild_Jobified();
        }

        private void OnDisable()
        {
            CompleteJob();

            verts.Dispose();
            normals.Dispose();
            uvs.Dispose();
            tris.Dispose();

            if (mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh);
                }
            }
        }

        private void Update()
        {
            if (RebuildEveryFrame)
            {
                Rebuild_Jobified();
            }
        }

        private void LateUpdate()
        {
            if (RebuildEveryFrame && AllowAsyncRebuild)
            {
                CompleteJob();
            }
        }

        private JobHandle previousHandle;

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

            var job = new BuildMeshFromSpline()
            {
                quality = quality,
                width = width,
                height = height,
                uv_tile_scale = uv_tile_scale,

                verts = verts,
                normals = normals,
                uvs = uvs,
                tris = tris,

                Points = SplineReference.NativePoints,
                Mode = SplineReference.GetSplineMode(),
                SplineSpace = SplineReference.GetSplineSpace(),
                worldToLocalMatrix = SplineReference.transform.worldToLocalMatrix,
                localToWorldMatrix = SplineReference.transform.localToWorldMatrix,
                ClosedSpline = SplineReference.GetSplineClosed(),

            };

            previousHandle = job.Schedule();

            if (!AllowAsyncRebuild)
            {
                CompleteJob();
            }
        }

        private void CompleteJob()
        {
            previousHandle.Complete();

            mesh.Clear();
            mesh.SetVertices(verts.AsArray());
            mesh.SetNormals(normals.AsArray());
            mesh.SetUVs(0, uvs.AsArray());
            mesh.SetIndices(tris.AsArray(), MeshTopology.Triangles, 0);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
        }

        [BurstCompile]
        private struct BuildMeshFromSpline : IJob
        {
            // settings
            public int quality;
            public float width;
            public float height;
            public float uv_tile_scale;

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
                var end_forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0.9f);

                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var lastPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 1f);
                var previousPosition = firstPoint.position;

                // ensure even quality 
                quality = quality - quality % 2;

                // step through 
                for (var step = 0; step < quality; ++step)
                {
                    var t0 = (float)(step - 1) / quality;
                    var t1 = (float)(step - 0) / quality;

                    var splinePoint0 = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t0);
                    var splinePoint1 = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t1);

                    if(SplineSpace == Space.Self)
                    {
                        splinePoint0 = Spline.JobSafe_TransformSplinePoint(splinePoint0, worldToLocalMatrix);
                        splinePoint1 = Spline.JobSafe_TransformSplinePoint(splinePoint1, worldToLocalMatrix);
                    }

                    var position0 = splinePoint0.position;
                    var position1 = splinePoint1.position;

                    var rotation0 = splinePoint0.rotation;
                    var rotation1 = splinePoint1.rotation;

                    var position = Vector3.Lerp(position0, position1, 0.5f);
                    var forward = (position1 - position0).normalized;
                    var rotation = Quaternion.Slerp(rotation0, rotation1, 0.5f);
                    var up = rotation * Vector3.forward;
                    var right = Vector3.Cross(forward, up);

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
                    current_uv_step += Vector3.Distance(previousPosition, position) * uv_tile_scale;
                    current_uv_step = current_uv_step % 1.0f;

                    uvs.Add(new Vector2(0f, current_uv_step));
                    uvs.Add(new Vector2(1f, current_uv_step));

                    previousPosition = position;
                }

                // generate tris 
                for (var v = 0; v < verts.Length; v += 4)
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
                    for (var v = floor_vert_index; v < verts.Length; v += 4)
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
                    for (var v = 0; v < floor_vert_index; v += 4)
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


                        //
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
                }
            }
        }
    }
}

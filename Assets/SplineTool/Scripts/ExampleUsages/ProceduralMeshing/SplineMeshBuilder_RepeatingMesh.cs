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
        [Header("RepeatingMesh")]
        [Tooltip("The mesh to copy/paste when creating this spline mesh.")]
        public Mesh RepeatableMesh;

        [Tooltip("Offsets the local vertices on each paste of the mesh along the spline.")]
        public Vector3 MeshLocalOffsetVertices;

        [Tooltip("Use the real UV data from the mesh we are pasting.")]
        public bool UseRepeatingMeshUVs;

        // internal stuff 
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

        protected override void OnEnable()
        {
            Debug.Assert(RepeatableMesh != null, "RepeatableMesh is null.", gameObject);

            native_tris = new NativeList<int>(Allocator.Persistent);
            native_verts = new NativeList<Vector3>(Allocator.Persistent);
            native_normals = new NativeList<Vector3>(Allocator.Persistent);
            native_tangents = new NativeList<Vector4>(Allocator.Persistent);
            native_uv0 = new NativeList<Vector4>(Allocator.Persistent);
            native_colors = new NativeList<Vector4>(Allocator.Persistent);

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
            native_colors.Clear();

            // fetch the data from the repeatable mesh 
            RepeatableMesh.GetTriangles(cache_tris, 0);
            RepeatableMesh.GetVertices(cache_verts);

            for (var t = 0; t < cache_tris.Count; ++t)
                native_tris.Add(cache_tris[t]);

            for (var v = 0; v < cache_verts.Count; ++v)
                native_verts.Add(cache_verts[v]);

            // check if this repeatable mesh actually has the attributes we want.. 
            var has_normals = RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal);
            var has_tangents = RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
            var has_uv0 = RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
            var has_color = RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);

            if(has_normals)
            {
                RepeatableMesh.GetNormals(cache_normals);

                for (var v = 0; v < cache_normals.Count; ++v)
                {
                    native_normals.Add(cache_normals[v]);
                }
            }
            else
            {
                for (var v = 0; v < cache_verts.Count; ++v)
                {
                    native_normals.Add(Vector3.up);
                }
            }

            if(has_tangents)
            {
                RepeatableMesh.GetTangents(cache_tangents);

                for (var v = 0; v < cache_tangents.Count; ++v)
                {
                    native_tangents.Add(cache_tangents[v]);
                }
            }
            else
            {
                for (var v = 0; v < cache_verts.Count; ++v)
                {
                    native_tangents.Add(Vector3.right);
                }
            }

            if(has_uv0)
            {
                RepeatableMesh.GetUVs(0, cache_uv0);

                for (var v = 0; v < cache_uv0.Count; ++v)
                {
                    native_uv0.Add(cache_uv0[v]);
                }
            }
            else
            {
                for (var v = 0; v < cache_verts.Count; ++v)
                {
                    native_uv0.Add(Vector4.zero);
                }
            }

            if (has_color)
            {
                RepeatableMesh.GetColors(cache_colors);

                for (var v = 0; v < cache_colors.Count; ++v)
                {
                    native_colors.Add(new Vector4(cache_colors[v].r, cache_colors[v].g, cache_colors[v].b, cache_colors[v].a));
                }
            }
            else
            {
                for (var v = 0; v < cache_verts.Count; ++v)
                {
                    native_colors.Add(Color.white);
                }
            }

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

                MeshLocalOffsetVertices = MeshLocalOffsetVertices,
                UseRepeatingMeshUVs = UseRepeatingMeshUVs,

                built_to_t = built_to_t,
                quality = quality,
                uv_tile_scale = uv_tile_scale,
                uv_stretch_instead_of_tile = uv_stretch_instead_of_tile,
                width = width,
                height = height,

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

            public bool UseRepeatingMeshUVs;
            public float width;
            public float height;

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

                    var meshBoundsZ = (repeatingMesh_bounds.max.z - repeatingMesh_bounds.min.z);
                    var totalMeshZ = meshBoundsZ * quality;

                for (var meshIndex = 0; meshIndex < quality; ++meshIndex)
                {
                    var currentMeshZ = meshIndex * meshBoundsZ;

                    var brokenEarly = false;

                    // pasted the mesh over and over, bending the verts to be along the spline 
                    for (var ri = 0; ri < repeatingMesh_verts.Length; ++ri)
                    {
                        var repeating_vertex = repeatingMesh_verts[ri];
                        var normal = repeatingMesh_normals[ri];
                        var tangent = repeatingMesh_tangents[ri];

                        if (repeatingMesh_has_colors)
                        {
                            var color = repeatingMesh_colors[ri];
                            colors.Add(color);
                        }

                        var meshBoundsWithInnerZ = currentMeshZ + (repeating_vertex.z - repeatingMesh_bounds.min.z); 
                        var innerMesh_t = meshBoundsWithInnerZ / totalMeshZ;

                        if(innerMesh_t > built_to_t)
                        {
                            brokenEarly = true; 
                        }

                        var vertex_splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, innerMesh_t);
                        
                        var trs = Matrix4x4.TRS(
                            vertex_splinePoint.position + MeshLocalOffsetVertices, 
                            vertex_splinePoint.rotation, 
                            Vector3.Scale(vertex_splinePoint.scale, new Vector3(width, height, 1f)));

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

                    if (brokenEarly)
                    {
                        break;
                    }
                }
                
                bounds[0] = trackedBounds;
            }
        }
    }
}

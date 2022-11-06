using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CorgiSpline
{

    public class SplineMeshBuilder_RepeatingRandomMeshes : SplineMeshBuilder
    {
        // [Header("RepeatingMesh")]
        [Tooltip("The meshes to copy/paste when creating this spline mesh.")]
        public List<Mesh> RepeatableMeshes = new List<Mesh>();

        // [Tooltip("Offsets the local vertices on each paste of the mesh along the spline.")]
        // public Vector3 MeshLocalOffsetVertices;

        [Tooltip("Use the real UV data from the mesh we are pasting.")]
        public bool UseRepeatingMeshUVs;

        // internal stuff 
        private List<MeshData> _meshDatas = new List<MeshData>();

        private class MeshData
        {
            public Mesh mesh; 
            public List<int> cache_tris = new List<int>();
            public List<Vector3> cache_verts = new List<Vector3>();
            public List<Vector3> cache_normals = new List<Vector3>();
            public List<Vector4> cache_tangents = new List<Vector4>();
            public List<Vector4> cache_uv0 = new List<Vector4>();
            public List<Color> cache_colors = new List<Color>();

            public NativeList<int> native_tris;
            public NativeList<Vector3> native_verts;
            public NativeList<Vector3> native_normals;
            public NativeList<Vector4> native_tangents;
            public NativeList<Vector4> native_uv0;
            public NativeList<Vector4> native_colors;

            public float z_min;
            public float z_max;
        }

        // flattened data 
        private NativeList<int> _repeatingMeshes_tris;
        private NativeList<Vector3> _repeatingMeshes_verts;
        private NativeList<Vector3> _repeatingMeshes_normals;
        private NativeList<Vector4> _repeatingMeshes_tangents;
        private NativeList<Vector4> _repeatingMeshes_uv0;
        private NativeList<Vector4> _repeatingMeshes_colors;
        private NativeList<Bounds> _repeatingMeshes_bounds;

        private NativeList<int2> _repeatingMeshes_tris_indices;
        private NativeList<int2> _repeatingMeshes_verts_indices;
        private NativeList<int2> _repeatingMeshes_normals_indices;
        private NativeList<int2> _repeatingMeshes_tangents_indices;
        private NativeList<int2> _repeatingMeshes_uv0_indices;
        private NativeList<int2> _repeatingMeshes_colors_indices;

        private bool _hasNativeData;

        protected override void OnEnable()
        {
            Debug.Assert(!Application.isPlaying || RepeatableMeshes != null, "RepeatableMeshes is null.", gameObject);
            RefreshMeshDatas(true); 
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            DisposeMeshDatas(); 
        }

        private void RefreshMeshDatas(bool forceRefresh = false)
        {
            if(_meshDatas.Count != RepeatableMeshes.Count || forceRefresh)
            {
                DisposeMeshDatas();
                
                _hasNativeData = true; 

                _meshDatas = new List<MeshData>(RepeatableMeshes.Count);

                for(var i = 0; i < RepeatableMeshes.Count; ++i)
                {
                    var mesh = RepeatableMeshes[i];
                    var meshData = new MeshData();

                    meshData.mesh = mesh;
                    meshData.native_tris = new NativeList<int>(Allocator.Persistent);
                    meshData.native_verts = new NativeList<Vector3>(Allocator.Persistent);
                    meshData.native_normals = new NativeList<Vector3>(Allocator.Persistent);
                    meshData.native_tangents = new NativeList<Vector4>(Allocator.Persistent);
                    meshData.native_uv0 = new NativeList<Vector4>(Allocator.Persistent);
                    meshData.native_colors = new NativeList<Vector4>(Allocator.Persistent);

                    meshData.cache_tris.Clear();
                    meshData.cache_verts.Clear();
                    meshData.cache_normals.Clear();
                    meshData.cache_tangents.Clear();
                    meshData.cache_uv0.Clear();
                    meshData.cache_colors.Clear();

                    // fetch the data from the repeatable mesh 
                    mesh.GetTriangles(meshData.cache_tris, 0);
                    mesh.GetVertices(meshData.cache_verts);

                    for (var t = 0; t < meshData.cache_tris.Count; ++t)
                        meshData.native_tris.Add(meshData.cache_tris[t]);

                    for (var v = 0; v < meshData.cache_verts.Count; ++v)
                        meshData.native_verts.Add(meshData.cache_verts[v]);

                    // check if this repeatable mesh actually has the attributes we want.. 
                    var has_normals = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal);
                    var has_tangents = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
                    var has_uv0 = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                    var has_color = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);

                    if (has_normals)
                    {
                        mesh.GetNormals(meshData.cache_normals);

                        for (var v = 0; v < meshData.cache_normals.Count; ++v)
                        {
                            meshData.native_normals.Add(meshData.cache_normals[v]);
                        }
                    }
                    else
                    {
                        for (var v = 0; v < meshData.cache_verts.Count; ++v)
                        {
                            meshData.native_normals.Add(Vector3.up);
                        }
                    }

                    if (has_tangents)
                    {
                        mesh.GetTangents(meshData.cache_tangents);

                        for (var v = 0; v < meshData.cache_tangents.Count; ++v)
                        {
                            meshData.native_tangents.Add(meshData.cache_tangents[v]);
                        }
                    }
                    else
                    {
                        for (var v = 0; v < meshData.cache_verts.Count; ++v)
                        {
                            meshData.native_tangents.Add(Vector3.right);
                        }
                    }

                    if (has_uv0)
                    {
                        mesh.GetUVs(0, meshData.cache_uv0);

                        for (var v = 0; v < meshData.cache_uv0.Count; ++v)
                        {
                            meshData.native_uv0.Add(meshData.cache_uv0[v]);
                        }
                    }
                    else
                    {
                        for (var v = 0; v < meshData.cache_verts.Count; ++v)
                        {
                            meshData.native_uv0.Add(Vector4.zero);
                        }
                    }

                    if (has_color)
                    {
                        mesh.GetColors(meshData.cache_colors);

                        for (var v = 0; v < meshData.cache_colors.Count; ++v)
                        {
                            meshData.native_colors.Add(
                                new Vector4(
                                    meshData.cache_colors[v].r,
                                    meshData.cache_colors[v].g,
                                    meshData.cache_colors[v].b,
                                    meshData.cache_colors[v].a
                                    )
                                );
                        }
                    }
                    else
                    {
                        for (var v = 0; v < meshData.cache_verts.Count; ++v)
                        {
                            meshData.native_colors.Add(Color.white);
                        }
                    }

                    // try and find start and end z values 
                    meshData.z_min = float.MaxValue;
                    meshData.z_max = float.MinValue;
                    for (var v = 0; v < meshData.native_verts.Length; ++v)
                    {
                        var vertex = meshData.native_verts[v];
                        if (vertex.z < meshData.z_min)
                        {
                            meshData.z_min = vertex.z;
                        }

                        if (vertex.z > meshData.z_max)
                        {
                            meshData.z_max = vertex.z;
                        }
                    }

                    _meshDatas.Add(meshData); 
                }

                // after collecting the repeatable mesh data, flatten them into a single array for each type 
                _repeatingMeshes_tris = new NativeList<int>(Allocator.Persistent);
                _repeatingMeshes_verts = new NativeList<Vector3>(Allocator.Persistent);
                _repeatingMeshes_normals = new NativeList<Vector3>(Allocator.Persistent);
                _repeatingMeshes_tangents = new NativeList<Vector4>(Allocator.Persistent);
                _repeatingMeshes_uv0 = new NativeList<Vector4>(Allocator.Persistent);
                _repeatingMeshes_colors = new NativeList<Vector4>(Allocator.Persistent);

                _repeatingMeshes_tris_indices = new NativeList<int2>(Allocator.Persistent);
                _repeatingMeshes_verts_indices = new NativeList<int2>(Allocator.Persistent);
                _repeatingMeshes_normals_indices = new NativeList<int2>(Allocator.Persistent);
                _repeatingMeshes_tangents_indices = new NativeList<int2>(Allocator.Persistent);
                _repeatingMeshes_uv0_indices = new NativeList<int2>(Allocator.Persistent);
                _repeatingMeshes_colors_indices = new NativeList<int2>(Allocator.Persistent);

                for (var i = 0; i < _meshDatas.Count; ++i)
                {
                    var meshData = _meshDatas[i];

                    _repeatingMeshes_tris_indices.Add(new int2(_repeatingMeshes_tris.Length, meshData.native_tris.Length));
                    _repeatingMeshes_verts_indices.Add(new int2(_repeatingMeshes_verts.Length, meshData.native_verts.Length));
                    _repeatingMeshes_normals_indices.Add(new int2(_repeatingMeshes_normals.Length, meshData.native_normals.Length));
                    _repeatingMeshes_tangents_indices.Add(new int2(_repeatingMeshes_tangents.Length, meshData.native_tangents.Length));
                    _repeatingMeshes_uv0_indices.Add(new int2(_repeatingMeshes_uv0.Length, meshData.native_uv0.Length));
                    _repeatingMeshes_colors_indices.Add(new int2(_repeatingMeshes_colors.Length, meshData.native_colors.Length));

                    // is there a faster way to do this? hm 
                    for (var j = 0; j < meshData.native_tris.Length; ++j) _repeatingMeshes_tris.Add(meshData.native_tris[j]);
                    for (var j = 0; j < meshData.native_verts.Length; ++j) _repeatingMeshes_verts.Add(meshData.native_verts[j]);
                    for (var j = 0; j < meshData.native_normals.Length; ++j) _repeatingMeshes_normals.Add(meshData.native_normals[j]);
                    for (var j = 0; j < meshData.native_tangents.Length; ++j) _repeatingMeshes_tangents.Add(meshData.native_tangents[j]);
                    for (var j = 0; j < meshData.native_uv0.Length; ++j) _repeatingMeshes_uv0.Add(meshData.native_uv0[j]);
                    for (var j = 0; j < meshData.native_colors.Length; ++j) _repeatingMeshes_colors.Add(meshData.native_colors[j]);
                }

                // store bounds too 
                _repeatingMeshes_bounds = new NativeList<Bounds>(Allocator.Persistent);
                for(var i = 0; i < _meshDatas.Count; ++i)
                {
                    var meshData = _meshDatas[i];
                    _repeatingMeshes_bounds.Add(meshData.mesh.bounds);
                }

            }
        }

        private void DisposeMeshDatas()
        {
            foreach(var meshData in _meshDatas)
            {
                meshData.native_tris.Dispose();
                meshData.native_verts.Dispose();
                meshData.native_normals.Dispose();
                meshData.native_tangents.Dispose();
                meshData.native_uv0.Dispose();
                meshData.native_colors.Dispose();
            }

            _meshDatas.Clear();

            if(_hasNativeData)
            {
                _hasNativeData = false;

                _repeatingMeshes_tris_indices.Dispose();
                _repeatingMeshes_verts_indices.Dispose();
                _repeatingMeshes_normals_indices.Dispose();
                _repeatingMeshes_tangents_indices.Dispose();
                _repeatingMeshes_uv0_indices.Dispose();
                _repeatingMeshes_colors_indices.Dispose();

                _repeatingMeshes_tris.Dispose();
                _repeatingMeshes_verts.Dispose();
                _repeatingMeshes_normals.Dispose();
                _repeatingMeshes_tangents.Dispose();
                _repeatingMeshes_uv0.Dispose();
                _repeatingMeshes_colors.Dispose();

                _repeatingMeshes_bounds.Dispose(); 
            }
        }

        protected override JobHandle ScheduleMeshingJob(JobHandle dependency = default)
        {
            if(RepeatableMeshes == null || RepeatableMeshes.Count == 0)
            {
                return dependency;
            }

            RefreshMeshDatas();
            DetermineSplineSettings(out Space splineSpace, out Matrix4x4 localToWorldMatrix, out Matrix4x4 worldToLocalMatrix);

            var job = new BuildMeshFromSpline_RepeatingRandomMeshes()
            {
                repeatingMeshes_tris = _repeatingMeshes_tris,
                repeatingMeshes_verts = _repeatingMeshes_verts,
                repeatingMeshes_normals = _repeatingMeshes_normals,
                repeatingMeshes_tangents = _repeatingMeshes_tangents,
                repeatingMeshes_uv0 = _repeatingMeshes_uv0,
                repeatingMeshes_colors = _repeatingMeshes_colors,
                repeatingMeshes_bounds = _repeatingMeshes_bounds,

                repeatingMeshes_tris_indices = _repeatingMeshes_tris_indices,
                repeatingMeshes_verts_indices = _repeatingMeshes_verts_indices,
                repeatingMeshes_normals_indices = _repeatingMeshes_normals_indices,
                repeatingMeshes_tangents_indices = _repeatingMeshes_tangents_indices,
                repeatingMeshes_colors_indices = _repeatingMeshes_colors_indices,
                repeatingMeshes_uv0_indices = _repeatingMeshes_uv0_indices,

                repeatingMesh_has_colors = _repeatingMeshes_colors.Length == _repeatingMeshes_verts.Length,
                repeatingMesh_has_uv0 = _repeatingMeshes_uv0.Length == _repeatingMeshes_tris.Length,

                repeatingMeshCount = _meshDatas.Count,

                MeshLocalOffsetVertices = vertexOffset,
                UseRepeatingMeshUVs = UseRepeatingMeshUVs,

                built_to_t = built_to_t,
                quality = quality,
                uv_tile_scale = uv_tile_scale,
                scale = scaleMult,
                rotationEulorOffset = rotationEulorOffset,
                normalsMode = MeshNormalsMode,
                uvsMode = UVsMode,

                verts = _nativeVertices,
                normals = _nativeNormals,
                tangents = _nativeTangents,
                bounds = _nativeBounds,
                colors = _nativeColors,

                uv0s = _nativeUV0,
                uv1s = _nativeUV1,
                tris = _nativeTris,

                Points = SplineReference.NativePoints,
                Mode = SplineReference.GetSplineMode(),
                ClosedSpline = SplineReference.GetSplineClosed(),

                SplineSpace = splineSpace,
                worldToLocalMatrix = worldToLocalMatrix,
                localToWorldMatrix = localToWorldMatrix,

                randomMeshSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue),
            };

            return job.Schedule(dependency);
        }

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
        private struct BuildMeshFromSpline_RepeatingRandomMeshes : IJob
        {
            // settings
            public int quality;
            public float built_to_t;
            public float uv_tile_scale;

            public MeshBuilderNormals normalsMode;
            public MeshBuilderUVs uvsMode;

            public NativeArray<int> repeatingMeshes_tris;
            public NativeArray<Vector3> repeatingMeshes_verts;
            public NativeArray<Vector3> repeatingMeshes_normals;
            public NativeArray<Vector4> repeatingMeshes_tangents;
            public NativeArray<Vector4> repeatingMeshes_uv0;
            public NativeArray<Vector4> repeatingMeshes_colors;
            public NativeArray<Bounds> repeatingMeshes_bounds;
            public NativeList<int2> repeatingMeshes_tris_indices;
            public NativeList<int2> repeatingMeshes_verts_indices;
            public NativeList<int2> repeatingMeshes_normals_indices;
            public NativeList<int2> repeatingMeshes_tangents_indices;
            public NativeList<int2> repeatingMeshes_uv0_indices;
            public NativeList<int2> repeatingMeshes_colors_indices;
            public int repeatingMeshCount;
            public int randomMeshSeed;

            private int GetRepeatingMeshTri(int repeatingMeshIndex, int index)
            {
                var index_offset = repeatingMeshes_tris_indices[repeatingMeshIndex];
                return repeatingMeshes_tris[index_offset.x + index];
            }

            private Vector3 GetRepeatingMeshVert(int repeatingMeshIndex, int index)
            {
                var index_offset = repeatingMeshes_verts_indices[repeatingMeshIndex];
                return repeatingMeshes_verts[index_offset.x + index];
            }

            private Vector3 GetRepeatingMeshNormals(int repeatingMeshIndex, int index)
            {
                var index_offset = repeatingMeshes_normals_indices[repeatingMeshIndex];
                return repeatingMeshes_normals[index_offset.x + index];
            }

            private Vector3 GetRepeatingMeshTangents(int repeatingMeshIndex, int index)
            {
                var index_offset = repeatingMeshes_tangents_indices[repeatingMeshIndex];
                return repeatingMeshes_tangents[index_offset.x + index];
            }

            private Vector3 GetRepeatingMeshUv0(int repeatingMeshIndex, int index)
            {
                var index_offset = repeatingMeshes_uv0_indices[repeatingMeshIndex];
                return repeatingMeshes_uv0[index_offset.x + index];
            }

            private Vector3 GetRepeatingMeshColors(int repeatingMeshIndex, int index)
            {
                var index_offset = repeatingMeshes_colors_indices[repeatingMeshIndex];
                return repeatingMeshes_colors[index_offset.x + index];
            }

            public bool repeatingMesh_has_uv0;
            public bool repeatingMesh_has_colors;

            public bool UseRepeatingMeshUVs;
            public Vector3 scale;
            public Vector3 rotationEulorOffset;

            // mesh data 
            public NativeList<Vector3> verts;
            public NativeList<Vector3> normals;
            public NativeList<Vector4> tangents;
            public NativeList<Vector4> uv0s;
            public NativeList<Vector4> uv1s;
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

            private int2 Get1Dto2D(int index, int width)
            {
                var x = index % width;
                var y = index / width;
                return new int2(x, y); 
            }

            private int Get2Dto1D(int2 pos, int width)
            {
                return pos.y * width + pos.x; 
            }

            public void Execute()
            {
                var trackedBounds = new Bounds();

                // reset data 
                verts.Clear();
                normals.Clear();
                uv0s.Clear();
                uv1s.Clear();
                tris.Clear();
                tangents.Clear();
                colors.Clear();

                // setup 
                var firstPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0f);
                var previousPosition = firstPoint.position;

                // closed splines overlap a bit so we dont have to stitch 
                var full_loop = ClosedSpline  && built_to_t >= 1f;

                var rotation = Quaternion.Euler(rotationEulorOffset); 

                var repeatingBoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var repeatingBoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                for(var ri = 0; ri < repeatingMeshes_verts.Length; ++ri)
                {
                    var vert = repeatingMeshes_verts[ri];
                    repeatingBoundsMin = Vector3.Min(repeatingBoundsMin, vert);
                    repeatingBoundsMax = Vector3.Max(repeatingBoundsMax, vert);
                }

                var boundsDistance = Vector3.Distance(repeatingBoundsMin, repeatingBoundsMax);
                var repeatCount = 0;

                var random = new Unity.Mathematics.Random((uint) randomMeshSeed);
                var tri_offset = 0;

                for (var meshIndex = 0; meshIndex < quality; ++meshIndex)
                {
                    var repeatableMeshIndex = random.NextInt(0, repeatingMeshCount);
                    var repeatingMeshBounds = repeatingMeshes_bounds[repeatableMeshIndex];
                    var meshBoundsZ = (repeatingMeshBounds.max.z - repeatingMeshBounds.min.z);
                    var totalMeshZ = meshBoundsZ * quality;
                    var currentMeshZ = meshIndex * meshBoundsZ;

                    // lightmap chunk data 
                    var lightmapGridSize = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(quality)));
                    var lightmapChunk = Get1Dto2D(meshIndex, lightmapGridSize); 
                    var lightmapInverseWidth = 1f / lightmapGridSize;
                    var lightmapOffset = new Vector4(lightmapInverseWidth * lightmapChunk.x, lightmapInverseWidth * lightmapChunk.y, 0f, 0f);
                    var lightmapScale = new Vector4(lightmapInverseWidth, lightmapInverseWidth, 0f, 0f);

                    // pasted the mesh over and over, bending the verts to be along the spline 
                    var brokenEarly = false;
                    var repeatingMeshVertIndexData = repeatingMeshes_verts_indices[repeatableMeshIndex];
                    for (var ri = 0; ri < repeatingMeshVertIndexData.y; ++ri)
                    {
                        var repeating_vertex = GetRepeatingMeshVert(repeatableMeshIndex, ri);
                        var normal = GetRepeatingMeshNormals(repeatableMeshIndex, ri);
                        var tangent = GetRepeatingMeshTangents(repeatableMeshIndex, ri);

                        if (repeatingMesh_has_colors)
                        {
                            var color = GetRepeatingMeshColors(repeatableMeshIndex, ri);
                            colors.Add(color);
                        }

                        var meshBoundsWithInnerZ = currentMeshZ + (repeating_vertex.z - repeatingMeshBounds.min.z); 
                        var innerMesh_t = meshBoundsWithInnerZ / totalMeshZ;

                        if(innerMesh_t > built_to_t)
                        {
                            brokenEarly = true; 
                        }

                        var vertex_splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, innerMesh_t);

                        var trs = Matrix4x4.TRS(
                            vertex_splinePoint.position,
                            vertex_splinePoint.rotation, 
                            Vector3.Scale(vertex_splinePoint.scale, scale)
                        );

                        var vertex = trs.MultiplyPoint(rotation * new Vector3(repeating_vertex.x, repeating_vertex.y, 0) + MeshLocalOffsetVertices);
                        normal = trs.MultiplyVector(rotation * normal);
                        tangent = trs.MultiplyVector(rotation * tangent);

                        verts.Add(vertex);
                        normals.Add(normal);
                        tangents.Add(tangent);

                        if (UseRepeatingMeshUVs && repeatingMesh_has_uv0)
                        {
                            var repeatingMeshUv0 = GetRepeatingMeshUv0(repeatableMeshIndex, ri);
                            uv0s.Add(repeatingMeshUv0);
                            uv1s.Add(Vector4.Scale(repeatingMeshUv0, lightmapScale) + lightmapOffset);
                        }
                        else
                        {
                            var uv_x = innerMesh_t;
                            var uv_y = (repeating_vertex.y - repeatingBoundsMin.y) / (repeatingBoundsMax.y - repeatingBoundsMin.y);

                            if (uvsMode == MeshBuilderUVs.Tile)
                            {
                                uv_x = (innerMesh_t * uv_tile_scale);
                            }

                            uv0s.Add(new Vector4(uv_x, uv_y));
                            uv1s.Add(Vector4.Scale(new Vector4(innerMesh_t, uv_y), lightmapScale) + lightmapOffset);
                        }

                        // track bounds.. 
                        trackedBounds.min = Vector3.Min(trackedBounds.min, vertex);
                        trackedBounds.max = Vector3.Max(trackedBounds.max, vertex);
                    }

                    // copy/paste tris from repeatable mesh 
                    var repeatableMeshTriIndexData = repeatingMeshes_tris_indices[repeatableMeshIndex];
                    for (var ri = 0; ri < repeatableMeshTriIndexData.y; ++ri)
                    {
                        tris.Add(GetRepeatingMeshTri(repeatableMeshIndex, ri) + tri_offset);
                    }

                    tri_offset += repeatingMeshes_verts_indices[repeatableMeshIndex].y;
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

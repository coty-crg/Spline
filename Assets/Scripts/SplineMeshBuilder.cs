using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
public class SplineMeshBuilder : MonoBehaviour
{
    public Spline SplineReference;
    public bool RebuildEveryFrame;

    [Range(4, 1024)] public int quality = 256;
    public float uv_tile_scale = 1f;

    public float width = 1f;
    public float height = 1f;

    private Mesh mesh;

    private void OnEnable()
    {
        Rebuild(); 
    }

    private void OnDisable()
    {
        Release(); 
    }

    private void Update()
    {
        if(RebuildEveryFrame)
        {
            Rebuild(); 
        }
    }

    public void Release()
    {
        if(mesh != null)
        {
            if(Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh); 
            }
        }
    }

    public void Rebuild()
    {
        if (SplineReference == null) 
            return;

        Release();

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        var current_uv_step = 0f;

        var previousPosition = SplineReference.GetPoint(0f).position;

        for (var step = 1; step <= quality; ++step)
        {
            var t0 = (float) (step - 1) / quality;
            var t1 = (float) (step - 0) / quality;

            var splinePoint0 = SplineReference.GetPoint(t0);
            var splinePoint1 = SplineReference.GetPoint(t1);

            var position0 = splinePoint0.position;
            var position1 = splinePoint1.position;

            var up0 = splinePoint0.up;
            var up1 = splinePoint1.up;

            var position = Vector3.Lerp(position0, position1, 0.5f);
            var forward = (position1 - position0).normalized;
            var up = Vector3.Slerp(up0, up1, 0.5f);
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
        for(var v = 0; v < verts.Count; v += 4)
        {
            tris.Add(v + 0);
            tris.Add(v + 1);
            tris.Add(v + 3);
            tris.Add(v + 0);
            tris.Add(v + 3);
            tris.Add(v + 2);

            if(v < verts.Count - 4)
            {
                tris.Add(v + 2);
                tris.Add(v + 3);
                tris.Add(v + 4);
                tris.Add(v + 4);
                tris.Add(v + 3);
                tris.Add(v + 5);
            }
        }

        if(height > 0f)
        {

            var vert_count = verts.Count;

            // floor 
            for(var v = 0; v < vert_count; ++v)
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
            for(var v = vert_count; v < verts.Count; v += 4)
            {
                tris.Add(v + 2);
                tris.Add(v + 3);
                tris.Add(v + 0);

                tris.Add(v + 3);
                tris.Add(v + 1);
                tris.Add(v + 0);

                if (v < verts.Count - 4)
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
            for(var v = 0; v < vert_count; v += 4)
            {
                // right wall 
                tris.Add(v + vert_count + 1);
                tris.Add(v + 3);
                tris.Add(v + 1);

                tris.Add(v + 3);
                tris.Add(v + vert_count + 1);
                tris.Add(v + vert_count + 3);

                // left wall
                tris.Add(v + vert_count + 0);
                tris.Add(v + 0);
                tris.Add(v + 2);

                tris.Add(v + vert_count + 2);
                tris.Add(v + vert_count + 0);
                tris.Add(v + 2);


                //
                if (v < vert_count - 4)
                {

                    // right wall 
                    tris.Add(v + vert_count + 1 + 2);
                    tris.Add(v + 3 + 2);
                    tris.Add(v + 1 + 2);
                    tris.Add(v + 3 + 2);
                    tris.Add(v + vert_count + 1 + 2);
                    tris.Add(v + vert_count + 3 + 2);

                    // left wall
                    tris.Add(v + vert_count + 0 + 2);
                    tris.Add(v + 0 + 2);
                    tris.Add(v + 2 + 2);
                    tris.Add(v + vert_count + 2 + 2);
                    tris.Add(v + vert_count + 0 + 2);
                    tris.Add(v + 2 + 2);
                }
            }
        }

        mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        var meshFilter = GetComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh; 
    }
}

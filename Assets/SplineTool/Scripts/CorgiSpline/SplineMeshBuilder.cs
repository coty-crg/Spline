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
    [Range(0, 16)] public int cap_quality = 4;

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


        var start_forward = SplineReference.GetForward(0f);
        var end_forward = SplineReference.GetForward(0.9f);

        var firstPoint = SplineReference.GetPoint(0f);
        var lastPoint = SplineReference.GetPoint(1f);
        var previousPosition = firstPoint.position;

        quality = quality - quality % 2;

        for (var step = 0; step < quality; ++step)
        {
            var t0 = (float) (step - 1) / quality;
            var t1 = (float) (step - 0) / quality;

            var splinePoint0 = SplineReference.GetPoint(t0);
            var splinePoint1 = SplineReference.GetPoint(t1);

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

        var floor_vert_index = verts.Count;

        if(height > 0f)
        {

            // floor 
            for(var v = 0; v < floor_vert_index; ++v)
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
            for(var v = floor_vert_index; v < verts.Count; v += 4)
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
            for(var v = 0; v < floor_vert_index; v += 4)
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

        // start cap 
        // var start_cap_vert_index = verts.Count;
        // 
        // cap_quality = cap_quality - cap_quality % 2;
        // 
        // for (var step = 0; step <= cap_quality; ++step)
        // {
        // 
        //     var t = (float) step / cap_quality;
        // 
        //     var x = Mathf.Sin(t * Mathf.PI + Mathf.PI * 0.5f);
        //     var y = Mathf.Cos(t * Mathf.PI + Mathf.PI * 0.5f);
        // 
        //     var up = firstPoint.up;
        //     var forward = start_forward * y * width;
        //     var right = Vector3.Cross(start_forward, up) * x * width;
        // 
        //     var vert0 = firstPoint.position;
        //     var vert1 = firstPoint.position + forward + right;
        // 
        //     verts.Add(vert0);
        //     verts.Add(vert1);
        // 
        //     normals.Add(up);
        //     normals.Add(up);
        // 
        //     uvs.Add(new Vector2(x, y));
        //     uvs.Add(new Vector2(x, y));
        // }
        // 
        // // stitch 
        // tris.Add(3);
        // tris.Add(0);
        // tris.Add(start_cap_vert_index + 1);
        // 
        // tris.Add(verts.Count - 1);
        // tris.Add(0);
        // tris.Add(2);
        // 
        // // generate tris 
        // for (var v = start_cap_vert_index; v < verts.Count - 2; v += 2)
        // {
        //     tris.Add(v + 3);
        //     tris.Add(v + 1);
        //     tris.Add(v + 0);
        // }
        // 
        // if (height > 0f)
        // {
        //     var floor_cap_vert = verts.Count;
        // 
        //     // floor verts for cap 
        //     for(var v = start_cap_vert_index; v < floor_cap_vert; ++v)
        //     {
        //         var new_vert = verts[v];
        //         var new_normal = normals[v] * -1f;
        //         var new_uv = uvs[v];
        // 
        //         new_uv.x = 1.0f - new_uv.x;
        //         // new_uv.y = 1.0f - new_uv.y;
        // 
        //         new_vert += new_normal * height;
        // 
        //         verts.Add(new_vert);
        //         normals.Add(new_normal);
        //         uvs.Add(new_uv);
        //     }
        // 
        //     // stitch 
        //     tris.Add(floor_cap_vert + 1);
        //     tris.Add(floor_vert_index + 0);
        //     tris.Add(floor_vert_index + 3);
        // 
        //     tris.Add(floor_vert_index + 2);
        //     tris.Add(floor_vert_index + 0);
        //     tris.Add(verts.Count - 1);
        // 
        //     // east walls 
        //     tris.Add(3);
        //     tris.Add(start_cap_vert_index + 1);
        //     tris.Add(floor_cap_vert + 1);
        // 
        //     tris.Add(3);
        //     tris.Add(floor_cap_vert + 1);
        //     tris.Add(floor_vert_index + 3);
        // 
        //     // west walls 
        //     tris.Add(2);
        //     tris.Add(verts.Count - 1);
        //     tris.Add(floor_cap_vert - 1);
        // 
        //     tris.Add(floor_vert_index + 2);
        //     tris.Add(verts.Count - 1);
        //     tris.Add(2);
        // 
        //     // generate tris 
        //     for (var v = floor_cap_vert; v < verts.Count - 2; v += 2)
        //     {
        //         tris.Add(v + 0);
        //         tris.Add(v + 1);
        //         tris.Add(v + 3);
        //     }
        // 
        //     var floor_delta = floor_cap_vert - start_cap_vert_index;
        //     for (var v = start_cap_vert_index; v < floor_cap_vert - 3; v += 1)
        //     {
        //         tris.Add(v + 1);
        //         tris.Add(v + 3);
        //         tris.Add(v + floor_delta + 3);
        // 
        //         tris.Add(v + 1);
        //         tris.Add(v + floor_delta + 3);
        //         tris.Add(v + floor_delta + 1);
        //     }
        // }


        // end cap 

        // var end_cap_vert_index = verts.Count;
        // 
        // Vector3 final_vert_0;
        // Vector3 final_vert_1;
        // 
        // {
        // 
        //     var t0 = (float)(quality - 2) / quality;
        //     var t1 = (float)(quality - 1) / quality;
        // 
        //     var splinePoint0 = SplineReference.GetPoint(t0);
        //     var splinePoint1 = SplineReference.GetPoint(t1);
        // 
        //     var position0 = splinePoint0.position;
        //     var position1 = splinePoint1.position;
        // 
        //     var up0 = splinePoint0.up;
        //     var up1 = splinePoint1.up;
        // 
        //     var position = Vector3.Lerp(position0, position1, 0.5f);
        //     var forward = (position1 - position0).normalized;
        //     var up = Vector3.Slerp(up0, up1, 0.5f);
        //     var right = Vector3.Cross(forward, up);
        // 
        //     // verts 
        //     final_vert_0 = position - right * width;
        //     final_vert_1 = position + right * width;
        // }
        // 
        // 
        // for (var step = 0; step <= cap_quality; ++step)
        // {
        // 
        //     var t0 = (float) (step + 0) / cap_quality;
        //     var t1 = (float) (step + 1) / cap_quality;
        // 
        //     var x0 = Mathf.Sin(t0 * Mathf.PI + Mathf.PI * 0.5f);
        //     var y0 = Mathf.Cos(t0 * Mathf.PI + Mathf.PI * 0.5f);
        // 
        //     var x1 = Mathf.Sin(t1 * Mathf.PI + Mathf.PI * 0.5f);
        //     var y1 = Mathf.Cos(t1 * Mathf.PI + Mathf.PI * 0.5f);
        // 
        //     var up0 = lastPoint.up;
        //     var forward0 = -end_forward * y0 * width;
        //     var right0 = Vector3.Cross(end_forward, up0) * x0 * width;
        // 
        //     var up1 = lastPoint.up;
        //     var forward1 = -end_forward * y1 * width;
        //     var right1 = Vector3.Cross(end_forward, up1) * x1 * width;
        // 
        //     var vert0 = lastPoint.position;
        //     var vert1 = lastPoint.position + forward0 + right0;
        //     var vert2 = lastPoint.position + forward1 + right1;
        // 
        //     var tri_vert = verts.Count;
        // 
        //     verts.Add(vert0);
        //     verts.Add(vert1);
        //     verts.Add(vert2);
        // 
        //     normals.Add(up0);
        //     normals.Add(up0);
        //     normals.Add(up1);
        // 
        //     uvs.Add(new Vector2(0f, 0f));
        //     uvs.Add(new Vector2(x0, y0));
        //     uvs.Add(new Vector2(x1, y1));
        // 
        //     Debug.DrawRay(lastPoint.position, forward0 + right0, Color.green, 1f);
        // 
        //     tris.Add(tri_vert + 0);
        //     tris.Add(tri_vert + 1);
        //     tris.Add(tri_vert + 2);
        // }
        // 
        // tris.Add(end_cap_vert_index + 1);
        // tris.Add(end_cap_vert_index + 1);
        // tris.Add(end_cap_vert_index + 1);



        // var end_cap_vert_index = verts.Count;
        // 
        // for (var step = 0; step <= cap_quality; ++step)
        // {
        // 
        //     var t = (float)step / cap_quality;
        // 
        //     var x = Mathf.Sin(t * Mathf.PI + Mathf.PI * 0.5f);
        //     var y = Mathf.Cos(t * Mathf.PI + Mathf.PI * 0.5f);
        // 
        //     var up = lastPoint.up;
        //     var forward = - end_forward * y * width;
        //     var right = Vector3.Cross(end_forward, up) * x * width;
        // 
        //     var vert0 = lastPoint.position;
        //     var vert1 = lastPoint.position + forward + right;
        // 
        //     verts.Add(vert0);
        //     verts.Add(vert1);
        // 
        //     normals.Add(up);
        //     normals.Add(up);
        // 
        //     uvs.Add(new Vector2(x, y));
        //     uvs.Add(new Vector2(x, y));
        // }
        // 
        // for (var v = end_cap_vert_index; v < verts.Count - 2; v += 2)
        // {
        //     tris.Add(v + 0);
        //     tris.Add(v + 1);
        //     tris.Add(v + 3);
        // }
        // 
        // // stitch 
        // tris.Add(end_cap_vert_index + 1);
        // tris.Add(floor_vert_index - 5);
        // tris.Add(floor_vert_index - 1);
        // 
        // tris.Add(verts.Count - 1);
        // tris.Add(floor_vert_index - 50);
        // tris.Add(floor_vert_index - 48);
        // 
        // 
        // if (height > 0f)
        // {
        //     var floor_cap_vert = verts.Count;
        // 
        //     // floor verts for cap 
        //     for (var v = end_cap_vert_index; v < floor_cap_vert; ++v)
        //     {
        //         var new_vert = verts[v];
        //         var new_normal = normals[v] * -1f;
        //         var new_uv = uvs[v];
        // 
        //         new_uv.x = 1.0f - new_uv.x;
        //         // new_uv.y = 1.0f - new_uv.y;
        // 
        //         new_vert += new_normal * height;
        // 
        //         verts.Add(new_vert);
        //         normals.Add(new_normal);
        //         uvs.Add(new_uv);
        //     }
        // 
        //     // stitch 
        //     var end_floor_verts = floor_vert_index * 2;
        // 
        //     // tris.Add(floor_cap_vert + 1);
        //     // tris.Add(end_floor_verts - 48);
        //     // tris.Add(end_floor_verts - 49);
        // 
        //     // tris.Add(tris[end_tris_stitch + 3] + end_delta);
        //     // tris.Add(tris[end_tris_stitch + 4] + end_delta);
        //     // tris.Add(tris[end_tris_stitch + 5] + end_delta);
        // 
        //     // tris.Add(verts.Count - 1);
        //     // tris.Add(floor_vert_index - 50);
        //     // tris.Add(floor_vert_index - 48);
        // 
        //     // east walls 
        //     // tris.Add(floor_cap_vert + 1);
        //     // tris.Add(start_cap_vert_index + 1);
        //     // tris.Add(3);
        //     // 
        //     // tris.Add(floor_vert_index + 3);
        //     // tris.Add(floor_cap_vert + 1);
        //     // tris.Add(3);
        //     // 
        //     // // west walls 
        //     // tris.Add(floor_cap_vert - 1);
        //     // tris.Add(verts.Count - 1);
        //     // tris.Add(2);
        //     // 
        //     // tris.Add(2);
        //     // tris.Add(verts.Count - 1);
        //     // tris.Add(floor_vert_index + 2);
        // 
        //     // generate tris 
        //     for (var v = floor_cap_vert; v < verts.Count - 2; v += 2)
        //     {
        //         tris.Add(v + 3);
        //         tris.Add(v + 1);
        //         tris.Add(v + 0);
        //     }
        // 
        //     var floor_delta = floor_cap_vert - end_cap_vert_index;
        //     for (var v = end_cap_vert_index; v < floor_cap_vert - 3; v += 1)
        //     {
        //         tris.Add(v + floor_delta + 3);
        //         tris.Add(v + 3);
        //         tris.Add(v + 1);
        // 
        //         tris.Add(v + floor_delta + 1);
        //         tris.Add(v + floor_delta + 3);
        //         tris.Add(v + 1);
        //     }
        // }






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

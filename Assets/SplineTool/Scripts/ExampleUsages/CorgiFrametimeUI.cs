using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CorgiFrametimeUI : MonoBehaviour
{
    public Text textArea;
    public MeshFilter meshFilter;
    public CorgiSpline.SplineMeshBuilder meshBuilder;

    private void Update()
    {
        Application.targetFrameRate = 10000;
        
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Time.deltaTime: {Time.deltaTime:N4}");
        sb.AppendLine($"Time.smoothDeltaTime: {Time.smoothDeltaTime:N4}");
        sb.AppendLine($"{1f / Time.deltaTime:N2} fps");

        if(meshBuilder != null)
        {
            sb.AppendLine($"~{meshBuilder._prevCompleteMs:N4} ms to build mesh");
        }

        if(meshFilter != null && meshFilter.sharedMesh != null)
        {
            var mesh = meshFilter.sharedMesh;
            var vertCount = mesh.vertexCount;
            var triCount = mesh.GetIndexCount(0) / 3;

            sb.AppendLine($"{vertCount:N0} verts");
            sb.AppendLine($"{triCount:N0} tris");
        }

        textArea.text = sb.ToString(); 

    }
}

#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CorgiSpline
{
    [CustomEditor(typeof(SplineMeshBuilder_RepeatingMesh))]
    public class SplineMeshBuilder_RepeatingMesh_Editor : SplineMeshBuilder_Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder_RepeatingMesh) target;

            if (instance.RepeatableMesh == null)
            {
                EditorGUILayout.HelpBox("RepeatableMesh is null. Please assign a mesh to repeat!", MessageType.Error);
            }
            else
            {
                var has_normals = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal);
                var has_tangents = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
                var has_uv0 = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                var has_color = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);

                if(!has_normals)
                {
                    EditorGUILayout.HelpBox("RepeatableMesh does not contain normals. It is recommended to add them.", 
                        MessageType.Warning);
                }

                if (!has_tangents)
                {
                    EditorGUILayout.HelpBox("RepeatableMesh does not contain tangents. It is recommended to add them.", 
                        MessageType.Warning);
                }

                if (!has_uv0 && instance.UseRepeatingMeshUVs)
                {
                    EditorGUILayout.HelpBox("RepeatableMesh does not contain UVs. It is recommended to add them, because you have enabled UseRepeatingMeshUVs.", 
                        MessageType.Info);
                }

                if (!has_color)
                {
                    EditorGUILayout.HelpBox("RepeatableMesh does not contain vertex colors. It is recommended to add them.", 
                        MessageType.Info);
                }
            }
        }
    }
}
#endif
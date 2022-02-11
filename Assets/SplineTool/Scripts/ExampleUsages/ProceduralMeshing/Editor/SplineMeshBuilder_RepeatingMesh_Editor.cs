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
        protected SerializedProperty RepeatableMesh;
        protected SerializedProperty MeshLocalOffsetVertices;
        protected SerializedProperty UseRepeatingMeshUVs;

        protected override void OnEnable()
        {
            base.OnEnable();

            RepeatableMesh              = serializedObject.FindProperty("RepeatableMesh");
            MeshLocalOffsetVertices     = serializedObject.FindProperty("MeshLocalOffsetVertices");
            UseRepeatingMeshUVs         = serializedObject.FindProperty("UseRepeatingMeshUVs");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder_RepeatingMesh) target;

            GUILayout.BeginVertical("GroupBox");
            {
                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Repeating Mesh", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(RepeatableMesh);

                    if (instance.RepeatableMesh == null)
                    {
                        EditorGUILayout.HelpBox("RepeatableMesh is null. Please assign a mesh to repeat!", MessageType.Error);
                    }
                    else
                    {
                        EditorGUILayout.Space(); 
                        EditorGUILayout.BeginHorizontal();
                        {
                            // draw preview 
                            var assetPreview = AssetPreview.GetAssetPreview(instance.RepeatableMesh);
                            if (assetPreview != null)
                            {
                                EditorGUILayout.Space();
                                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                                GUILayout.Label(assetPreview, style);
                                EditorGUILayout.Space();
                            }

                            EditorGUILayout.BeginVertical();
                            {
                                // warnings for missing vertex data 
                                var has_normals = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal);
                                var has_tangents = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
                                var has_uv0 = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                                var has_color = instance.RepeatableMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);

                                if (!has_normals)
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
                            EditorGUILayout.EndVertical();
                        }
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space();
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Repeating Mesh Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(MeshLocalOffsetVertices);
                    EditorGUILayout.PropertyField(UseRepeatingMeshUVs);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            if (GUI.changed)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        [MenuItem("GameObject/CorgiSpline/Spline Mesh (repeating mesh)", priority = 10)]
        public static void MenuItemCreateMeshBuilder_RepeatingMesh()
        {
            var newGameobject = new GameObject("NewSplineMesh_RepeatingMesh");

            var spline = newGameobject.AddComponent<Spline>();
            var meshBuilder = newGameobject.AddComponent<SplineMeshBuilder_RepeatingMesh>();
                meshBuilder.SplineReference = spline;

            if (Selection.activeTransform != null)
            {
                newGameobject.transform.SetParent(Selection.activeTransform);
            }
        }
    }
}
#endif
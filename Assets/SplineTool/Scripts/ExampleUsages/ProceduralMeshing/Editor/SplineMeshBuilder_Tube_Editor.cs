#if UNITY_EDITOR
namespace CorgiSpline
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;

    [CustomEditor(typeof(SplineMeshBuilder_Tube))]
    public class SplineMeshBuilder_Tube_Editor : SplineMeshBuilder_Editor
    {
        protected SerializedProperty tube_quality;
        protected SerializedProperty minimum_distance_between_points;
        protected SerializedProperty max_distance_between_points;
        protected SerializedProperty minimum_dot_between_forwards;

        protected override void OnEnable()
        {
            base.OnEnable();

            tube_quality                        = serializedObject.FindProperty("tube_quality");
            minimum_distance_between_points     = serializedObject.FindProperty("minimum_distance_between_points");
            max_distance_between_points         = serializedObject.FindProperty("max_distance_between_points");
            minimum_dot_between_forwards        = serializedObject.FindProperty("minimum_dot_between_forwards");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder_Tube) target;

            GUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.LabelField("Tube Quality Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(tube_quality);
                EditorGUILayout.PropertyField(minimum_distance_between_points);
                EditorGUILayout.PropertyField(max_distance_between_points);
                EditorGUILayout.PropertyField(minimum_dot_between_forwards);
            }
            GUILayout.EndVertical(); 

            if (GUI.changed)
            {
                instance.EditorOnSplineUpdated(instance.SplineReference);
                serializedObject.ApplyModifiedProperties();
            }
        }

        [MenuItem("GameObject/CorgiSpline/Spline Mesh (tube)", priority = 10)]
        public static void MenuItemCreateMeshBuilder_Tubey()
        {
            var editorConfig = SplineEditorConfig.FindConfig();
            var newGameobject = new GameObject("NewSplineMesh_Tube");
                newGameobject.AddComponent<MeshFilter>();

            var spline = newGameobject.AddComponent<Spline>();
                spline.SetSplineSpace(Space.Self, false);
                spline.EditorAlwaysFacePointsForwardAndUp = true;

            var meshBuilder = newGameobject.AddComponent<SplineMeshBuilder_Tube>();
                meshBuilder.SplineReference = spline;

            var newMeshRenderer = newGameobject.AddComponent<MeshRenderer>();
                newMeshRenderer.sharedMaterial = editorConfig.defaultMaterialForRenderers;

            meshBuilder.use_splinepoint_rotations = true;
            meshBuilder.use_splinepoint_scale = true;

            // toggle to force register 
            meshBuilder.enabled = false;
            meshBuilder.enabled = true;

            if (Selection.activeTransform != null)
            {
                newGameobject.transform.SetParent(Selection.activeTransform);
            }

            Selection.activeGameObject = newGameobject;
        }
    }
}
#endif
#if UNITY_EDITOR 
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace CorgiSpline
{
    [CustomEditor(typeof(SplineMeshBuilder))]
    public class SplineMeshBuilder_Editor : Editor
    {
        protected SerializedProperty SplineReference;
        protected SerializedProperty RebuildEveryFrame;
        protected SerializedProperty RebuildOnEnable;
        protected SerializedProperty AllowAsyncRebuild;
        protected SerializedProperty SerializeMesh;
        protected SerializedProperty built_to_t;
        protected SerializedProperty quality;
        protected SerializedProperty width;
        protected SerializedProperty height;
        protected SerializedProperty uv_tile_scale;
        protected SerializedProperty cover_ends_with_quads;
        protected SerializedProperty uv_stretch_instead_of_tile;
        protected SerializedProperty use_splinepoint_rotations;
        protected SerializedProperty use_splinepoint_scale;
        protected SerializedProperty _serializedMesh;

        protected virtual void OnEnable()
        {
            SplineReference             = serializedObject.FindProperty("SplineReference");
            RebuildEveryFrame           = serializedObject.FindProperty("RebuildEveryFrame");
            RebuildOnEnable             = serializedObject.FindProperty("RebuildOnEnable");
            AllowAsyncRebuild           = serializedObject.FindProperty("AllowAsyncRebuild");
            SerializeMesh               = serializedObject.FindProperty("SerializeMesh");
            built_to_t                  = serializedObject.FindProperty("built_to_t");
            quality                     = serializedObject.FindProperty("quality");
            width                       = serializedObject.FindProperty("width");
            height                      = serializedObject.FindProperty("height");
            uv_tile_scale               = serializedObject.FindProperty("uv_tile_scale");
            cover_ends_with_quads       = serializedObject.FindProperty("cover_ends_with_quads");
            uv_stretch_instead_of_tile  = serializedObject.FindProperty("uv_stretch_instead_of_tile");
            use_splinepoint_rotations   = serializedObject.FindProperty("use_splinepoint_rotations");
            use_splinepoint_scale       = serializedObject.FindProperty("use_splinepoint_scale");
            _serializedMesh             = serializedObject.FindProperty("_serializedMesh");
        }

        public override void OnInspectorGUI()
        {
            #if !CORGI_DETECTED_BURST
                EditorGUILayout.HelpBox("Burst is not detected. It is recommended you install it from the package manaager.", MessageType.Warning);
            #endif

            var instance = (SplineMeshBuilder) target;

            if(instance.GetPreviousMeshingDurationMs() > 0f)
            {
                EditorGUILayout.HelpBox($"Meshing took {instance.GetPreviousMeshingDurationMs()}ms to complete.", MessageType.Info);
            }

            EditorGUILayout.Space();

            GUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.LabelField("Spline", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(SplineReference);

                if (instance.SplineReference == null)
                {
                    EditorGUILayout.HelpBox("SplineReference is null. Please assign a spline to sample from!", MessageType.Error);

                    if (GUILayout.Button("Create&Assign Spline"))
                    {
                        Undo.RecordObject(instance, "Create&Assign Spline");
                        Undo.RecordObject(instance.gameObject, "Create&Assign Spline");

                        instance.SplineReference = instance.gameObject.GetComponent<Spline>();
                        if (instance.SplineReference == null)
                        {
                            instance.SplineReference = instance.gameObject.AddComponent<Spline>();
                        }
                    }
                }
                else if(!Application.isPlaying)
                {
                    var splineOnGameobject = instance.GetComponent<Spline>();
                    if(splineOnGameobject != null && splineOnGameobject != instance.SplineReference)
                    {
                        EditorGUILayout.HelpBox("Referenced spline is not attached to this gameobject, but there is a spline on this gameobject.", MessageType.Warning);

                        EditorGUILayout.BeginHorizontal();
                        {
                            if (GUILayout.Button("Reference attached Spline"))
                            {
                                Undo.RecordObject(instance, "Reference attached Spline");
                                instance.SplineReference = splineOnGameobject;
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.LabelField("Procedural Mesh Settings", EditorStyles.boldLabel);

                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(SerializeMesh);

                    if(!instance.SerializeMesh)
                    {
                        EditorGUILayout.PropertyField(RebuildEveryFrame);
                        EditorGUILayout.PropertyField(RebuildOnEnable);
                        EditorGUILayout.PropertyField(AllowAsyncRebuild);
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Quality Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(quality);
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Visual Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(built_to_t);
                    EditorGUILayout.PropertyField(width);
                    EditorGUILayout.PropertyField(height);
                    EditorGUILayout.PropertyField(uv_tile_scale);
                    EditorGUILayout.PropertyField(cover_ends_with_quads);
                    EditorGUILayout.PropertyField(uv_stretch_instead_of_tile);

                    EditorGUILayout.PropertyField(use_splinepoint_rotations);
                    EditorGUILayout.PropertyField(use_splinepoint_scale);
                }
                GUILayout.EndVertical();

            }
            GUILayout.EndVertical();

            if(GUI.changed)
            {
                serializedObject.ApplyModifiedProperties();
            }

            // uncomment to draw the normal inspector.. 
            // base.OnInspectorGUI();
        }
    }
}
#endif
﻿namespace CorgiSpline
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
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
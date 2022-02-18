#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CorgiSpline
{ 
    [CustomEditor(typeof(PrefabRepeater))]
    public class PrefabRepeaterEditor : Editor
    {
        protected SerializedProperty SplineReference;
        protected SerializedProperty PrefabToRepeat;
        protected SerializedProperty RepeatCount;
        protected SerializedProperty RepeatPercentage;
        protected SerializedProperty RefreshOnEnable;
        protected SerializedProperty RefreshOnUpdate;
        protected SerializedProperty PositionOffset;
        protected SerializedProperty ScaleOffset;
        protected SerializedProperty RotationEulorOffset;
        protected SerializedProperty RandomizedOffsetRange;
        protected SerializedProperty RandomizedScaleRange;
        protected SerializedProperty RandomizedRotationEulorRange;

        private void OnEnable()
        {
            SplineReference                 = serializedObject.FindProperty("SplineReference");
            PrefabToRepeat                  = serializedObject.FindProperty("PrefabToRepeat");
            RepeatCount                     = serializedObject.FindProperty("RepeatCount");
            RepeatPercentage                = serializedObject.FindProperty("RepeatPercentage");
            RefreshOnEnable                 = serializedObject.FindProperty("RefreshOnEnable");
            RefreshOnUpdate                 = serializedObject.FindProperty("RefreshOnUpdate");
            PositionOffset                  = serializedObject.FindProperty("PositionOffset");
            ScaleOffset                     = serializedObject.FindProperty("ScaleOffset");
            RotationEulorOffset             = serializedObject.FindProperty("RotationEulorOffset");
            RandomizedOffsetRange           = serializedObject.FindProperty("RandomizedOffsetRange");
            RandomizedScaleRange            = serializedObject.FindProperty("RandomizedScaleRange");
            RandomizedRotationEulorRange    = serializedObject.FindProperty("RandomizedRotationEulorRange");
        }

        public override void OnInspectorGUI()
        {
            var instance = (PrefabRepeater) target;
            var prevSplineReference = instance.SplineReference;

            GUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.LabelField("Spline", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(SplineReference);

                if (instance.SplineReference == null)
                {
                    EditorGUILayout.HelpBox("SplineReference is null. Please assign a spline to sample from!", MessageType.Error);
                }
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(PrefabToRepeat);
                EditorGUILayout.Space();

                if (instance.PrefabToRepeat == null)
                {
                    EditorGUILayout.HelpBox("PrefabToRepeat is null. Please assign a GameObject to repeat!", MessageType.Error);
                }
                else
                {
                    // draw preview 
                    var assetPreview = AssetPreview.GetAssetPreview(instance.PrefabToRepeat);
                    if (assetPreview != null)
                    {
                        EditorGUILayout.Space();
                        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                        GUILayout.Label(assetPreview, style);
                        EditorGUILayout.Space();
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.LabelField("Repeater Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(RepeatCount);
                EditorGUILayout.PropertyField(RepeatPercentage);

                EditorGUILayout.LabelField("Offsets", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(PositionOffset);
                EditorGUILayout.PropertyField(ScaleOffset);
                EditorGUILayout.PropertyField(RotationEulorOffset);

                EditorGUILayout.LabelField("Randomness", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(RandomizedOffsetRange);
                EditorGUILayout.PropertyField(RandomizedScaleRange);
                EditorGUILayout.PropertyField(RandomizedRotationEulorRange);

                EditorGUILayout.PropertyField(RefreshOnEnable);
                EditorGUILayout.PropertyField(RefreshOnUpdate);
            }
            GUILayout.EndVertical();

            if (GUI.changed)
            {
                instance.EditorOnSplineUpdated(instance.SplineReference); 
                serializedObject.ApplyModifiedProperties();
            }

            // update callback registration.. 
            if (instance.SplineReference != prevSplineReference)
            {
                if (prevSplineReference != null)
                {
                    prevSplineReference.onEditorSplineUpdated -= instance.EditorOnSplineUpdated;
                }

                if (instance.SplineReference != null)
                {
                    instance.SplineReference.onEditorSplineUpdated += instance.EditorOnSplineUpdated;
                }
            }

            // draw default inspector.. 
            // base.OnInspectorGUI();
        }

        [MenuItem("GameObject/CorgiSpline/Spline Prefab Repeater", priority = 10)]
        public static void MenuItemCreateMeshBuilder_Cubey()
        {
            var editorConfig = SplineEditorConfig.FindConfig();
            var newGameobject = new GameObject("NewSplinePrefabRepeater");

            var spline = newGameobject.AddComponent<Spline>();
                spline.SetSplineSpace(Space.Self, false);

            var meshBuilder = newGameobject.AddComponent<PrefabRepeater>();
                meshBuilder.SplineReference = spline;

            // toggle to force register 
            meshBuilder.enabled = false;
            meshBuilder.enabled = true;

            if (Selection.activeTransform != null)
            {
                newGameobject.transform.SetParent(Selection.activeTransform);
            }
        }
    }
}
#endif
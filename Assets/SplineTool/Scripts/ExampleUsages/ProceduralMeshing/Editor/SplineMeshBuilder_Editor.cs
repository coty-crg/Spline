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
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder) target;

            if (instance.SplineReference == null)
            {
                EditorGUILayout.HelpBox("SplineReference is null. Please assign a spline to sample from!", MessageType.Error);
            }

            if(instance.GetPreviousMeshingDurationMs() > 0f)
            {
                EditorGUILayout.HelpBox($"Meshing took {instance.GetPreviousMeshingDurationMs()}ms to complete.", MessageType.Info);
            }
        }
    }
}
#endif
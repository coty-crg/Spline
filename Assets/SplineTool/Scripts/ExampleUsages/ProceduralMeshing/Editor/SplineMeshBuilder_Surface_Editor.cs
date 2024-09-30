#if UNITY_EDITOR
namespace CorgiSpline
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;

    [CustomEditor(typeof(SplineMeshBuilder_Surface))]
    public class SplineMeshBuilder_Surface_Editor : SplineMeshBuilder_Editor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder_Surface) target;

            if (GUI.changed)
            {
                instance.EditorOnSplineUpdated(instance.SplineReference);
                serializedObject.ApplyModifiedProperties();
            }
        }

        [MenuItem("GameObject/CorgiSpline/Meshes/Surface", priority = 10)]
        public static void MenuItemCreateMeshBuilder_Tubey()
        {
            var editorConfig = SplineEditorConfig.FindConfig();
            var newGameobject = new GameObject("NewSplineMesh_Surface");
                newGameobject.AddComponent<MeshFilter>();

            var spline = newGameobject.AddComponent<Spline>();
                spline.SetSplineSpace(Space.Self, false);
                spline.EditorAlwaysFacePointsForwardAndUp = true;

            var meshBuilder = newGameobject.AddComponent<SplineMeshBuilder_Surface>();
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

#if UNITY_EDITOR
            UnityEditor.Selection.SetActiveObjectWithContext(newGameobject, newGameobject);
#endif
        }
    }
}
#endif
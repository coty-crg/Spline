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
        protected SerializedProperty built_to_t;
        protected SerializedProperty quality;
        protected SerializedProperty vertexOffset;
        protected SerializedProperty scaleMult;
        protected SerializedProperty rotationEulorOffset;
        protected SerializedProperty uv_tile_scale;
        protected SerializedProperty cover_ends_with_quads;
        protected SerializedProperty use_splinepoint_rotations;
        protected SerializedProperty use_splinepoint_scale;
        protected SerializedProperty _serializedMesh;
        protected SerializedProperty MeshNormalsMode;
        protected SerializedProperty UVsMode;
        protected SerializedProperty unity_generate_lightmap_uvs;
        protected SerializedProperty unity_lightmap_params;

        protected virtual void OnEnable()
        {
            SplineReference             = serializedObject.FindProperty("SplineReference");
            RebuildEveryFrame           = serializedObject.FindProperty("RebuildEveryFrame");
            RebuildOnEnable             = serializedObject.FindProperty("RebuildOnEnable");
            AllowAsyncRebuild           = serializedObject.FindProperty("AllowAsyncRebuild");
            built_to_t                  = serializedObject.FindProperty("built_to_t");
            quality                     = serializedObject.FindProperty("quality");
            vertexOffset                = serializedObject.FindProperty("vertexOffset");
            scaleMult                   = serializedObject.FindProperty("scaleMult");
            rotationEulorOffset         = serializedObject.FindProperty("rotationEulorOffset");
            uv_tile_scale               = serializedObject.FindProperty("uv_tile_scale");
            cover_ends_with_quads       = serializedObject.FindProperty("cover_ends_with_quads");
            use_splinepoint_rotations   = serializedObject.FindProperty("use_splinepoint_rotations");
            use_splinepoint_scale       = serializedObject.FindProperty("use_splinepoint_scale");
            _serializedMesh             = serializedObject.FindProperty("_serializedMesh");
            MeshNormalsMode             = serializedObject.FindProperty("MeshNormalsMode");
            UVsMode                     = serializedObject.FindProperty("UVsMode");
            unity_generate_lightmap_uvs = serializedObject.FindProperty("unity_generate_lightmap_uvs");
            unity_lightmap_params       = serializedObject.FindProperty("unity_lightmap_params");
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
            else
            {
                EditorGUILayout.HelpBox($"Awaiting remesh.", MessageType.Info);
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
                            instance.SplineReference = Undo.AddComponent<Spline>(instance.gameObject);
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
                    // EditorGUILayout.PropertyField(SerializeMesh);

                    if(instance._serializedMesh != null)
                    {
                        EditorGUI.BeginDisabledGroup(true); 
                        EditorGUILayout.PropertyField(_serializedMesh);
                        EditorGUI.EndDisabledGroup(); 

                        if(GUILayout.Button("Unlink Serialized Mesh"))
                        {
                            Undo.RecordObject(instance, "unlink"); 
                            instance._serializedMesh = null;
                            instance.ResetMesh(); 
                        }
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(RebuildEveryFrame);
                        EditorGUILayout.PropertyField(RebuildOnEnable);
                        EditorGUILayout.PropertyField(AllowAsyncRebuild);

                        EditorGUILayout.Space();
                        if(GUILayout.Button("Serialize Mesh"))
                        {
                            var filename = EditorUtility.SaveFilePanel("Save Mesh Asset", Application.dataPath, $"{instance.gameObject.name}", "asset");
                            if(!string.IsNullOrEmpty(filename))
                            {
                                filename = filename.Replace(Application.dataPath + "/", "Assets/");

                                Undo.RecordObject(instance, "serialized mesh"); 

                                var existingAsset = AssetDatabase.LoadAssetAtPath<Mesh>(filename);
                                if(existingAsset != null)
                                {
                                    instance.ConfigureSerializedMesh(existingAsset);
                                }
                                else
                                {
                                    var mesh = instance.GetMesh();

                                    AssetDatabase.CreateAsset(mesh, filename);
                                    AssetDatabase.SaveAssets();

                                    var meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(filename);
                                    instance.ConfigureSerializedMesh(meshAsset);
                                }
                            }
                        }
                    }

                    EditorGUILayout.Space();

                    var existingMeshCollider = instance.GetComponent<MeshCollider>();
                    if(existingMeshCollider == null && GUILayout.Button("Generate MeshCollider"))
                    {
                        var meshCollider = Undo.AddComponent<MeshCollider>(instance.gameObject);
                            meshCollider.sharedMesh = instance.GetMesh(); 
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Quality Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(quality);
                    EditorGUILayout.PropertyField(unity_generate_lightmap_uvs);

                    if(instance.unity_generate_lightmap_uvs)
                    {
                        EditorGUILayout.PropertyField(unity_lightmap_params);
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("GroupBox");
                {
                    var repeatingMeshBuilder = instance as SplineMeshBuilder_RepeatingMesh;
                    var supportsEndCapQuads = repeatingMeshBuilder == null;
                    var supportsMeshNormalsMode = repeatingMeshBuilder == null;
                    var supportsUvSettings = repeatingMeshBuilder == null || !repeatingMeshBuilder.UseRepeatingMeshUVs;
                    var supportsSplineSamplingSettings = repeatingMeshBuilder == null;

                    EditorGUILayout.LabelField("Visual Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(built_to_t);
                    EditorGUILayout.PropertyField(vertexOffset);
                    EditorGUILayout.PropertyField(rotationEulorOffset);
                    EditorGUILayout.PropertyField(scaleMult);

                    if(supportsEndCapQuads)
                    {
                        EditorGUILayout.PropertyField(cover_ends_with_quads);
                    }

                    if (supportsMeshNormalsMode)
                    {
                        EditorGUILayout.PropertyField(MeshNormalsMode);
                    }

                    if(supportsUvSettings)
                    {
                        EditorGUILayout.PropertyField(UVsMode);

                        if(instance.UVsMode == SplineMeshBuilder.MeshBuilderUVs.Tile)
                        {
                            EditorGUILayout.PropertyField(uv_tile_scale);
                        }
                    }

                    if(supportsSplineSamplingSettings)
                    {
                        EditorGUILayout.PropertyField(use_splinepoint_rotations);
                        EditorGUILayout.PropertyField(use_splinepoint_scale);
                    }
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

        [MenuItem("GameObject/CorgiSpline/Spline Mesh (cube)", priority = 10)]
        public static void MenuItemCreateMeshBuilder_Cubey()
        {
            var editorConfig = SplineEditorConfig.FindConfig();
            var newGameobject = new GameObject("NewSplineMesh_Cube");

            var spline = newGameobject.AddComponent<Spline>();
                spline.SetSplineSpace(Space.Self, false);

            var meshBuilder = newGameobject.AddComponent<SplineMeshBuilder>();
                meshBuilder.SplineReference = spline;

            var newMeshRenderer = newGameobject.AddComponent<MeshRenderer>();
                newMeshRenderer.sharedMaterial = editorConfig.defaultMaterialForRenderers;

            if (Selection.activeTransform != null)
            {
                newGameobject.transform.SetParent(Selection.activeTransform);
            }
        }
    }
}
#endif
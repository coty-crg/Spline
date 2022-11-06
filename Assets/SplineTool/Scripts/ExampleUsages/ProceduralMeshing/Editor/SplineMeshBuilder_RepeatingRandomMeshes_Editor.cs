#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CorgiSpline
{
    [CustomEditor(typeof(SplineMeshBuilder_RepeatingRandomMeshes))]
    public class SplineMeshBuilder_RepeatingRandomMeshes_Editor : SplineMeshBuilder_Editor
    {
        protected SerializedProperty RepeatableMeshes;
        protected SerializedProperty UseRepeatingMeshUVs;
        protected SerializedProperty repeatableMeshSeed;
        private List<Vector3> _vertexCache = new List<Vector3>();
        private List<int> _vertexMinZCache = new List<int>();
        private List<int> _vertexMaxZCache = new List<int>();

        private Mesh _prevMeshVerified;
        private bool _passed_MinMaxMatch;
        private bool _passed_XYMatch;

        protected override void OnEnable()
        {
            base.OnEnable();

            RepeatableMeshes        = serializedObject.FindProperty("RepeatableMeshes");
            UseRepeatingMeshUVs     = serializedObject.FindProperty("UseRepeatingMeshUVs");
            repeatableMeshSeed      = serializedObject.FindProperty("repeatableMeshSeed");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var instance = (SplineMeshBuilder_RepeatingRandomMeshes) target;

            GUILayout.BeginVertical("GroupBox");
            {
                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Repeating Meshes", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(RepeatableMeshes);

                    if (instance.RepeatableMeshes == null || instance.RepeatableMeshes.Count == 0)
                    {
                        EditorGUILayout.HelpBox("RepeatableMesh is null. Please assign at least one mesh to repeat!", MessageType.Error);
                    }
                    else
                    {
                        EditorGUILayout.Space(); 
                        EditorGUILayout.BeginVertical();
                        {
                            for(var m = 0; m < instance.RepeatableMeshes.Count; ++m)
                            {
                                var randomMesh = instance.RepeatableMeshes[m];

                                EditorGUILayout.BeginHorizontal();
                                {

                                    // draw preview 
                                    var assetPreview = AssetPreview.GetAssetPreview(randomMesh);
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
                                        var has_normals = randomMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal);
                                        var has_tangents = randomMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
                                        var has_uv0 = randomMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                                        var has_color = randomMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);

                                        // run various tests on the mesh 
                                        VerifyMeshTests(randomMesh);

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

                                        if (!_passed_MinMaxMatch)
                                        {
                                            EditorGUILayout.HelpBox($"The number of vertices at the z start and the number of vertices at z end do NOT match! " +
                                                $"start: {_vertexMinZCache.Count}, end: {_vertexMaxZCache.Count}", MessageType.Error);
                                        }

                                        if (!_passed_XYMatch)
                                        {
                                            EditorGUILayout.HelpBox($"The vertices at the start of the mesh do not match the vertices at the end of the mesh! ", MessageType.Warning);
                                        }
                                    }
                                    EditorGUILayout.EndVertical();
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space();
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("Repeating Mesh Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(UseRepeatingMeshUVs);

                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.PropertyField(repeatableMeshSeed);
                        EditorGUI.EndDisabledGroup();
                        if (GUILayout.Button("new seed"))
                        {
                            Undo.RecordObject(instance, "new seed");
                            instance.repeatableMeshSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                            EditorUtility.SetDirty(instance);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            if (GUI.changed)
            {
                instance.EditorOnSplineUpdated(instance.SplineReference);
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void VerifyMeshTests(Mesh mesh)
        {
            // repetable mesh vertices match check 
            if (_prevMeshVerified == null || _prevMeshVerified != mesh)
            {
                _prevMeshVerified = mesh;

                _vertexCache.Clear();
                mesh.GetVertices(_vertexCache);

                var minZ = float.MaxValue;
                foreach (var vert in _vertexCache)
                {
                    if (vert.z < minZ)
                    {
                        minZ = vert.z;
                    }
                }

                var maxZ = float.MinValue;
                foreach (var vert in _vertexCache)
                {
                    if (vert.z > maxZ)
                    {
                        maxZ = vert.z;
                    }
                }

                _vertexMinZCache.Clear();
                for (var i = 0; i < _vertexCache.Count; ++i)
                {
                    var vert = _vertexCache[i];
                    if (Mathf.Abs(vert.z - minZ) < 0.0001f)
                    {
                        _vertexMinZCache.Add(i);
                    }
                }

                _vertexMaxZCache.Clear();
                for (var i = 0; i < _vertexCache.Count; ++i)
                {
                    var vert = _vertexCache[i];
                    if (Mathf.Abs(vert.z - maxZ) < 0.0001f)
                    {
                        _vertexMaxZCache.Add(i);
                    }
                }

                _passed_MinMaxMatch = _vertexMinZCache.Count == _vertexMaxZCache.Count;
                _passed_XYMatch = true; 

                if (_passed_MinMaxMatch)
                {
                    // sort vertice indices by angle around the plane of their start and end caps 
                    // note: this assumes the z values match perfectly 
                    _vertexMinZCache.Sort((ai, bi) => Mathf.Atan2(_vertexCache[ai].y, _vertexCache[ai].x).CompareTo(Mathf.Atan2(_vertexCache[bi].y, _vertexCache[bi].x)));
                    _vertexMaxZCache.Sort((ai, bi) => Mathf.Atan2(_vertexCache[ai].y, _vertexCache[ai].x).CompareTo(Mathf.Atan2(_vertexCache[bi].y, _vertexCache[bi].x)));
                    
                    for (var i = 0; i < _vertexMinZCache.Count; ++i)
                    {
                        var min_z_i = _vertexMinZCache[i];
                        var max_z_i = _vertexMaxZCache[i];


                        var vert_min_z = _vertexCache[min_z_i];
                        var vert_max_z = _vertexCache[max_z_i];

                        if(Mathf.Abs(vert_min_z.x - vert_max_z.x) > 0.0001f || Mathf.Abs(vert_min_z.y - vert_max_z.y) > 0.0001f)
                        {
                            _passed_XYMatch = false; 
                            break;
                        }
                    }
                }
            }
        }

        [MenuItem("GameObject/CorgiSpline/Meshes/Spline Mesh (repeating random meshes)", priority = 10)]
        public static void MenuItemCreateMeshBuilder_RepeatingRandomMeshes()
        {
            var editorConfig = SplineEditorConfig.FindConfig();

            var newGameobject = new GameObject("NewSplineMesh_RepeatingRandomMeshes");
                newGameobject.AddComponent<MeshFilter>();

            var spline = newGameobject.AddComponent<Spline>();
                spline.SetSplineSpace(Space.Self, false);
                spline.EditorAlwaysFacePointsForwardAndUp = true;

            var meshBuilder = newGameobject.AddComponent<SplineMeshBuilder_RepeatingRandomMeshes>();
                meshBuilder.SplineReference = spline;
                meshBuilder.UseRepeatingMeshUVs = true;
                meshBuilder.use_splinepoint_rotations = true;
                meshBuilder.use_splinepoint_scale = true;
            
            var newMeshRenderer = newGameobject.AddComponent<MeshRenderer>();
                newMeshRenderer.sharedMaterial = editorConfig.defaultMaterialForRenderers;

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
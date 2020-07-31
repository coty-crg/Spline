
#if UNITY_EDITOR && DREAMTECK_SPLINES
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Text;
using System.Linq;
using UnityEditor.SceneManagement;
using Dreamteck.Splines;

public class DreamtechToCorgiSplineConverter : EditorWindow
{

    [MenuItem("Window/CorgiSpline/Convert/Dreamtech Splines to CorgiSpline")]
    public static void Init()
    {
        var window = GetWindow<DreamtechToCorgiSplineConverter>();
        window.Show();
    }


    private List<MonoBehaviour> FoundScripts = new List<MonoBehaviour>();
    private List<Dreamteck.Splines.SplineComputer> FoundSplines = new List<Dreamteck.Splines.SplineComputer>();
    private List<Dreamteck.Splines.SplineComputer> SkippedSplines = new List<Dreamteck.Splines.SplineComputer>();
    private bool hasScanned = false;
    
    // <dreamteck, corgispline>
    private Dictionary<GlobalObjectId, GlobalObjectId> ReplaceGuids = new Dictionary<GlobalObjectId, GlobalObjectId>(); 

    private void OnGUI()
    {
     
        if(GUILayout.Button("Begin Scan (Scene)"))
        {

            hasScanned = true;

            FoundSplines.Clear();
            SkippedSplines.Clear();

            ScanSplines_CurrentScene();
        }

        if (GUILayout.Button("Begin Scan (Prefabs)"))
        {
            hasScanned = true;

            FoundSplines.Clear();
            SkippedSplines.Clear();

            ScanSplines_Prefabs();
        }
        
        if (GUILayout.Button("Convert Splines"))
        {
            hasScanned = true;

            FoundSplines.Clear();
            SkippedSplines.Clear();
            
            ScanSplines_CurrentScene();
            ScanSplines_Prefabs();

            ConvertFoundSplines();
            ConvertReferences();
            ScanScripts(); 
        }

        if(GUILayout.Button("Destroy splines"))
        {
            DestroySplineComputers();
            FoundSplines.Clear(); 
        }

        if(hasScanned)
        {
            if(FoundSplines.Count == 0)
            {
                GUILayout.Label("No splines found.");
            }
            else
            {
                GUILayout.Label($"Found {FoundSplines.Count} splines to convert.");
                for (var i = 0; i < FoundSplines.Count; ++i)
                {
                    FoundSplines[i] = (Dreamteck.Splines.SplineComputer)EditorGUILayout.ObjectField(FoundSplines[i], typeof(Dreamteck.Splines.SplineComputer), true); 
                }
            }

            if(SkippedSplines.Count == 0)
            {
                // nothing 
            }
            else
            {
                GUILayout.Label($"Skipped {SkippedSplines.Count} splines. They are within prefabs, and you scanned for scene splines.");
                for (var i = 0; i < SkippedSplines.Count; ++i)
                {
                    SkippedSplines[i] = (Dreamteck.Splines.SplineComputer)EditorGUILayout.ObjectField(SkippedSplines[i], typeof(Dreamteck.Splines.SplineComputer), true);
                }
            }
        }
        else
        {
            GUILayout.Label("Awaiting scan.");
        }

    }

    public void DestroySplineComputers()
    {
        for(var i = 0; i < FoundSplines.Count; ++i)
        {
            var splineComputer = FoundSplines[i];
            DestroyImmediate(splineComputer, true); 
        }
    }

    public void ScanScripts()
    {
        hasScanned = true; 
        FoundScripts.Clear();


        var all_scripts = System.IO.Directory.GetFiles(Application.dataPath, "*.cs", System.IO.SearchOption.AllDirectories); 
        
        for(var i = 0; i < all_scripts.Length; ++i)
        {
            var filename_script = all_scripts[i];
            var text_script = System.IO.File.ReadAllText(filename_script);

            var original_hash = text_script.GetHashCode();

            // watch out 
            if (text_script.Contains("namespace Dreamteck")) continue;
            if (text_script.Contains("DreamtechToCorgiSplineConverter")) continue;

            text_script = text_script.Replace("using Dreamteck.Splines;", "using CorgiSpline;");
            text_script = text_script.Replace("using Dreamteck;", "using CorgiSpline;");
            text_script = text_script.Replace("Dreamteck.Splines.SplineComputer", "CorgiSpline.Spline");
            text_script = text_script.Replace("Splines.SplineComputer", "CorgiSpline.Spline");
            text_script = text_script.Replace("SplineComputer", "Spline");
            text_script = text_script.Replace("Dreamteck", "CorgiSpline");
            
            var new_hash = text_script.GetHashCode();

            if(original_hash != new_hash)
            {
                System.IO.File.WriteAllText(filename_script, text_script);
                Debug.Log($"Updated {filename_script}");
            }
        }
    }

    public void ConvertReferences()
    {
        var all_filenames = System.IO.Directory.GetFiles(Application.dataPath, "*", System.IO.SearchOption.AllDirectories);

        for (var i = 0; i < all_filenames.Length; ++i)
        {
            var filename = all_filenames[i];
            if (!System.IO.File.Exists(filename)) continue;

            var text_meta_lines = System.IO.File.ReadAllLines(filename);

            var any_modified = false; 

            for (var line_meta = 0; line_meta < text_meta_lines.Length; ++line_meta)
            {
                var text_meta_line = text_meta_lines[line_meta];

                if (text_meta_line.Contains("component:"))
                    continue;

                if (text_meta_line.Contains("---"))
                    continue;

                var original_hash = text_meta_line.GetHashCode(); 

                foreach (var guidPair in ReplaceGuids)
                {
                    var dreamteckGuidPair = guidPair.Key;
                    var corgisplineGuidPair = guidPair.Value;
                    
                    text_meta_line = text_meta_line.Replace(dreamteckGuidPair.assetGUID.ToString(), corgisplineGuidPair.assetGUID.ToString());
                    text_meta_line = text_meta_line.Replace(dreamteckGuidPair.targetPrefabId.ToString(), corgisplineGuidPair.targetPrefabId.ToString());
                    text_meta_line = text_meta_line.Replace(dreamteckGuidPair.targetObjectId.ToString(), corgisplineGuidPair.targetObjectId.ToString());
                }

                var modified_hash = text_meta_line.GetHashCode();

                if(original_hash != modified_hash)
                {
                    any_modified = true;
                    text_meta_lines[line_meta] = text_meta_line;
                }

            }

            if(any_modified)
            {
                System.IO.File.WriteAllLines(filename, text_meta_lines);
            }
        }
        
    }

    private void ConvertFoundSplines()
    {
        ReplaceGuids.Clear();
        
        for (var i = 0; i < FoundSplines.Count; ++i)
        {
            var splineComputer = FoundSplines[i];
            var go = splineComputer.gameObject;

            Undo.RecordObject(go, "Convert Spline");

            var corgiSpline = go.AddComponent<CorgiSpline.Spline>();
            
            var splineType = splineComputer.type;
            
            switch(splineType)
            {
                case Dreamteck.Splines.Spline.Type.Linear:
                    corgiSpline.SetSplineMode(CorgiSpline.SplineMode.Linear);
                    break;
                case Dreamteck.Splines.Spline.Type.Bezier:
                    corgiSpline.SetSplineMode(CorgiSpline.SplineMode.Bezier);
                    break;
                case Dreamteck.Splines.Spline.Type.BSpline:
                    corgiSpline.SetSplineMode(CorgiSpline.SplineMode.BSpline);
                    break;
                case Dreamteck.Splines.Spline.Type.CatmullRom:
                    // catmull rom is not supported in corgi spline, so we're just using their own tools to convert it to a bezier spline
                    // then, convert it to our bezier spline setup 
                    splineComputer.CatToBezierTangents();
                    splineComputer.type = Dreamteck.Splines.Spline.Type.Bezier;
                    corgiSpline.SetSplineMode(CorgiSpline.SplineMode.Bezier);
                    break;
            }

            var splineSpace = splineComputer.space;
            switch(splineSpace)
            {
                case Dreamteck.Splines.SplineComputer.Space.Local:
                    corgiSpline.SetSplineSpace(Space.Self, false);
                    break;
                case Dreamteck.Splines.SplineComputer.Space.World:
                    corgiSpline.SetSplineSpace(Space.World, false);
                    break; 
            }

            var all_points = splineComputer.GetPoints(splineComputer.space);
            for(var p = 0; p < all_points.Length; ++p)
            {
                var spline_point = all_points[p];

                var tangent = splineComputer.GetPointTangent(p, splineComputer.space);
                var normal = spline_point.normal;
                var rotation = Quaternion.LookRotation(tangent, normal);
                var position = spline_point.position;

                corgiSpline.AppendPoint(position, rotation, Vector3.one * spline_point.size);
            }

            // if bezier type, we have to fix our handles (dreamtech calls them tangents)
            if (corgiSpline.GetSplineMode() == CorgiSpline.SplineMode.Bezier)
            {
                for (var p = 0; p < all_points.Length; ++p)
                {
                    var spline_point = all_points[p];
                    var position = spline_point.position;

                    var lastAnchorIndex = corgiSpline.GetPointIgnoreHandles(p);
                    CorgiSpline.SplinePoint.GetHandleIndexes(corgiSpline.GetSplineMode(), corgiSpline.GetSplineClosed(), corgiSpline.Points.Length, lastAnchorIndex,
                        out int handleIndex0, out int handleIndex1);

                    var originalHandle0 = spline_point.tangent;
                    var originalHandle1 = spline_point.tangent2;

                    if(handleIndex0 >= 0 && handleIndex0 < corgiSpline.Points.Length)
                        corgiSpline.Points[handleIndex0].position = originalHandle0;

                    if (handleIndex1 >= 0 && handleIndex1 < corgiSpline.Points.Length)
                        corgiSpline.Points[handleIndex1].position = originalHandle1;
                }
            }


            if (splineComputer.isClosed)
            {
                corgiSpline.SetSplineClosed(true);
            }

            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            var assetPath = AssetDatabase.GetAssetPath(go);
            if(!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.ImportAsset(assetPath);
                Debug.Log($"Reimported {go.name}");
            }

            var dreamteckSplineIdPair = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(splineComputer);
            var corgiSplineIdPair = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(corgiSpline);
            
            ReplaceGuids.Add(dreamteckSplineIdPair, corgiSplineIdPair);

            Debug.Log($"{go.name}'s ({go.GetType().Name}): " +
                $"{dreamteckSplineIdPair.assetGUID} {dreamteckSplineIdPair.targetObjectId} {dreamteckSplineIdPair.targetPrefabId} {dreamteckSplineIdPair.identifierType} " +
                $"-> {corgiSplineIdPair.assetGUID} {corgiSplineIdPair.targetObjectId} {corgiSplineIdPair.targetPrefabId} {corgiSplineIdPair.identifierType} ");
        }
    }

    private void ScanSplines_CurrentScene()
    {

        var splineComputersInScene = FindObjectsOfType<Dreamteck.Splines.SplineComputer>();
        for(var i = 0; i < splineComputersInScene.Length; ++i)
        {
            var splineComputer = splineComputersInScene[i];
            var go = splineComputer.gameObject;
            if(PrefabUtility.IsPartOfAnyPrefab(go))
            {
                SkippedSplines.Add(splineComputer);
                continue;
            }

            FoundSplines.Add(splineComputer); 
        }
    }

    private void ScanSplines_Prefabs()
    {
        var all_filenames = System.IO.Directory.GetFiles(Application.dataPath, "*", System.IO.SearchOption.AllDirectories);

        for(var i = 0; i < all_filenames.Length; ++i)
        {
            var filename = all_filenames[i];
            var assetPath = filename.Replace($"{Application.dataPath}", "Assets");

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null) continue;

            var splineComputers = go.GetComponentsInChildren<SplineComputer>();
            if (splineComputers == null || splineComputers.Length == 0) continue;

            for(var s = 0; s < splineComputers.Length; ++s)
            {
                var splineComputer = splineComputers[s];
                FoundSplines.Add(splineComputer);
            }
        }
    }
}
#endif

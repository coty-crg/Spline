
#if UNITY_EDITOR && DREAMTECK_SPLINES
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class DreamtechToCorgiSplineConverter : EditorWindow
{

    [MenuItem("Window/CorgiSpline/Convert/Dreamtech Splines to CorgiSpline")]
    public static void Init()
    {
        var window = GetWindow<DreamtechToCorgiSplineConverter>();
        window.Show();
    }


    private List<Dreamteck.Splines.SplineComputer> FoundSplines = new List<Dreamteck.Splines.SplineComputer>();
    private List<Dreamteck.Splines.SplineComputer> SkippedSplines = new List<Dreamteck.Splines.SplineComputer>();
    private bool hasScanned = false;

    private void OnGUI()
    {
     
        if(GUILayout.Button("Begin Scan (Scene)"))
        {
            ScanSplines_CurrentScene();
        }

        if (GUILayout.Button("Begin Scan (Prefabs)"))
        {
            ScanSplines_Prefabs();
        }

        if (GUILayout.Button("Convert Splines"))
        {
            ConvertFoundSplines(); 
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

    private void ConvertFoundSplines()
    {

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
            
            // clean up 
            DestroyImmediate(splineComputer, true); 
            
            Debug.Log($"Converted {go.name}'s SplineComputer.", go); 
            EditorUtility.SetDirty(go);
        }
    }

    private void ScanSplines_CurrentScene()
    {
        hasScanned = true;

        FoundSplines.Clear();
        SkippedSplines.Clear();

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

        hasScanned = true;

        FoundSplines.Clear();
        SkippedSplines.Clear();

        var all_splines = Resources.FindObjectsOfTypeAll<Dreamteck.Splines.SplineComputer>();

        for(var i = 0; i < all_splines.Length; ++i)
        {
            var splineComputer = all_splines[i];
            var go = splineComputer.gameObject;
            
            if (!PrefabUtility.IsPartOfPrefabThatCanBeAppliedTo(go) || !PrefabUtility.IsPartOfPrefabAsset(go))
            {
                continue;
            }

            FoundSplines.Add(splineComputer);
        }
    }
}
#endif
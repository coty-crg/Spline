
#if UNITY_EDITOR
namespace CorgiSpline
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;

    /// <summary>
    /// Editor only references for editor only tools, for the spline systems. 
    /// </summary>
    public class SplineEditorConfig : ScriptableObject
    {
        public Material defaultMaterialForRenderers;

        public static SplineEditorConfig FindConfig()
        {
            var guids = AssetDatabase.FindAssets("t:SplineEditorConfig");
            foreach(var guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;

                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                var result = AssetDatabase.LoadAssetAtPath<SplineEditorConfig>(assetPath);
                if (result == null) continue;

                return result; 
            }

            var newEditorConfig = SplineEditorConfig.CreateInstance<SplineEditorConfig>();

            var newAssetPath = "Assets/SplineEditorConfig.asset";
            AssetDatabase.CreateAsset(newEditorConfig, newAssetPath);
            AssetDatabase.SaveAssets();
            var newAsset = AssetDatabase.LoadAssetAtPath<SplineEditorConfig>(newAssetPath);

            Debug.Log("[CorgiSpline] SplineEditorConfig was not found, so one has been created.", newAsset);

            return newAsset;
        }
    }
}
#endif
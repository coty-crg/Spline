
#if UNITY_EDITOR
/// source: https://gist.github.com/MattRix/9205bc62d558fef98045
using UnityEngine;
using UnityEditor;
using System.Collections;
using System;
using System.Linq;
using System.Reflection;

namespace CorgiSpline
{
    [InitializeOnLoad]
    public class RXLookingGlass
    {
        public static Type type_HandleUtility;
        protected static MethodInfo meth_IntersectRayMesh;

        static RXLookingGlass()
        {
            var editorTypes = typeof(Editor).Assembly.GetTypes();

            type_HandleUtility = editorTypes.FirstOrDefault(t => t.Name == "HandleUtility");
            meth_IntersectRayMesh = type_HandleUtility.GetMethod("IntersectRayMesh", (BindingFlags.Static | BindingFlags.NonPublic));
        }

        public static bool IntersectRayMesh(Ray ray, MeshFilter meshFilter, out RaycastHit hit)
        {
            return IntersectRayMesh(ray, meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix, out hit);
        }

        public static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            var parameters = new object[] { ray, mesh, matrix, null };
            bool result = (bool)meth_IntersectRayMesh.Invoke(null, parameters);
            hit = (RaycastHit)parameters[3];
            return result;
        }

        public static bool IntersectRayGameObject(Ray ray, GameObject go, out RaycastHit hit)
        {
            var filters = go.GetComponents<MeshFilter>();
            foreach (var filter in filters)
            {
                if (IntersectRayMesh(ray, filter, out hit))
                {
                    return true;
                }
            }

            var terrainColliders = go.GetComponentsInChildren<TerrainCollider>();
            foreach (var terrianCollider in terrainColliders)
            {
                if(terrianCollider.Raycast(ray, out hit, 256f))
                {
                    return true; 
                }
            }

            hit = new RaycastHit();
            return false;
        }
    }
}

#endif
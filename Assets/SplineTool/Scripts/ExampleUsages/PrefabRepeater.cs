using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CorgiSpline
{
    [ExecuteAlways]
    public class PrefabRepeater : MonoBehaviour
    {
        [Tooltip("Spline to use for prefab repeating.")] 
        public Spline SplineReference; 

        [Tooltip("Prefab to repeat along the spline.")] 
        public GameObject PrefabToRepeat;

        [Tooltip("Number of times to repeat this prefab along the spline.")] [Range(0, 256)] 
        public int RepeatCount = 16;

        [Tooltip("For animating this effect.")] [Range(0f, 1f)] 
        public float RepeatPercentage = 1f;

        [Tooltip("OnEnable() will spawn prefabs along the spline.")] 
        public bool RefreshOnEnable = true;

        [Tooltip("OnUpdate() will spawn prefabs along the spline, destroying any existing ones.")] 
        public bool RefreshOnUpdate;

        [Tooltip("Position offset for the prefab from the spline.")] 
        public Vector3 PositionOffset;

        [Tooltip("Scale offset for the prefab from the spline.")]
        public Vector3 ScaleOffset = new Vector3(1, 1, 1);

        [Tooltip("Rotation offset, in eulor angles, for the prefab from the spline.")] 
        public Vector3 RotationEulorOffset;

        [Tooltip("Randomized positional offset range. (0,0,0) means no randomness.")] 
        public Vector3 RandomizedOffsetRange;

        [Tooltip("Randomized scale range. (0,0,0) means no randomness.")]
        public Vector3 RandomizedScaleRange;

        [Tooltip("Randomized rotation range in eulor angles. (0,0,0) means no randomness.")]
        public Vector3 RandomizedRotationEulorRange;


#if UNITY_EDITOR
        public void EditorOnSplineUpdated(Spline spline)
        {
            if (spline != SplineReference)
            {
                return;
            }

            Refresh(); 
        }

        public void EditorOnUndoRedo()
        {
            EditorOnSplineUpdated(SplineReference); 
        }
#endif

        private void RuntimeOnSplineDisabled(Spline spline)
        {
            // wrap up any ongoing jobs? 
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Refresh();
            }

            if (SplineReference != null)
            {
                SplineReference.onEditorSplineUpdated += EditorOnSplineUpdated;
            }


            UnityEditor.Undo.undoRedoPerformed += EditorOnUndoRedo;
#endif

            if (SplineReference != null)
            {
                SplineReference.onRuntimeSplineDisabled += RuntimeOnSplineDisabled;
            }

            if (RefreshOnEnable)
            {
                Refresh();   
            }
        }

        private void OnDisable()
        {
            if (SplineReference != null)
            {
                SplineReference.onRuntimeSplineDisabled -= RuntimeOnSplineDisabled;
            }

#if UNITY_EDITOR
            if (SplineReference != null)
            {
                SplineReference.onEditorSplineUpdated -= EditorOnSplineUpdated;
            }
            

            UnityEditor.Undo.undoRedoPerformed -= EditorOnUndoRedo;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)
            {
                return;
            }
#endif

            if (RefreshOnUpdate)
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            if (SplineReference == null || PrefabToRepeat == null)
            {
                return; 
            }

            ClearPrefabs();

            var parent = transform;

            var random = new System.Random(transform.position.GetHashCode()); 

            for(var s = 0; s < RepeatCount; ++s)
            {
                var t = (float) s / RepeatCount;
                if (t > RepeatPercentage) break;

                var splinePoint = SplineReference.GetPoint(t);

                var forward = splinePoint.rotation * Vector3.forward;
                var right = splinePoint.rotation * Vector3.right;
                var up = splinePoint.rotation * Vector3.up;
                var offset = PositionOffset.x * right + PositionOffset.y * up + PositionOffset.z * forward;

                var randomValuePos_x = (float) (random.NextDouble() * 2.0d - 1.0d);
                var randomValuePos_y = (float) (random.NextDouble() * 2.0d - 1.0d);
                var randomValuePos_z = (float) (random.NextDouble() * 2.0d - 1.0d);
                var randomValueRot   = (float) (random.NextDouble() * 2.0d - 1.0d);
                var randomValueScale = (float) (random.NextDouble() * 2.0d - 1.0d);

                offset += randomValuePos_x * RandomizedOffsetRange.x * right + randomValuePos_y * RandomizedOffsetRange.y * up + randomValuePos_z * RandomizedOffsetRange.z * forward;


#if UNITY_EDITOR
                GameObject go; 
                if(Application.isPlaying)
                {
                    go = GameObject.Instantiate(PrefabToRepeat, parent);
                }
                else
                {
                    go = (GameObject) UnityEditor.PrefabUtility.InstantiatePrefab(PrefabToRepeat, parent);
                }
#else
                var go = GameObject.Instantiate(PrefabToRepeat, parent);
#endif
                var transform = go.transform;
                    transform.SetPositionAndRotation(splinePoint.position + offset, splinePoint.rotation * Quaternion.Euler(RotationEulorOffset) * Quaternion.Euler(RandomizedRotationEulorRange * randomValueRot));
                    transform.localScale = Vector3.Scale(splinePoint.scale, ScaleOffset + RandomizedScaleRange * randomValueScale);
            }
        }

        public void ClearPrefabs()
        {
            var parent = transform;
            var childCount = parent.childCount;
            
            var toDestroy = new List<GameObject>();

            for(var t = 0; t < childCount; ++t)
            {
                toDestroy.Add(parent.GetChild(t).gameObject); 
            }

            for (var t = 0; t < childCount; ++t)
            {
                var go = toDestroy[t];

#if UNITY_EDITOR
                if(Application.isPlaying)
                {
                    GameObject.Destroy(go); 
                }
                else
                {
                    GameObject.DestroyImmediate(go); 
                }
#else
                GameObject.Destroy(go); 
#endif
            }
        }
    }
}
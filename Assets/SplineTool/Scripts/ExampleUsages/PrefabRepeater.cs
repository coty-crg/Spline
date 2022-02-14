using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CorgiSpline
{
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

        [Tooltip("Rotation offset for the prefab from the spline.")] 
        public Vector3 RotationEulorOffset;

        private void OnEnable()
        {
            if(RefreshOnEnable)
            {
                Refresh();   
            }
        }

        private void Update()
        {
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

            for(var s = 0; s < RepeatCount; ++s)
            {
                var t = (float) s / RepeatCount;
                if (t > RepeatPercentage) break;

                var splinePoint = SplineReference.GetPoint(t);

                var forward = splinePoint.rotation * Vector3.forward;
                var right = splinePoint.rotation * Vector3.right;
                var up = splinePoint.rotation * Vector3.up;
                var offset = PositionOffset.x * right + PositionOffset.y * up + PositionOffset.z * forward;

                var go = GameObject.Instantiate(PrefabToRepeat, parent);
                var transform = go.transform;
                    transform.SetPositionAndRotation(splinePoint.position + offset, splinePoint.rotation * Quaternion.Euler(RotationEulorOffset));
                    transform.localScale = Vector3.Scale(splinePoint.scale, ScaleOffset);
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
                GameObject.Destroy(go); 
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (SplineReference == null) return;

            Gizmos.color = new Color(1, 1, 1, 0.75f);

            for (var s = 0; s < RepeatCount; ++s)
            {
                var t = (float)s / RepeatCount;
                if (t > RepeatPercentage) break;

                var splinePoint = SplineReference.GetPoint(t);

                var forward = splinePoint.rotation * Vector3.forward;
                var right = splinePoint.rotation * Vector3.right;
                var up = splinePoint.rotation * Vector3.up;
                var offset = PositionOffset.x * right + PositionOffset.y * up + PositionOffset.z * forward;

                Gizmos.DrawSphere(splinePoint.position + offset, splinePoint.scale.magnitude * ScaleOffset.magnitude); 
            }
        }
    }
}
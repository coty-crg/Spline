using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;

namespace CorgiSpline
{

    [System.Serializable]
    public struct SplinePoint
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public SplinePoint(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public override bool Equals(object obj)
        {
            var otherPoint = (SplinePoint)obj;
            var matchPosition = (position - otherPoint.position).sqrMagnitude < 0.001f;
            var matchUp = (rotation.eulerAngles - otherPoint.rotation.eulerAngles).sqrMagnitude < 0.001f;
            var matchScale = (scale - otherPoint.scale).sqrMagnitude < 0.001f;
            return matchPosition && matchUp && matchScale;
        }

        public override int GetHashCode()
        {
            int hashCode = -1285106862;
            hashCode = hashCode * -1521134295 + position.GetHashCode();
            hashCode = hashCode * -1521134295 + rotation.GetHashCode();
            hashCode = hashCode * -1521134295 + scale.GetHashCode();
            return hashCode;
        }

        public static bool IsHandle(SplineMode mode, int index)
        {
            var isHandle = mode == SplineMode.Bezier && index % 3 != 0;
            return isHandle;
        }

        public static int GetAnchorIndex(SplineMode mode, int index)
        {
            if (mode != SplineMode.Bezier)
            {
                return index;
            }
            else
            {
                var index_mod = index % 3;

                if (index_mod == 1) return index - 1;
                else if (index_mod == 2) return index + 1;

                return index;
            }
        }

        public static void GetHandleIndexes(SplineMode mode, bool isClosed, int pointCount, int index, out int handleIndex0, out int handleIndex1)
        {
            if (mode != SplineMode.Bezier)
            {
                handleIndex0 = index;
                handleIndex1 = index;
            }
            else
            {
                var anchorIndex = GetAnchorIndex(mode, index);
                handleIndex0 = anchorIndex - 1;
                handleIndex1 = anchorIndex + 1;

                if (isClosed)
                {
                    if (handleIndex0 == -1) handleIndex0 = pointCount - 1;
                    if (handleIndex1 == pointCount) handleIndex1 = 1;
                }
            }
        }
    }

    public enum SplineMode
    {
        Linear,
        Bezier,
        BSpline,
    }

    [ExecuteInEditMode]
    [DefaultExecutionOrder(1)] // this is so we run the de-allocate in OnDisable after the other scripts run their OnDisable()s in the case of a shutdown 
    public class Spline : MonoBehaviour
    {
        /// <summary>
        /// Serialized Points. Used in-editor for serialization.
        /// </summary>
        public SplinePoint[] Points = new SplinePoint[0];

        /// <summary>
        /// Junction dependency. If this is not null this spline begins as a junction. 
        /// </summary>
        /// 
        [Tooltip("When set, this spline will begin at a junction with the defined spline.")]
        [SerializeField, HideInInspector] private Spline _junctionSplineBegin;
        [Tooltip("Where along the junctioned spline to begin.")]
        [SerializeField, HideInInspector, Range(0f, 1f)] private float _junctionBegin_t;
        [Tooltip("How far the first point or handle after the junction will be automatically moved to, to seemlessly blend across the junction.")]
        [SerializeField, HideInInspector, Range(-10f, 10f)] private float _junctionBeginTightness = 1.0f;

        [Tooltip("When set, this spline will end at a junction with the defined spline.")]
        [SerializeField, HideInInspector] private Spline _junctionSplineEnd;
        [Tooltip("Where along the junctioned spline to begin.")]
        [SerializeField, HideInInspector, Range(0f, 1f)] private float _junctionEnd_t;
        [Tooltip("How far the previous point or handle before the junction will be automatically moved to, to seemlessly blend across the junction.")]
        [SerializeField, HideInInspector, Range(-10f, 10f)] private float _junctionEndTightness = 1.0f;

        /// <summary>
        /// Used at runtime for burstable jobs. Does not automatically update, aside from on OnEnable. 
        /// </summary>
        [System.NonSerialized] public NativeArray<SplinePoint> NativePoints;
        [System.NonSerialized] public NativeArray<float> NativeDistanceCache;

        // settings
        [Tooltip("Only necessary if you care about using Splines in the Job System. Some of the example scripts use this.")] 
        public bool UpdateNativeArrayOnEnable = true;

        [SerializeField, HideInInspector] private SplineMode Mode = SplineMode.Linear;
        [SerializeField, HideInInspector] private Space SplineSpace = Space.World;
        [SerializeField, HideInInspector] private bool ClosedSpline;

        // editor only settings 
        public bool EditorDrawThickness;
        public bool EditorDrawRotations;
        public bool EditorAlwaysDraw;
        public bool EditorAlwaysFacePointsForwardAndUp;
        public float EditorGizmosScale = 1.0f;

        public delegate void RuntimeSplineDisabledEvent(Spline spline);

        [System.NonSerialized] private float[] _projectionDistanceCache;
        [System.NonSerialized] private float _projectionDistanceLength;

        /// <summary>
        /// Register to this if you are scheduling a job that requires access to this spline's NativeArrays. 
        /// If the spline is disabled, the NativeArray will be cleared, so you'll want to clean up any ongoing dependencies.
        /// </summary>
        [System.NonSerialized] public RuntimeSplineDisabledEvent onRuntimeSplineDisabled;

#if UNITY_EDITOR
        public delegate void EditorSplineUpdatedEvent(Spline spline);
        [System.NonSerialized] public EditorSplineUpdatedEvent onEditorSplineUpdated;

        public void SendEditorSplineUpdatedEvent()
        {
            UpdateNative();

            if(onEditorSplineUpdated != null)
            {
                onEditorSplineUpdated.Invoke(this); 
            }
        }

        public void EditorOnUndoRedoPerformed()
        {
            if(GetHasAnyJunction())
            {
                UpdateStartJunction();
                UpdateEndJunction();
                UpdateNative(); 
            }

            SendEditorSplineUpdatedEvent(); 
        }
#endif

        private void OnEnable()
        {
            if(UpdateNativeArrayOnEnable)
            {
                UpdateNative();
            }

#if UNITY_EDITOR
            UnityEditor.Undo.undoRedoPerformed += EditorOnUndoRedoPerformed;
#endif

        }

        private void OnDisable()
        {
            if(onRuntimeSplineDisabled != null)
            {
                onRuntimeSplineDisabled.Invoke(this);
            }

            DisposeNativePoints();
            DisposeNativeDistanceProjectionCache(); 

#if UNITY_EDITOR
            UnityEditor.Undo.undoRedoPerformed -= EditorOnUndoRedoPerformed;
#endif
        }

        public void DisposeNativePoints()
        {
            if (NativePoints.IsCreated)
            {
                NativePoints.Dispose();
            }

        }

        public void DisposeNativeDistanceProjectionCache()
        {
            if (NativeDistanceCache.IsCreated)
            {
                NativeDistanceCache.Dispose();
            }
        }

        public void UpdateNative()
        {
            var point_count = Points.Length;

            if (NativePoints.Length != point_count || !NativePoints.IsCreated)
            {
                DisposeNativePoints(); 
                NativePoints = new NativeArray<SplinePoint>(point_count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            for (var i = 0; i < point_count; ++i)
            {
                NativePoints[i] = Points[i];
            }

            if(_projectionDistanceCache != null)
            {
                var projectionCacheRes = _projectionDistanceCache.Length;

                if (!NativeDistanceCache.IsCreated || NativeDistanceCache.Length != projectionCacheRes)
                {
                    DisposeNativeDistanceProjectionCache(); 
                    NativeDistanceCache = new NativeArray<float>(projectionCacheRes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory); 
                }

                for(var i = 0; i < projectionCacheRes; ++i)
                {
                    NativeDistanceCache[i] = _projectionDistanceCache[i];
                }
            }
        }

        // api 

        /// <summary>
        /// Configures this spline with a dependency on another spline, so we can junction from it. 
        /// </summary>
        /// <param name="otherSpline"></param>
        /// <param name="otherSplinePercentage"></param>
        public void ConfigureAsJunction(Spline junctionStartSpline, float junctionStartPercent, float junctionStartTightness, Spline junctionEndSpline, float junctionEndPercent, float junctionEndTightness)
        {
            _junctionSplineBegin = junctionStartSpline;
            _junctionBegin_t = junctionStartPercent;
            _junctionBeginTightness = junctionStartTightness;

            _junctionSplineEnd = junctionEndSpline;
            _junctionEnd_t = junctionEndPercent;
            _junctionEndTightness = junctionEndTightness;

            SetSplineClosed(false);
        }

        /// <summary>
        /// For dynamic splines, the junction point will need to be updated. 
        /// </summary>
        public void UpdateStartJunction()
        {
            if(_junctionSplineBegin == null || Points.Length < 1)
            {
                return;
            }

            var beginJunctionSplinePoint = _junctionSplineBegin.GetPoint(_junctionBegin_t);

            if(GetSplineSpace() == Space.Self)
            {
                Points[0] = InverseTransformSplinePoint(beginJunctionSplinePoint);
            }
            else
            {
                Points[0] = beginJunctionSplinePoint;
            }

            switch (GetSplineMode())
            {
                case SplineMode.Linear:
                    break;
                case SplineMode.BSpline:
                case SplineMode.Bezier:
                    if (Points.Length > 1)
                    {
                        var junctionSplineForward = _junctionSplineBegin.GetForward(_junctionBegin_t);
                        var junctionHandle = beginJunctionSplinePoint;
                        junctionHandle.position += junctionSplineForward * _junctionBeginTightness;

                        if(GetSplineSpace() == Space.Self)
                        {
                            Points[1] = InverseTransformSplinePoint(junctionHandle);
                        }
                        else
                        {
                            Points[1] = junctionHandle;
                        }
                    }
                    break; 
            }
        }

        /// <summary>
        /// For dynamic splines, the junction point will need to be updated. 
        /// </summary>
        public void UpdateEndJunction()
        {
            if (_junctionSplineEnd == null || Points.Length < 1)
            {
                return;
            }

            var endJunctionSplinePoint = _junctionSplineEnd.GetPoint(_junctionEnd_t);

            if (GetSplineSpace() == Space.Self)
            {
                Points[Points.Length - 1] = InverseTransformSplinePoint(endJunctionSplinePoint);
            }
            else
            {
                Points[Points.Length - 1] = endJunctionSplinePoint;
            }

            switch (GetSplineMode())
            {
                case SplineMode.Linear:
                    break;

                case SplineMode.BSpline:
                case SplineMode.Bezier:
                    if (Points.Length > 2)
                    {
                        var junctionSplineForward = _junctionSplineEnd.GetForward(_junctionEnd_t);
                        var junctionHandle = endJunctionSplinePoint;
                        junctionHandle.position -= junctionSplineForward * _junctionEndTightness;

                        if(GetSplineSpace() == Space.Self)
                        {
                            Points[Points.Length - 1 - 1] = InverseTransformSplinePoint(junctionHandle);
                        }
                        else
                        {
                            Points[Points.Length - 1 - 1] = junctionHandle;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Returns the spline's curve type. 
        /// </summary>
        /// <returns></returns>
        public SplineMode GetSplineMode()
        {
            return Mode;
        }

        /// <summary>
        /// Sets the Spline's Curve Type. 
        /// If there are not a valid number of Points when turning a Linear spline into a Bezier spline, 
        /// this function will append some more points, so be careful.
        /// </summary>
        /// <param name="newMode"></param>
        public void SetSplineMode(SplineMode newMode)
        {
            if (Mode != newMode)
            {
                // need to
                Mode = newMode;

                if (newMode == SplineMode.Bezier)
                {
                    var create_count = 0;
                    var point_mod = (Points.Length - 1) % 3;
                    if (point_mod == 0) create_count = 0;
                    else if (point_mod == 1) create_count = 2;
                    else if (point_mod == 2) create_count = 1;

                    if (create_count > 0)
                    {
                        var final_point = GetPoint(1f);
                        var final_forward = GetForward(1f);

                        var final_index = Points.Length;
                        ResizePointArray(Points.Length + create_count);
                        for (var i = final_index; i < Points.Length; ++i)
                        {
                            final_point.position += final_forward;
                            Points[i] = final_point;
                        }

#if UNITY_EDITOR
                        Debug.LogWarning($"Created {create_count} new points to ensure valid Bezier spline.");
#endif
                    }
                }
            }
        }
         
        public Space GetSplineSpace()
        {
            return SplineSpace;
        }

        /// <summary>
        /// Ignoring handles, returns the "point" count. 
        /// For Bezier splines, this means returning the number of anchor points in the spline.
        /// </summary>
        /// <returns></returns>
        public int GetPointCountIgnoreHandles()
        {
            var isBezier = GetSplineMode() == SplineMode.Bezier;
            if(isBezier)
            {
                return Points.Length / 3; 
            }
            else
            {
                return Points.Length;
            }
        }

        /// <summary>
        /// Matches the input index to return the anchor point index, given the count of anchor points
        /// returned by GetPointCountIgnoreHandles().
        /// </summary>
        /// <param name="index"></param>
        /// <param name="includeHandles"></param>
        /// <returns></returns>
        public int GetPointIgnoreHandles(int index)
        {
            if(GetSplineMode() == SplineMode.Bezier)
            {
                return index * 3; 
            }
            else
            {
                return index;
            }
        }

        /// <summary>
        /// Updates the Space of this Spline. 
        /// Optionally automatically recalculates the stored Points to keep their relative world position stable when swapping spaces.
        /// </summary>
        /// <param name="space"></param>
        public void SetSplineSpace(Space space, bool updatePoints)
        {
            var previous = SplineSpace;
            if (previous != space)
            {
                if (updatePoints)
                {
                    for (var i = 0; i < Points.Length; ++i)
                    {
                        var point = Points[i];

                        switch (space)
                        {
                            case Space.Self:
                                point = InverseTransformSplinePoint(point);
                                break;
                            case Space.World:
                                point = TransformSplinePoint(point);
                                break;
                        }

                        Points[i] = point;
                    }
                }

                SplineSpace = space;
            }
        }

        /// <summary>
        /// Gets an interpolated SplinePoint along the spline at a given screenPosition, relative to the given camera. 
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="screenPosition"></param>
        /// <returns></returns>
        public SplinePoint ProjectOnSpline(Camera camera, Vector3 screenPosition)
        {
            var t = ProjectOnSpline_t(camera, screenPosition);
            return GetPoint(t);
        }

        /// <summary>
        /// Gets an interpolated SplinePoint along the spline at the given world position.
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public SplinePoint ProjectOnSpline(Vector3 position)
        {
            var t = ProjectOnSpline_t(position);
            return GetPoint(t);
        }

        /// <summary>
        /// Projects a world position onto this Spline, which can be used for fetching an interpolated SplinePoint with GetPoint(t); 
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public float ProjectOnSpline_t(Vector3 position)
        {
            if (SplineSpace == Space.Self)
            {
                position = transform.InverseTransformPoint(position);
            }

            if (Points.Length == 0)
            {
                return 0f;
            }

            if (Points.Length == 1)
            {
                return 0f;
            }

            var length = Points.Length;

            if (Mode == SplineMode.Linear)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var closestIndex = -1;

                for (var i = 0; i < length; ++i)
                {
                    var point = Points[i];

                    var toPoint = point.position - position;
                    var toPointDistance = toPoint.magnitude;
                    if (toPointDistance < closestDistance)
                    {
                        closestDistance = toPointDistance;
                        closestIndex = i;
                    }
                }

                SplinePoint point0;
                SplinePoint point1;

                int lowest_index;

                if (!ClosedSpline && closestIndex <= 0)
                {
                    lowest_index = closestIndex;

                    var index_a = closestIndex;
                    var index_b = closestIndex + 1;

                    point0 = Points[index_a];
                    point1 = Points[index_b];
                }
                else if (!ClosedSpline && closestIndex == Points.Length - 1)
                {
                    lowest_index = closestIndex - 1;

                    var index_a = closestIndex;
                    var index_b = closestIndex - 1;

                    point0 = Points[index_b];
                    point1 = Points[index_a];
                }
                else
                {
                    var index_a = closestIndex;
                    var index_b = closestIndex - 1;
                    var index_c = closestIndex + 1;

                    if (ClosedSpline)
                    {
                        var mod_value = Points.Length - 1;
                        index_a = ((index_a % mod_value) + mod_value) % mod_value;
                        index_b = ((index_b % mod_value) + mod_value) % mod_value;
                        index_c = ((index_c % mod_value) + mod_value) % mod_value;
                    }

                    var point_a = Points[index_a];
                    var point_b = Points[index_b];
                    var point_c = Points[index_c];

                    var projected_ba = ProjectLinear(point_b, point_a, position);
                    var projected_ac = ProjectLinear(point_a, point_c, position);

                    var distance_ba = Vector3.Distance(position, projected_ba);
                    var distance_ac = Vector3.Distance(position, projected_ac);

                    if (distance_ba < distance_ac)
                    {
                        lowest_index = index_b;

                        point0 = point_b;
                        point1 = point_a;
                    }
                    else
                    {
                        lowest_index = index_a;

                        point0 = point_a;
                        point1 = point_c;
                    }
                }

                var projectedPosition = ProjectLinear(point0, point1, position);
                var percentageBetweenPoints = GetPercentageLinear(point0, point1, projectedPosition);
                var total_t = (float)lowest_index / (Points.Length - 1) + percentageBetweenPoints * (1f / (Points.Length - 1));

                // Debug.Log($"{lowest_index} - {closestIndex} / {Points.Length}, {percentageBetweenPoints}, {total_t}");

                return total_t;
            }
            else if (Mode == SplineMode.Bezier)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var best_t = 0f;
                var best_i = -1;
                
                var groupCount = (Points.Length - 1) - (Points.Length - 1) % 3;
                
                for (var i = 0; i < length - 3; i += 3)
                {
                    var p0 = Points[i + 0];
                    var p1 = Points[i + 1];
                    var p2 = Points[i + 2];
                    var p3 = Points[i + 3];

                    var t = QuadraticProject(p0.position, p1.position, p2.position, p3.position, position);
                    var projected = QuadraticInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                    var distance = Vector3.Distance(projected, position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;

                        best_i = i;
                        best_t = t;
                    }
                }

                return (float)best_i / (groupCount) + best_t * (3f / (groupCount));
            }
            else if (Mode == SplineMode.BSpline)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var best_t = 0f;
                var best_i = -1;

                for (var i = 0; i < length; i += 1)
                {
                    var index = Mathf.Clamp(i, 0, Points.Length) - 1; // note, offsetting by -1 so index0 starts behind current point 

                    int index0;
                    int index1;
                    int index2;
                    int index3;

                    if (ClosedSpline)
                    {
                        int mod_count = Points.Length;

                        index0 = ((index + 0) % (mod_count) + mod_count) % mod_count;
                        index1 = ((index + 1) % (mod_count) + mod_count) % mod_count;
                        index2 = ((index + 2) % (mod_count) + mod_count) % mod_count;
                        index3 = ((index + 3) % (mod_count) + mod_count) % mod_count;
                    }
                    else
                    {
                        index0 = index + 0;
                        index1 = index + 1;
                        index2 = index + 2;
                        index3 = index + 3;

                        index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                        index1 = Mathf.Clamp(index1, 0, Points.Length - 1);
                        index2 = Mathf.Clamp(index2, 0, Points.Length - 1);
                        index3 = Mathf.Clamp(index3, 0, Points.Length - 1);
                    }

                    var p0 = Points[index0];
                    var p1 = Points[index1];
                    var p2 = Points[index2];
                    var p3 = Points[index3];

                    var t = BSplineProject(p0.position, p1.position, p2.position, p3.position, position);
                    var projected = BSplineInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                    var distance = Vector3.Distance(projected, position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;

                        best_i = i;
                        best_t = t;
                    }
                }

                return (float)best_i / Points.Length + best_t * (1f / Points.Length);
            }

            return 0f;
        }

        /// <summary>
        /// Updates a non-jobified internal data structure used in the non-jobified ProjectDistance() function.
        /// <br/> Returns the distance calculated. Note: this is not going to be exactly the same as CalculateSplineLength()'s result. 
        /// resolution can be set, if the default value is not high enough (for very complex splines). 
        /// <br/> Note: changing the resolution will re-allocate managed memory (generating garbage), so avoid changing it frequently. 
        /// </summary>
        /// <returns></returns>
        public float UpdateDistanceProjectionsData(int resolution = 1000)
        {
            if(_projectionDistanceCache == null || _projectionDistanceCache.Length != resolution)
            {
                _projectionDistanceCache = new float[resolution]; 
            }

            _projectionDistanceLength = 0f;
            var prevPosition = GetPoint(0).position;

            for (var r = 0; r < resolution; ++r)
            {
                var resT = (float)r / resolution;
                var point = GetPoint(resT);

                _projectionDistanceLength += Vector3.Distance(point.position, prevPosition);
                _projectionDistanceCache[r] = _projectionDistanceLength;

                prevPosition = point.position;
            }

            return _projectionDistanceLength; 
        }

        /// <summary>
        /// For moving over the spline based on distance rather than time. Useful to move along a spline with a constant velocity. 
        /// For this non-jobified function, you must first call UpdateDistanceProjectionsData() at least once. 
        /// Call UpdateDistanceProjectionsData() again any time the spline may have been updated.
        /// </summary>
        /// <param name="d"></param>
        /// <returns></returns>
        public float ProjectDistance(float d, bool repeat = false)
        {
            if(repeat)
            {
                d = Mathf.Repeat(d, _projectionDistanceLength);
            }
            else
            {
                d = Mathf.Clamp(d, 0f, _projectionDistanceLength); 
            }

            // find out our closest d 
            var resolution = _projectionDistanceCache.Length;

            for (var r = 1; r < resolution; ++r)
            {
                // one found, lerp 
                if(_projectionDistanceCache[r] > d)
                {
                    var d0 = _projectionDistanceCache[r - 1];
                    var d1 = _projectionDistanceCache[r - 0];

                    var t0 = (float) (r - 1) / resolution;
                    var t1 = (float) (r - 0) / resolution;

                    var tt = Mathf.InverseLerp(d0, d1, d);
                    return Mathf.Lerp(t0, t1, tt);
                }
            }

            return 1.0f; 
        }

        /// <summary>
        /// ProjectOnSpline will give you a percentage. This will give you a distance from a percentage. 
        /// Useful for getting the starting distance, for then moving along the spline via ProjectDistance().
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public float ProjectPercentToDistance(float t)
        {
            var resolution = _projectionDistanceCache.Length;

            for (var r = 1; r < resolution; ++r)
            {
                var d0 = _projectionDistanceCache[r - 1];
                var d1 = _projectionDistanceCache[r - 0];

                var t0 = (float)(r - 1) / resolution;
                var t1 = (float)(r - 0) / resolution;

                if(t1 > t)
                {
                    var tt = Mathf.InverseLerp(t0, t1, t);
                    var dt = Mathf.Lerp(d0, d1, tt);

                    return dt; 
                }
            }

            return _projectionDistanceLength;
        }

        /// <summary>
        /// Projects a screen position onto this Spline, which can be used for fetching an interpolated SplinePoint with GetPoint(t);
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="screenPosition"></param>
        /// <returns></returns>
        public float ProjectOnSpline_t(Camera camera, Vector3 screenPosition)
        {
            if (Points.Length == 0)
            {
                return 0f;
            }

            if (Points.Length == 1)
            {
                return 0f;
            }

            var length = Points.Length;

            if (Mode == SplineMode.Linear)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var closestIndex = -1;

                for (var i = 0; i < length; ++i)
                {
                    var point = Points[i];

                    if (SplineSpace == Space.Self)
                    {
                        point = TransformSplinePoint(point);
                    }

                    var screenPointPosition = camera.WorldToScreenPoint(point.position);
                        screenPointPosition.z = 0f;

                    var toPoint = screenPointPosition - screenPosition;
                    var toPointDistance = toPoint.magnitude;
                    if (toPointDistance < closestDistance)
                    {
                        closestDistance = toPointDistance;
                        closestIndex = i;
                    }
                }

                SplinePoint point0;
                SplinePoint point1;

                int lowest_index;

                if (!ClosedSpline && closestIndex <= 0)
                {
                    var index_a = closestIndex;
                    var index_b = closestIndex + 1;

                    point0 = Points[index_a];
                    point1 = Points[index_b];

                    lowest_index = index_a;
                }

                else if (!ClosedSpline && closestIndex == Points.Length - 1)
                {
                    var index_a = closestIndex - 1;
                    var index_b = closestIndex ;

                    point0 = Points[index_a];
                    point1 = Points[index_b];

                    lowest_index = index_a;
                }

                else
                {
                    var index_a = closestIndex;
                    var index_b = closestIndex - 1;
                    var index_c = closestIndex + 1;

                    if (ClosedSpline)
                    {
                        var mod_value = Points.Length - 1;
                        index_a = ((index_a % mod_value) + mod_value) % mod_value;
                        index_b = ((index_b % mod_value) + mod_value) % mod_value;
                        index_c = ((index_c % mod_value) + mod_value) % mod_value;
                    }

                    var point_a = Points[index_a];
                    var point_b = Points[index_b];
                    var point_c = Points[index_c];

                    if (SplineSpace == Space.Self)
                    {
                        point_a = TransformSplinePoint(point_a);
                        point_b = TransformSplinePoint(point_b);
                        point_c = TransformSplinePoint(point_c);
                    }

                    // convert from world to screen 
                    point_a.position = camera.WorldToScreenPoint(point_a.position);
                    point_b.position = camera.WorldToScreenPoint(point_b.position);
                    point_c.position = camera.WorldToScreenPoint(point_c.position);

                    var projected_ab = ProjectLinear(point_a, point_b, screenPosition);
                    var projected_ac = ProjectLinear(point_a, point_c, screenPosition);

                    var distance_ab = Vector3.Distance(screenPosition, projected_ab);
                    var distance_ac = Vector3.Distance(screenPosition, projected_ac);

                    if (distance_ab < distance_ac)
                    {
                        point0 = point_b;
                        point1 = point_a;

                        lowest_index = index_b;
                    }
                    else
                    {
                        point0 = point_a;
                        point1 = point_c;

                        lowest_index = index_a;
                    }
                }

                point0.position.z = 0;
                point1.position.z = 0;

                var projectedPosition = ProjectLinear(point0, point1, screenPosition);
                var percentageBetweenPoints = GetPercentageLinear(point0, point1, projectedPosition);
                return (float)lowest_index / Points.Length + percentageBetweenPoints * (1f / Points.Length);
            }
            else if (Mode == SplineMode.Bezier)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var best_i = 0;
                var best_t = 0f;

                var groupCount = (Points.Length - 1) - (Points.Length - 1) % 3;

                for (var i = 0; i < length - 3; i += 3)
                {
                    var p0 = Points[i + 0];
                    var p1 = Points[i + 1];
                    var p2 = Points[i + 2];
                    var p3 = Points[i + 3];

                    if (SplineSpace == Space.Self)
                    {
                        p0 = TransformSplinePoint(p0);
                        p1 = TransformSplinePoint(p1);
                        p2 = TransformSplinePoint(p2);
                        p3 = TransformSplinePoint(p3);
                    }

                    // convert to screen coords 
                    p0.position = camera.WorldToScreenPoint(p0.position);
                    p1.position = camera.WorldToScreenPoint(p1.position);
                    p2.position = camera.WorldToScreenPoint(p2.position);
                    p3.position = camera.WorldToScreenPoint(p3.position);

                    var t = QuadraticProject(p0.position, p1.position, p2.position, p3.position, screenPosition);
                    var projected = QuadraticInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                    var distance = Vector3.Distance(projected, screenPosition);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;

                        best_i = i;
                        best_t = t;
                    }
                }

                return (float)best_i / groupCount + best_t * (3f / groupCount);
            }
            else if (Mode == SplineMode.BSpline)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var best_i = 0;
                var best_t = 0f;
                 
                for (var i = 0; i < length; i += 1)
                {
                    var index = Mathf.Clamp(i, 0, Points.Length) - 1; // note, offsetting by -1 so index0 starts behind current point 

                    int index0;
                    int index1;
                    int index2;
                    int index3;

                    if (ClosedSpline)
                    {
                        int mod_count = Points.Length;

                        index0 = ((index + 0) % (mod_count) + mod_count) % mod_count;
                        index1 = ((index + 1) % (mod_count) + mod_count) % mod_count;
                        index2 = ((index + 2) % (mod_count) + mod_count) % mod_count;
                        index3 = ((index + 3) % (mod_count) + mod_count) % mod_count;
                    }
                    else
                    {
                        index0 = index + 0;
                        index1 = index + 1;
                        index2 = index + 2;
                        index3 = index + 3;

                        index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                        index1 = Mathf.Clamp(index1, 0, Points.Length - 1);
                        index2 = Mathf.Clamp(index2, 0, Points.Length - 1);
                        index3 = Mathf.Clamp(index3, 0, Points.Length - 1);
                    }

                    var p0 = Points[index0];
                    var p1 = Points[index1];
                    var p2 = Points[index2];
                    var p3 = Points[index3];

                    if (SplineSpace == Space.Self)
                    {
                        p0 = TransformSplinePoint(p0);
                        p1 = TransformSplinePoint(p1);
                        p2 = TransformSplinePoint(p2);
                        p3 = TransformSplinePoint(p3);
                    }

                    // convert to screen coords 
                    p0.position = camera.WorldToScreenPoint(p0.position);
                    p1.position = camera.WorldToScreenPoint(p1.position);
                    p2.position = camera.WorldToScreenPoint(p2.position);
                    p3.position = camera.WorldToScreenPoint(p3.position);

                    p0.position.z = 0f;
                    p1.position.z = 0f;
                    p2.position.z = 0f;
                    p3.position.z = 0f;

                    var t = BSplineProject(p0.position, p1.position, p2.position, p3.position, screenPosition);
                    var projected = BSplineInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                    var distance = Vector3.Distance(projected, screenPosition);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;

                        best_i = i;
                        best_t = t;
                    }
                }

                return (float)best_i / Points.Length + best_t * (1f / Points.Length);
            }
            else
            {
                return 0f;
            }
        }

        /// <summary>
        /// Transforms a SplinePoint from local space to world space.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public SplinePoint TransformSplinePoint(SplinePoint point)
        {
            var matrix = transform.localToWorldMatrix;

            point.position = matrix.MultiplyPoint(point.position);
            point.rotation = matrix.rotation * point.rotation;
            point.scale = matrix.MultiplyVector(point.scale);

            return point;
        }

        /// <summary>
        /// Transforms a SplinePoint from world space to local space.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public SplinePoint InverseTransformSplinePoint(SplinePoint point)
        {
            var matrix = transform.worldToLocalMatrix;

            point.position = matrix.MultiplyPoint(point.position);
            point.rotation = matrix.rotation * point.rotation;
            point.scale = matrix.MultiplyVector(point.scale);

            return point;
        }

        /// <summary>
        /// Returns a world space SplinePoint, given an input t. t is valid from 0 to 1. 
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public SplinePoint GetPoint(float t)
        {
            if (Points.Length == 0)
            {
                return new SplinePoint();
            }

            if (ClosedSpline)
            {
                t = Mathf.Repeat(t, 1f);
            }
            else
            {
                t = Mathf.Clamp01(t);
            }

            if (Mode == SplineMode.Linear)
            {
                var delta_t = 1f / (Points.Length - 1);
                var mod_t = Mathf.Repeat(t, delta_t);
                var inner_t = mod_t / delta_t;

                // sometimes inner_t will not quite reach 1 (0), so if we reach some threshold just force it over ourselves (to avoid spline jumping around) 
                if (inner_t > 0.9999f)
                {
                    inner_t = 0.0f;
                }

                var index0 = Mathf.FloorToInt(t * (Points.Length - 1));
                var index1 = index0 + 1;

                index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                index1 = Mathf.Clamp(index1, 0, Points.Length - 1);

                if (index0 == Points.Length - 1)
                {
                    var firstPoint = Points[index0];

                    if (SplineSpace == Space.Self)
                    {
                        firstPoint = TransformSplinePoint(firstPoint);
                    }

                    return firstPoint;
                }

                var point0 = Points[index0];
                var point1 = Points[index1];

                var result = new SplinePoint();
                result.position = Vector3.Lerp(point0.position, point1.position, inner_t);
                result.rotation = Quaternion.Slerp(point0.rotation, point1.rotation, inner_t);
                result.scale = Vector3.Lerp(point0.scale, point1.scale, inner_t);


                if (SplineSpace == Space.Self)
                {
                    return TransformSplinePoint(result);
                }
                else
                {
                    return result;
                }
            }
            else if (Mode == SplineMode.Bezier)
            {
                var groupCount = (Points.Length - 1) - (Points.Length - 1) % 3;

                var delta_t = 3f / groupCount;
                var mod_t = Mathf.Repeat(t, delta_t);
                var inner_t = mod_t / delta_t;

                var index0 = Mathf.FloorToInt(t * groupCount);
                index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                index0 = index0 - index0 % 3;

                if (index0 >= Points.Length - 3)
                {
                    var lastPoint = Points[Points.Length - 1];
                
                    if (SplineSpace == Space.Self)
                    {
                        lastPoint = TransformSplinePoint(lastPoint);
                    }
                
                    return lastPoint;
                }

                var index1 = index0 + 1;
                var index2 = index0 + 2;
                var index3 = index0 + 3;

                var point0 = Points[index0];
                var point1 = Points[index1];
                var point2 = Points[index2];
                var point3 = Points[index3];

                var result = CalculateBezierPoint(point0, point1, point2, point3, inner_t);

                if (SplineSpace == Space.Self)
                {
                    return TransformSplinePoint(result);
                }
                else
                {
                    return result;
                }
            }
            else if (Mode == SplineMode.BSpline)
            {
                var delta_t = 1f / Points.Length;
                var mod_t = Mathf.Repeat(t, delta_t);
                var inner_t = mod_t / delta_t;

                var index = Mathf.FloorToInt(t * Points.Length);

                index = Mathf.Clamp(index, 0, Points.Length);

                // note, offsetting by -1 so index0 starts behind current point 
                index -= 1;

                int index0;
                int index1;
                int index2;
                int index3;

                if (ClosedSpline)
                {
                    int mod_count = Points.Length; // - 1; // -1 to ignore duplicate final point 

                    index0 = ((index + 0) % (mod_count) + mod_count) % mod_count;
                    index1 = ((index + 1) % (mod_count) + mod_count) % mod_count;
                    index2 = ((index + 2) % (mod_count) + mod_count) % mod_count;
                    index3 = ((index + 3) % (mod_count) + mod_count) % mod_count;
                }
                else
                {
                    index0 = index + 0;
                    index1 = index + 1;
                    index2 = index + 2;
                    index3 = index + 3;

                    index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                    index1 = Mathf.Clamp(index1, 0, Points.Length - 1);
                    index2 = Mathf.Clamp(index2, 0, Points.Length - 1);
                    index3 = Mathf.Clamp(index3, 0, Points.Length - 1);
                }

                var point0 = Points[index0];
                var point1 = Points[index1];
                var point2 = Points[index2];
                var point3 = Points[index3];

                var result = CalculateBSplinePoint(point0, point1, point2, point3, inner_t);

                if (SplineSpace == Space.Self)
                {
                    return TransformSplinePoint(result);
                }
                else
                {
                    return result;
                }
            }
            // not implemented 
            else
            {
                return new SplinePoint();
            }
        }

        /// <summary>
        /// Returns a world space forward, given an input t. t is valid from 0 to 1.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public Vector3 GetForward(float t)
        {
            var delta_t = 1f / 256f;

            var p0 = GetPoint(t - delta_t * 1);
            var p1 = GetPoint(t + delta_t * 1);

            var vec = (p1.position - p0.position);
            var forward = vec.sqrMagnitude > 0 ? vec.normalized : Vector3.forward;
            
            return forward;
        }

        /// <summary>
        /// Returns the lower index of the pair surrounding wherever t ends up on the spline. Returns -1 if no points exist. t is valid from 0 to 1.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public int GetPointIndexFromTime(float t)
        {
            if (Points.Length == 0)
            {
                return -1;
            }

            t = Mathf.Clamp01(t);

            if (Mode == SplineMode.Linear)
            {
                return Mathf.FloorToInt(t * Points.Length);
            }
            else if (Mode == SplineMode.Bezier)
            {
                var index_estimate = Mathf.FloorToInt(t * Points.Length);
                return index_estimate - index_estimate % 3;
            }
            else if (Mode == SplineMode.BSpline)
            {
                return Mathf.FloorToInt(t * Points.Length);
            }

            // not implemented 
            else
            {
                return -1;
            }
        }

        /// <summary>
        /// Reverses the ordering of the internal Points array.
        /// </summary>
        public void ReversePoints()
        {
            var point_count = Points.Length;
            var point_count_half = point_count / 2;

            for (var i = 0; i < point_count_half; ++i)
            {
                var index_first = i;
                var index_last = point_count - 1 - i;

                var point_first = Points[index_first];
                var point_last = Points[index_last];

                Points[index_first] = point_last;
                Points[index_last] = point_first;
            }
        }

        /// <summary>
        /// Adds a point to the Points array given this information. Input data is expected to be world space.
        /// If the spline is a Bezier type, it will actually add 4 points internally, and handle the Handles placement for you.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <param name="scale"></param>
        public void AppendPoint(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (SplineSpace == Space.Self)
            {
                var worldToLocalMatrix = transform.worldToLocalMatrix;
                position = worldToLocalMatrix.MultiplyPoint(position);
                rotation = worldToLocalMatrix.rotation * rotation;
                scale = worldToLocalMatrix.MultiplyVector(scale);
            }

            switch(Mode)
            {
                case SplineMode.Linear:
                case SplineMode.BSpline:
                    ResizePointArray(Points.Length + 1);
                    var last_index = Points.Length - 1;
                    Points[last_index] = new SplinePoint(position, rotation, scale);
                    break;
                case SplineMode.Bezier:
                    if (Points.Length == 0)
                    {
                        ResizePointArray(Points.Length + 1);
                        Points[0] = new SplinePoint(position + Vector3.forward * 0, rotation, scale);
                    }
                    else if (Points.Length == 1)
                    {
                        ResizePointArray(Points.Length + 3);

                        var firstPointPos = Points[0].position;
                        var fromFirstPointPos = position - firstPointPos;
                        var distanceScale = 0.25f;

                        Points[1] = new SplinePoint(firstPointPos + fromFirstPointPos * distanceScale, rotation, scale);    // handle 1
                        Points[2] = new SplinePoint(position - fromFirstPointPos * distanceScale, rotation, scale);         // handle 2
                        Points[3] = new SplinePoint(position, rotation, scale);                                             // point  2
                    }
                    else
                    {
                        ResizePointArray(Points.Length + 3);

                        var index_prev_handle = Points.Length - 5;
                        var index_prev_point = Points.Length - 4;

                        var prev_handle = Points[index_prev_handle];
                        var prev_point = Points[index_prev_point];

                        // update previous handle to mirror new handle
                        var new_to_prev = position - prev_point.position;
                        var distanceScale = 0.25f;

                        prev_handle.position = prev_point.position - new_to_prev * distanceScale;
                        Points[index_prev_handle] = prev_handle;

                        Points[Points.Length - 3] = new SplinePoint(prev_point.position + new_to_prev * distanceScale, rotation, scale);    // handle 1
                        Points[Points.Length - 2] = new SplinePoint(position - new_to_prev * distanceScale, rotation, scale);               // handle 2 
                        Points[Points.Length - 1] = new SplinePoint(position, rotation, scale);                                             // point 
                    }
                    break; 
            }
        }

        /// <summary>
        /// Inserts a new point into the spline, between the two points found from projecting the world positon on the sl
        /// </summary>
        /// <param name="placingPoint"></param>
        public void InsertPoint(SplinePoint placingPoint)
        {
            var t = ProjectOnSpline_t(placingPoint.position); // resolves space internally 
            var pointIndex = GetPointIndexFromTime(t);
            var newPoint = GetPoint(t);
            var forward = GetForward(t);

            if (SplineSpace == Space.Self)
            {
                newPoint = InverseTransformSplinePoint(newPoint);
                forward = transform.InverseTransformDirection(forward);
            }

            // don't insert point before or after the spline (MUST be a true insert) 
            if (t > 0f && t < 1f)
            {

                var pointList = Points.ToList();
                if (Mode == SplineMode.Linear)
                {
                    pointList.Insert(pointIndex + 1, newPoint);
                }
                else if (Mode == SplineMode.Bezier)
                {
                    var point_start = pointList[pointIndex + 0];
                    var point_end = pointList[pointIndex + 3];

                    var distance0 = Vector3.Distance(point_start.position, newPoint.position);
                    var distance1 = Vector3.Distance(point_end.position, newPoint.position);
                    var point_distance = Mathf.Min(distance0, distance1);

                    var handle0 = newPoint;
                    var handle1 = newPoint;

                    handle0.position -= forward * point_distance * 0.25f;
                    handle1.position += forward * point_distance * 0.25f;

                    // inserts after a single handle, so we can slot a point with two surrounding handles 
                    pointList.Insert(pointIndex + 2 + 0, handle0);
                    pointList.Insert(pointIndex + 2 + 1, newPoint);
                    pointList.Insert(pointIndex + 2 + 2, handle1);

                }
                else if (Mode == SplineMode.BSpline)
                {
                    pointList.Insert(pointIndex + 1, newPoint);
                }
                else
                {
                    // not implemented? 
                }

                // update original array with list 
                Points = pointList.ToArray();
            }
        }

        /// <summary>
        /// Increases the internal Point array length. Does not automatically add valid points.
        /// </summary>
        /// <param name="newLength"></param>
        public void ResizePointArray(int newLength)
        {
            if (newLength < 0) newLength = 0;

            var newArray = new SplinePoint[newLength];
            var copyLength = Mathf.Min(Points.Length, newLength);
            for (var i = 0; i < copyLength; ++i)
            {
                newArray[i] = Points[i];
            }

            Points = newArray;
        }

        /// <summary>
        /// Call this after moving the first or last Points in the internal array, to ensure the spline stays closed.
        /// </summary>
        public void EnsureSplineStaysClosed()
        {
            if (!ClosedSpline) return;

            var length = Points.Length;

            switch (Mode)
            {
                case SplineMode.Linear:
                    Points[length - 1] = Points[0];
                    break;
                case SplineMode.Bezier:
                    var previous_handle0 = Points[length - 5];
                    var previous_anchor0 = Points[length - 4];

                    var to_anchor = previous_anchor0.position - previous_handle0.position;

                    var new_handle0 = previous_anchor0;
                    new_handle0.position += to_anchor;

                    var next_anchor = Points[0];
                    var next_handle = Points[1];

                    var to_next_anchor = next_anchor.position - next_handle.position;

                    var new_handle1 = next_anchor;
                    new_handle1.position += to_next_anchor;

                    Points[length - 3] = new_handle0;
                    Points[length - 2] = new_handle1;
                    Points[length - 1] = next_anchor;
                    break;

                case SplineMode.BSpline:
                    // Points[length - 1] = Points[0];
                    break;
                default:
                    // not implemented 
                    break;
            }
        }

        /// <summary>
        /// Opens or closes the spline, handling any internal changes necessary based on the spline's Mode.
        /// </summary>
        /// <param name="closed"></param>
        public void SetSplineClosed(bool closed)
        {
            var needsUpdate = ClosedSpline != closed;
            ClosedSpline = closed;

            if (needsUpdate)
            {
                // closing 
                if(closed)
                {
                    switch (GetSplineMode())
                    {
                        case SplineMode.Linear:
                            ResizePointArray(Points.Length + 1);
                            break;
                        case SplineMode.Bezier:
                            ResizePointArray(Points.Length + 3);
                            break;
                        case SplineMode.BSpline:
                            // ResizePointArray(Points.Length + 1);
                            break;
                        default:
                            // not implemented 
                            break;
                    }

                    EnsureSplineStaysClosed();
                }

                // opening 
                else
                {
                    switch (GetSplineMode())
                    {
                        case SplineMode.Linear:
                            ResizePointArray(Points.Length - 1); // remove extra point 
                            break;
                        case SplineMode.Bezier:
                            ResizePointArray(Points.Length - 3); // remove extra points
                            break;
                        case SplineMode.BSpline:
                            // ResizePointArray(Points.Length - 1); // remove extra point 
                            break;
                        default:
                            // not implemented 
                            break;
                    }
                }
            }

        }

        /// <summary>
        /// Returns true if this spline is closed. 
        /// </summary>
        /// <returns></returns>
        public bool GetSplineClosed()
        {
            return ClosedSpline;
        }

        /// <summary>
        /// Returns true if spline type has handles. True for bezier type splines. 
        /// </summary>
        /// <returns></returns>
        public bool GetHasHandles()
        {
            var splineMode = GetSplineMode();
            return splineMode == SplineMode.Bezier;
        }

        /// <summary>
        /// Helper function to automatically set rotations of all spline points, such that the points are each facing their next point. 
        /// Takes in an array of point indices. Used by the SplineEditor window.
        /// </summary>
        public void SetSplinePointsRotationForward(int[] pointIndices)
        {
            var isLocalSpace = GetSplineSpace() == Space.Self;

            if (isLocalSpace)
            {
                SetSplineSpace(Space.World, true);
            }

            for (var p = 0; p < pointIndices.Length; ++p)
            {
                var i = pointIndices[p];
                var selectedPoint = Points[i];

                var selected_t = (float)p / Points.Length;
                var selected_forward = GetForward(selected_t);
                var selected_up = Vector3.up;

                if (selected_forward.sqrMagnitude > 0.0001f && (selected_forward - selected_up).sqrMagnitude > 0.0001f)
                {
                    selectedPoint.rotation = Quaternion.LookRotation(selected_forward, selected_up);
                }

                Points[i] = selectedPoint;
            }

            if (isLocalSpace)
            {
                SetSplineSpace(Space.Self, true);
            }
        }

        /// <summary>
        /// Helper function to automatically set rotations of all spline points, such that the points are each facing their next point. 
        /// </summary>
        public void SetSplinePointsRotationForward()
        {
            var isLocalSpace = GetSplineSpace() == Space.Self;

            if (isLocalSpace)
            {
                SetSplineSpace(Space.World, true);
            }

            for (var p = 0; p < Points.Length; ++p)
            {
                var selectedPoint = Points[p];

                var selected_t = (float) p / Points.Length;
                var selected_forward = GetForward(selected_t);
                var selected_up = Vector3.up;

                if(selected_forward.sqrMagnitude > 0.0001f && (selected_forward - selected_up).sqrMagnitude > 0.0001f)
                {
                    selectedPoint.rotation = Quaternion.LookRotation(selected_forward, selected_up);
                }

                Points[p] = selectedPoint;
            }

            if (isLocalSpace)
            {
                SetSplineSpace(Space.Self, true);
            }
        }

        /// <summary>
        /// Helper function to get the percentage of a spline at a given raw point index. 
        /// By raw, I mean it does not need to account for handles in the Points[] array. 
        /// </summary>
        /// <param name="rawPointIndex"></param>
        /// <returns></returns>
        public float GetPercentageOfPointIndex(int rawPointIndex)
        {
            var splinePoint = Points[rawPointIndex];
            return ProjectOnSpline_t(splinePoint.position); 
        }

        /// <summary>
        /// Returns true if the start or end of this spline is a junction to another spline. 
        /// </summary>
        /// <returns></returns>
        public bool GetHasAnyJunction()
        {
            return _junctionSplineBegin != null || _junctionSplineEnd != null; 
        }

        /// <summary>
        /// Returns the percentage for the attachment position to our parent junctioned spline. 
        /// </summary>
        /// <returns></returns>
        public float GetStartSplineJunctionPercent()
        {
            return _junctionBegin_t;
        }

        /// <summary>
        /// Returns the spline we are junctioned to at our start. 
        /// </summary>
        /// <returns></returns>
        public Spline GetStartSplineJunction()
        {
            return _junctionSplineBegin;
        }

        /// <summary>
        /// Returns the tightness of the starting junction. This is just the distance between the first two handles for Bezier and BSpline types. 
        /// </summary>
        /// <returns></returns>
        public float GetStartJunctionTightness()
        {
            return _junctionBeginTightness;
        }

        /// <summary>
        /// Returns the percentage for the attachment position to our parent junctioned spline at our end. 
        /// </summary>
        /// <returns></returns>
        public float GetEndSplineJunctionPercent()
        {
            return _junctionEnd_t;
        }

        /// <summary>
        /// Returns the spline we are junctioned to at our end. 
        /// </summary>
        /// <returns></returns>
        public Spline GetEndSplineJunction()
        {
            return _junctionSplineEnd;
        }

        /// <summary>
        /// Returns the tightness of the ending junction. This is just the distance between the first two handles for Bezier and BSpline types. 
        /// </summary>
        /// <returns></returns>
        public float GetEndJunctionTightness()
        {
            return _junctionEndTightness;
        }

        /// <summary>
        /// Updates the start junction's percentage. 
        /// </summary>
        /// <param name="percent"></param>
        public void SetStartJunctionPercent(float percent)
        {
            _junctionBegin_t = percent;
        }

        /// <summary>
        /// Updates the end junction's percentage.
        /// </summary>
        /// <param name="percent"></param>
        public void SetEndJunctionPercent(float percent)
        {
            _junctionEnd_t = percent;
        }

        /// <summary>
        /// Returns true if our spline mode supports a tightness parameter when acting as a junction.
        /// </summary>
        /// <returns></returns>
        public bool GetSplineJunctionsSupportTightness()
        {
            return Mode != SplineMode.Linear;
        }

        /// <summary>
        /// This will take the transform position and plae it at the center of all the points. 
        /// Point data will be lifted out, then put back in, so points will be in the same world space positions after the pivot moves.
        /// This will also unparent children and reparent them so their world space positions stay the same. 
        /// Generates some garbage. 
        /// </summary>
        public void CenterPivotTransform()
        {
            var childTransformCount = transform.childCount;
            var rememberChildren = new Transform[childTransformCount];

            for(var t = 0; t < childTransformCount; ++t)
            {
                rememberChildren[t] = transform.GetChild(t);
            }

            transform.DetachChildren();

            var pointCount = Points.Length;
            var pointsClone = new SplinePoint[pointCount];

            if (GetSplineSpace() == Space.Self)
            {
                for (var i = 0; i < pointCount; ++i)
                {
                    pointsClone[i] = TransformSplinePoint(Points[i]);
                }
            }

            var centerPoint = Vector3.zero;
            for (var i = 0; i < pointCount; ++i)
            {
                var point = pointsClone[i];
                centerPoint += point.position;
            }

            centerPoint /= Mathf.Max(1, pointCount);

            transform.position = centerPoint;

            if (GetSplineSpace() == Space.Self)
            {
                for (var i = 0; i < pointCount; ++i)
                {
                    Points[i] = InverseTransformSplinePoint(pointsClone[i]);
                }
            }

            for (var t = 0; t < childTransformCount; ++t)
            {
                var child = rememberChildren[t];
                child.SetParent(transform, true);
            }
        }

        public static Vector3 QuadraticInterpolate(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, float t)
        {
            var oneMinusT = 1f - t;
            var result =
                oneMinusT * oneMinusT * oneMinusT * point0 +
                3f * oneMinusT * oneMinusT * t * point1 +
                3f * oneMinusT * t * t * point2 +
                t * t * t * point3;

            return result;

            // var ab = Vector3.Lerp(point0, point1, t);
            // var bc = Vector3.Lerp(point1, point2, t);
            // var cd = Vector3.Lerp(point2, point3, t);
            // 
            // var ac = Vector3.Lerp(ab, bc, t);
            // var bd = Vector3.Lerp(bc, cd, t);
            // 
            // var ad = Vector3.Lerp(ac, bd, t);
            // 
            // return ad; 
        }

        public static Vector3 BSplineInterpolate(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, float t)
        {
            var result = (
                  (  -point0 + point2) * 0.5f
                + (  (point0 - 2f * point1 + point2) * 0.5f
                + (  -point0 + 3f * point1 - 3f * point2 + point3) * 0.166666f * t) * t ) * t
                + (   point0 + 4f * point1 + point2) * 0.166666f;

            return result;
        }

        public static Vector3 QuadraticFirstDerivative(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, float t)
        {
            t = Mathf.Clamp01(t);

            float oneMinusT = 1f - t;
            return
                3f * oneMinusT * oneMinusT * (point1 - point0) +
                6f * oneMinusT * t * (point2 - point1) +
                3f * t * t * (point3 - point2);
        }

        public static Quaternion QuadraticInterpolate(Quaternion point0, Quaternion point1, Quaternion point2, Quaternion point3, float t)
        {
            // var oneMinusT = 1f - t;
            // var result =
            //     oneMinusT * oneMinusT * oneMinusT * point0 +
            //     3f * oneMinusT * oneMinusT * t * point1 +
            //     3f * oneMinusT * t * t * point2 +
            //     t * t * t * point3;
            // 
            // return result;

            var ab = Quaternion.Slerp(point0, point1, t);
            var bc = Quaternion.Slerp(point1, point2, t);
            var cd = Quaternion.Slerp(point2, point3, t);

            var ac = Quaternion.Slerp(ab, bc, t);
            var bd = Quaternion.Slerp(bc, cd, t);

            var ad = Quaternion.Slerp(ac, bd, t);

            return ad;
        }

        private static SplinePoint CalculateBezierPoint(SplinePoint point0, SplinePoint point1, SplinePoint point2, SplinePoint point3, float t)
        {
            var result = new SplinePoint();

            result.position = QuadraticInterpolate(point0.position, point1.position, point2.position, point3.position, t);
            result.rotation = QuadraticInterpolate(point0.rotation, point1.rotation, point2.rotation, point3.rotation, t);
            result.scale = QuadraticInterpolate(point0.scale, point1.scale, point2.scale, point3.scale, t);

            return result;
        }

        private static SplinePoint CalculateBSplinePoint(SplinePoint point0, SplinePoint point1, SplinePoint point2, SplinePoint point3, float t)
        {
            var result = new SplinePoint();

            result.position = BSplineInterpolate(point0.position, point1.position, point2.position, point3.position, t);
            result.scale = BSplineInterpolate(point0.scale, point1.scale, point2.scale, point3.scale, t);

            // getting rotation is really dumb here, find a faster way 
            var forward0 = point0.rotation * Vector3.forward;
            var forward1 = point1.rotation * Vector3.forward;
            var forward2 = point2.rotation * Vector3.forward;
            var forward3 = point3.rotation * Vector3.forward;

            var up0 = point0.rotation * Vector3.up;
            var up1 = point1.rotation * Vector3.up;
            var up2 = point2.rotation * Vector3.up;
            var up3 = point3.rotation * Vector3.up;

            var result_forward = BSplineInterpolate(forward0, forward1, forward2, forward3, t);
            var result_up = BSplineInterpolate(up0, up1, up2, up3, t);
            result.rotation = Quaternion.LookRotation(result_forward, result_up); 

            return result;
        }

        private static float QuadraticProject(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, Vector3 projectPoint)
        {


            // var smallestDistance = float.MaxValue;
            // var smallest_t = 0f;
            // 
            // var max_test_steps = 256;
            // 
            // for(var step = 0; step < max_test_steps; ++step)
            // {
            //     var test_t = (float)step / max_test_steps;
            // 
            //     var test_point = QuadraticInterpolate(point0, point1, point2, point3, test_t);
            // 
            //     var sqrDistance = (test_point - projectPoint).sqrMagnitude;
            //     if(sqrDistance < smallestDistance)
            //     {
            //         smallestDistance = sqrDistance;
            //         smallest_t = test_t; 
            //     }
            // }

            // return smallest_t;

            var max_steps = 128;
            var i = 0;

            var t = 0.5f;
            var delta = 1f;

            var threshold = 0.00001f;

            while (i < max_steps)
            {
                var t0 = t - delta;
                var t1 = t + delta;

                t0 = Mathf.Clamp01(t0);
                t1 = Mathf.Clamp01(t1);

                var p0 = QuadraticInterpolate(point0, point1, point2, point3, (float)t0);
                var p1 = QuadraticInterpolate(point0, point1, point2, point3, (float)t1);

                var d0 = (p0 - projectPoint).sqrMagnitude;
                var d1 = (p1 - projectPoint).sqrMagnitude;

                if (d0 < d1)
                {
                    t = t0;
                }
                else
                {
                    t = t1;
                }

                i += 1;
                delta *= 0.60f;

                if (delta < threshold)
                {
                    break;
                }
            }

            // Debug.Log($"{i} / {max_steps}");

            return Mathf.Clamp01(t);

            // var delta = 1f / max_steps;
            // for(var t = 0f; t < 1f; t += delta)
            // {
            // 
            // }

            // var t = (3 * point2 - 2 * point3 - projectPoint).magnitude / (3 * (point2 - point3)).magnitude;
            // 
            // 
            // 
            // 
            // var t_x = 1f / (point0 - 3 * point1 + 3 * point2 - point3).magnitude; 

            // var t_y = 1f / (point0.y - 3 * point1.y + 3 * point2.y - point3.y); 
            // var t_z = 1f / (point0.z - 3 * point1.z + 3 * point2.z - point3.z);
            // 
            // var p_x = QuadraticInterpolate(point0, point1, point2, point3, t_x);
            // var p_y = QuadraticInterpolate(point0, point1, point2, point3, t_y);
            // var p_z = QuadraticInterpolate(point0, point1, point2, point3, t_z);
            // 
            // var distance_x = Vector3.Distance(p_x, projectPoint);
            // var distance_y = Vector3.Distance(p_y, projectPoint);
            // var distance_z = Vector3.Distance(p_z, projectPoint);
            // 
            // if (distance_x < distance_y && distance_x < distance_z)
            //     return t_x;
            // else if (distance_y < distance_x && distance_y < distance_z)
            //     return t_y;
            // else if (distance_z < distance_y && distance_z < distance_x)
            //     return t_z;
            // else 

            //    return t_x; 
        }

        private static float BSplineProject(Vector3 point0, Vector3 point1, Vector3 point2, Vector3 point3, Vector3 projectPoint)
        {
            var max_steps = 128;
            var i = 0;

            var t = 0.5f;
            var delta = 1f;

            var threshold = 0.00001f;

            while (i < max_steps)
            {
                var t0 = t - delta;
                var t1 = t + delta;

                t0 = Mathf.Clamp01(t0);
                t1 = Mathf.Clamp01(t1);

                var p0 = BSplineInterpolate(point0, point1, point2, point3, (float)t0);
                var p1 = BSplineInterpolate(point0, point1, point2, point3, (float)t1);

                var d0 = (p0 - projectPoint).sqrMagnitude;
                var d1 = (p1 - projectPoint).sqrMagnitude;

                if (d0 < d1)
                {
                    t = t0;
                }
                else
                {
                    t = t1;
                }

                i += 1;
                delta *= 0.60f;

                if (delta < threshold)
                {
                    break;
                }
            }

            return Mathf.Clamp01(t);
        }

        private static Vector3 InterpolatePosition(SplinePoint a, SplinePoint b, SplineMode mode, float t)
        {
            switch (mode)
            {
                default:
                case SplineMode.Linear:
                    return Vector3.Lerp(a.position, b.position, t);
            }
        }

        private static Quaternion InterpolateRotation(SplinePoint a, SplinePoint b, SplineMode mode, float t)
        {
            switch (mode)
            {
                default:
                case SplineMode.Linear:
                    return Quaternion.Slerp(a.rotation, b.rotation, t);
            }
        }

        /// <summary>
        /// Equiv to Vector3.Project, but Burst compatible. 
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static Vector3 VectorProject(Vector3 a, Vector3 b)
        {
            var d = math.dot(b, b);
            if (d < 0.0001f) return Vector3.zero;

            return b * math.dot(a, b) / d; 
        }

        private static Vector3 ProjectLinear(SplinePoint a, SplinePoint b, Vector3 point)
        {
            var direction = b.position - a.position;

            var toPointA = point - a.position;
            var dotA = Vector3.Dot(direction, toPointA);
            if (dotA < 0) return a.position;

            var toPointB = point - b.position;
            var dotB = Vector3.Dot(direction, toPointB);
            if (dotB > 0) return b.position;

            direction = direction.normalized;

            var projected = VectorProject(point - a.position, direction) + a.position;
            return projected;
        }

        private static float GetPercentageLinear(SplinePoint a, SplinePoint b, Vector3 point)
        {
            var betweenAB = b.position - a.position;
            var distanceAB = betweenAB.magnitude;

            var toPoint = point - a.position;
            var distanceToPoint = toPoint.magnitude;

            var percentage = distanceToPoint / distanceAB;
            return percentage;
        }

        /// <summary>
        /// <br/>Helper function to automatically calculate the length of the spline. 
        /// <br/>This may be slow to calculate, so it is recommended you cache this value, if possible.
        /// <br/>For linear splines, this is mathematically accurate. For Bezier and BSpline, this is an estimate of finite resolution. 
        /// <br/>So, resolution is not used with linear splines. 
        /// </summary>
        /// <returns></returns>
        public float CalculateSplineLength(int resolution = 256)
        {
            var length = 0f;

            var space = GetSplineSpace();
            var pointLength = Points.Length;

            switch (Mode)
            {
                // linear is simple: iterate over points directly 
                case SplineMode.Linear:
                    for(var p = 1; p < pointLength; ++p)
                    {
                        var point0 = Points[p - 1];
                        var point1 = Points[p];

                        if(space == Space.Self)
                        {
                            point0 = TransformSplinePoint(point0);
                            point1 = TransformSplinePoint(point1);
                        }

                        length += Vector3.Distance(point0.position, point1.position);
                    }

                    break;

                // for bezier and bspline, just iterate to estimate 
                case SplineMode.Bezier:
                case SplineMode.BSpline:
                    for(var s = 0; s < resolution; ++s)
                    {
                        var t = (float) s / resolution;
                        var delta = 1f / resolution;

                        var point0 = GetPoint(t - delta);
                        var point1 = GetPoint(t);

                        length += Vector3.Distance(point0.position, point1.position); 
                    }

                    break;
            }

            return length; 
        }

        // job helpers 
        public static SplinePoint JobSafe_TransformSplinePoint(SplinePoint point, Matrix4x4 localToWorldMatrix)
        {
            var matrix = localToWorldMatrix;

            point.position = matrix.MultiplyPoint(point.position);
            point.rotation = matrix.rotation * point.rotation;
            point.scale = matrix.MultiplyVector(point.scale);

            return point;
        }

        public static SplinePoint JobSafe_InverseTransformSplinePoint(SplinePoint point, Matrix4x4 worldToLocalMatrix)
        {
            var matrix = worldToLocalMatrix;

            point.position = matrix.MultiplyPoint(point.position);
            point.rotation = matrix.rotation * point.rotation;
            point.scale = matrix.MultiplyVector(point.scale);

            return point;
        }

        public static float JobSafe_ProjectOnSpline_t(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 worldToLocalMatrix, bool ClosedSpline, Vector3 position)
        {
            if (SplineSpace == Space.Self)
            {
                position = worldToLocalMatrix.MultiplyPoint(position);
            }

            if (Points.Length == 0)
            {
                return 0f;
            }

            if (Points.Length == 1)
            {
                return 0f;
            }

            var length = Points.Length;

            if (Mode == SplineMode.Linear)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var closestIndex = -1;

                for (var i = 0; i < length; ++i)
                {
                    var point = Points[i];

                    var toPoint = point.position - position;
                    var toPointDistance = toPoint.magnitude;
                    if (toPointDistance < closestDistance)
                    {
                        closestDistance = toPointDistance;
                        closestIndex = i;
                    }
                }

                SplinePoint point0;
                SplinePoint point1;

                int lowest_index;

                if (!ClosedSpline && closestIndex <= 0)
                {
                    lowest_index = closestIndex;

                    var index_a = closestIndex;
                    var index_b = closestIndex + 1;

                    point0 = Points[index_a];
                    point1 = Points[index_b];
                }

                else if (!ClosedSpline && closestIndex == Points.Length - 1)
                {
                    lowest_index = closestIndex - 1;

                    var index_a = closestIndex;
                    var index_b = closestIndex - 1;

                    point0 = Points[index_b];
                    point1 = Points[index_a];
                }
                else
                {
                    var index_a = closestIndex;
                    var index_b = closestIndex - 1;
                    var index_c = closestIndex + 1;

                    if (ClosedSpline)
                    {
                        var mod_value = Points.Length - 1;
                        index_a = ((index_a % mod_value) + mod_value) % mod_value;
                        index_b = ((index_b % mod_value) + mod_value) % mod_value;
                        index_c = ((index_c % mod_value) + mod_value) % mod_value;
                    }

                    var point_a = Points[index_a];
                    var point_b = Points[index_b];
                    var point_c = Points[index_c];

                    var projected_ba = ProjectLinear(point_b, point_a, position);
                    var projected_ac = ProjectLinear(point_a, point_c, position);

                    var distance_ba = Vector3.Distance(position, projected_ba);
                    var distance_ac = Vector3.Distance(position, projected_ac);

                    if (distance_ba < distance_ac)
                    {
                        lowest_index = index_b;

                        point0 = point_b;
                        point1 = point_a;
                    }
                    else
                    {
                        lowest_index = index_a;

                        point0 = point_a;
                        point1 = point_c;
                    }
                }

                var projectedPosition = ProjectLinear(point0, point1, position);
                var percentageBetweenPoints = GetPercentageLinear(point0, point1, projectedPosition);
                var total_t = (float)lowest_index / (Points.Length - 1) + percentageBetweenPoints * (1f / (Points.Length - 1));

                return total_t;

            }
            else if (Mode == SplineMode.Bezier)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var best_t = 0f;
                var best_i = -1;

                var groupCount = (Points.Length - 1) - (Points.Length - 1) % 3;

                for (var i = 0; i < length - 3; i += 3)
                {
                    var p0 = Points[i + 0];
                    var p1 = Points[i + 1];
                    var p2 = Points[i + 2];
                    var p3 = Points[i + 3];

                    var t = QuadraticProject(p0.position, p1.position, p2.position, p3.position, position);
                    var projected = QuadraticInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                    var distance = Vector3.Distance(projected, position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;

                        best_i = i;
                        best_t = t;
                    }
                }

                return (float)best_i / (groupCount) + best_t * (3f / (groupCount));

            }

            else if (Mode == SplineMode.BSpline)
            {
                // find closest point 
                var closestDistance = float.MaxValue;
                var best_t = 0f;
                var best_i = -1;

                for (var i = 0; i < length; i += 1)
                {
                    var index = i;
                    index = Mathf.Clamp(index, 0, Points.Length) - 1; // note, offsetting by -1 so index0 starts behind current point 

                    int index0;
                    int index1;
                    int index2;
                    int index3;

                    if (ClosedSpline)
                    {
                        int mod_count = Points.Length;

                        index0 = ((index + 0) % (mod_count) + mod_count) % mod_count;
                        index1 = ((index + 1) % (mod_count) + mod_count) % mod_count;
                        index2 = ((index + 2) % (mod_count) + mod_count) % mod_count;
                        index3 = ((index + 3) % (mod_count) + mod_count) % mod_count;
                    }
                    else
                    {
                        index0 = index + 0;
                        index1 = index + 1;
                        index2 = index + 2;
                        index3 = index + 3;

                        index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                        index1 = Mathf.Clamp(index1, 0, Points.Length - 1);
                        index2 = Mathf.Clamp(index2, 0, Points.Length - 1);
                        index3 = Mathf.Clamp(index3, 0, Points.Length - 1);
                    }

                    var p0 = Points[index0];
                    var p1 = Points[index1];
                    var p2 = Points[index2];
                    var p3 = Points[index3];

                    var t = BSplineProject(p0.position, p1.position, p2.position, p3.position, position);
                    var projected = BSplineInterpolate(p0.position, p1.position, p2.position, p3.position, t);
                    var distance = Vector3.Distance(projected, position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;

                        best_i = i;
                        best_t = t;
                    }
                }

                return (float)best_i / Points.Length + best_t * (1f / Points.Length);
            }

            return 0f;
        }

        public static SplinePoint JobSafe_GetPoint(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 localToWorldMatrix, bool ClosedSpline, float t)
        {
            if (Points.Length == 0)
            {
                return new SplinePoint();
            }

            if (ClosedSpline)
            {
                t = Mathf.Repeat(t, 1f);
            }
            else
            {
                t = Mathf.Clamp01(t);
            }

            if (Mode == SplineMode.Linear)
            {
                var delta_t = 1f / (Points.Length - 1);
                var mod_t = Mathf.Repeat(t, delta_t);
                var inner_t = mod_t / delta_t;

                // sometimes inner_t will not quite reach 1 (0), so if we reach some threshold just force it over ourselves (to avoid spline jumping around) 
                if (inner_t > 0.9999f)
                {
                    inner_t = 0.0f;
                }

                var index0 = Mathf.FloorToInt(t * (Points.Length - 1));
                var index1 = index0 + 1;

                index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                index1 = Mathf.Clamp(index1, 0, Points.Length - 1);

                if (index0 == Points.Length - 1)
                {
                    var firstPoint = Points[index0];

                    if (SplineSpace == Space.Self)
                    {
                        firstPoint = JobSafe_TransformSplinePoint(firstPoint, localToWorldMatrix);
                    }

                    return firstPoint;
                }

                var point0 = Points[index0];
                var point1 = Points[index1];

                var result = new SplinePoint();
                result.position = Vector3.Lerp(point0.position, point1.position, inner_t);
                result.rotation = Quaternion.Slerp(point0.rotation, point1.rotation, inner_t);
                result.scale = Vector3.Lerp(point0.scale, point1.scale, inner_t);


                if (SplineSpace == Space.Self)
                {
                    return JobSafe_TransformSplinePoint(result, localToWorldMatrix);
                }
                else
                {
                    return result;
                }
            }
            else if (Mode == SplineMode.Bezier)
            {
                var groupCount = (Points.Length - 1) - (Points.Length - 1) % 3;

                var delta_t = 3f / groupCount;
                var mod_t = Mathf.Repeat(t, delta_t);
                var inner_t = mod_t / delta_t;

                var index0 = Mathf.FloorToInt(t * groupCount);
                index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                index0 = index0 - index0 % 3;

                if (index0 >= Points.Length - 3)
                {
                    var lastPoint = Points[Points.Length - 1];

                    if (SplineSpace == Space.Self)
                    {
                        lastPoint = JobSafe_TransformSplinePoint(lastPoint, localToWorldMatrix);
                    }

                    return lastPoint;
                }

                var index1 = index0 + 1;
                var index2 = index0 + 2;
                var index3 = index0 + 3;

                var point0 = Points[index0];
                var point1 = Points[index1];
                var point2 = Points[index2];
                var point3 = Points[index3];

                var result = CalculateBezierPoint(point0, point1, point2, point3, inner_t);

                if (SplineSpace == Space.Self)
                {
                    return JobSafe_TransformSplinePoint(result, localToWorldMatrix);
                }
                else
                {
                    return result;
                }
            }

            else if (Mode == SplineMode.BSpline)
            {
                var delta_t = 1f / Points.Length;
                var mod_t = Mathf.Repeat(t, delta_t);
                var inner_t = mod_t / delta_t;

                var pointCount = Points.Length;
                var index = Mathf.FloorToInt(t * pointCount);
                index = Mathf.Clamp(index, 0, pointCount);

                // note, offsetting by -1 so index0 starts behind current point 
                index -= 1;

                int index0;
                int index1;
                int index2;
                int index3;

                if (ClosedSpline)
                {
                    int mod_count = Points.Length; // - 1; // -1 to ignore duplicate final point

                    index0 = ((index + 0) % (mod_count) + mod_count) % mod_count;
                    index1 = ((index + 1) % (mod_count) + mod_count) % mod_count;
                    index2 = ((index + 2) % (mod_count) + mod_count) % mod_count;
                    index3 = ((index + 3) % (mod_count) + mod_count) % mod_count;
                }
                else
                {
                    index0 = index + 0;
                    index1 = index + 1;
                    index2 = index + 2;
                    index3 = index + 3;

                    index0 = Mathf.Clamp(index0, 0, Points.Length - 1);
                    index1 = Mathf.Clamp(index1, 0, Points.Length - 1);
                    index2 = Mathf.Clamp(index2, 0, Points.Length - 1);
                    index3 = Mathf.Clamp(index3, 0, Points.Length - 1);
                }

                var point0 = Points[index0];
                var point1 = Points[index1];
                var point2 = Points[index2];
                var point3 = Points[index3];

                if (!ClosedSpline)
                {
                    // if we're on the first point, guess 
                    if (index < 0)
                    {
                        point0.position = point1.position + (point1.position - point2.position);
                    }

                    // if we're on the last point, guess 
                    if (index >= pointCount - 2)
                    {
                        point2.position = point1.position + (point1.position - point0.position).normalized * 0.01f;
                    }

                    if (index >= pointCount - 3)
                    {
                        point3.position = point2.position + (point2.position - point1.position).normalized * 0.01f;
                    }
                }

                var result = CalculateBSplinePoint(point0, point1, point2, point3, inner_t);

                if (SplineSpace == Space.Self)
                {
                    return JobSafe_TransformSplinePoint(result, localToWorldMatrix);
                }
                else
                {
                    return result;
                }
            }

            // not implemented 
            return new SplinePoint();
        }

        public static Vector3 JobSafe_GetForward(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 localToWorldMatrix, bool ClosedSpline, float t)
        {
            var pointsLength = Points.Length;
            var delta_t = 0.0001f;

            var p0 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t - delta_t);
            var p1 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t + delta_t);

            var vec = (p1.position - p0.position);
            var forward = vec.sqrMagnitude > 0f ? vec.normalized : Vector3.forward;
            
            // note: we want to return in world space, the GetPoints above are already world space so no matrix mult required here 
            return forward;
        }

        public static Vector3 JobSafe_GetForward_FromRotation(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 localToWorldMatrix, bool ClosedSpline, float t)
        {
            var p0 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
            return (p0.rotation * Vector3.forward).normalized;
        }

        public static Matrix4x4 GetLocalToWorldAtT(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 localToWorldMatrix, bool ClosedSpline, float t)
        {
            var p0 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
            var up = Vector3.up;
            var forward = JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

            var rotation = Quaternion.LookRotation(forward, up);
            var trs = Matrix4x4.TRS(p0.position, rotation, p0.scale);
            return trs;
        }

        public static Matrix4x4 GetLocalToWorldAtT_FromRotation(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 localToWorldMatrix, bool ClosedSpline, float t)
        {
            var p0 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
            var trs = Matrix4x4.TRS(p0.position, p0.rotation, p0.scale);
            return trs.inverse;
        }

        public static float JobSafe_CalculateSplineLength(NativeArray<SplinePoint> Points, SplineMode Mode, Space SplineSpace, Matrix4x4 localToWorldMatrix, bool ClosedSpline)
        {
            var length = 0f;
            var pointLength = Points.Length;

            switch (Mode)
            {
                // linear is simple: iterate over points directly 
                case SplineMode.Linear:
                    for (var p = 1; p < pointLength; ++p)
                    {
                        var point0 = Points[p - 1];
                        var point1 = Points[p];

                        if (SplineSpace == Space.Self)
                        {
                            point0 = JobSafe_TransformSplinePoint(point0, localToWorldMatrix);
                            point1 = JobSafe_TransformSplinePoint(point1, localToWorldMatrix);
                        }

                        length += Vector3.Distance(point0.position, point1.position);
                    }

                    break;

                // for bezier and bspline, just iterate to estimate 
                case SplineMode.Bezier:
                case SplineMode.BSpline:

                    var quality = 256;
                    for (var s = 0; s < quality; ++s)
                    {
                        var t = (float)s / quality;
                        var delta = 1f / quality;

                        var point0 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t - delta);
                        var point1 = JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

                        length += Vector3.Distance(point0.position, point1.position);
                    }

                    break;
            }

            return length;
        }

        public static float JobSafe_ProjectDistance(NativeArray<float> projectionDistanceCache, float d)
        {
            // find out our closest d 
            var resolution = projectionDistanceCache.Length;

            for (var r = 1; r < resolution; ++r)
            {
                // one found, lerp 
                if (projectionDistanceCache[r] > d)
                {
                    var d0 = projectionDistanceCache[r - 1];
                    var d1 = projectionDistanceCache[r - 0];

                    var t0 = (float)(r - 1) / resolution;
                    var t1 = (float)(r - 0) / resolution;

                    var tt = Mathf.InverseLerp(d0, d1, d);
                    return Mathf.Lerp(t0, t1, tt);
                }
            }

            return 1.0f;
        }

        public static float JobSafe_ProjectPercentToDistance(NativeArray<float> projectionDistanceCache, float projectionDistanceCacheLength, float t)
        {
            var resolution = projectionDistanceCache.Length;

            for (var r = 1; r < resolution; ++r)
            {
                var d0 = projectionDistanceCache[r - 1];
                var d1 = projectionDistanceCache[r - 0];

                var t0 = (float)(r - 1) / resolution;
                var t1 = (float)(r - 0) / resolution;

                if (t1 > t)
                {
                    var tt = Mathf.InverseLerp(t0, t1, t);
                    var dt = Mathf.Lerp(d0, d1, tt);

                    return dt;
                }
            }

            return projectionDistanceCacheLength;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (EditorAlwaysDraw) return;
            DrawGizmos();
        }

        private void OnDrawGizmos()
        {
            if (!EditorAlwaysDraw) return;
            DrawGizmos();
        }

        private void DrawLinearGizmos()
        {
            for (var i = 1; i < Points.Length; ++i)
            {
                var previous = Points[i - 1];
                var current = Points[i];

                if (SplineSpace == Space.Self)
                {
                    previous = TransformSplinePoint(previous);
                    current = TransformSplinePoint(current);
                }

                if (EditorDrawThickness)
                {
                    var up = previous.rotation * Vector3.up;
                    var forward = (current.position - previous.position).normalized;
                    var right = Vector3.Cross(forward, up);

                    var previous_offset = right * previous.scale.x;
                    var current_offset = right * current.scale.x;

                    // between thickness bars 
                    Gizmos.DrawLine(previous.position - previous_offset, previous.position + previous_offset);
                    Gizmos.DrawLine(current.position - current_offset, current.position + current_offset);

                    // between points 
                    Gizmos.DrawLine(previous.position - previous_offset, current.position - current_offset);
                    Gizmos.DrawLine(previous.position + previous_offset, current.position + current_offset);
                }
                else
                {
                    Gizmos.DrawLine(previous.position, current.position);
                }
            }
        }

        public void DrawGizmos()
        {
            var selected = Selection.activeObject == gameObject;

            if (Mode == SplineMode.Linear)
            {
                DrawLinearGizmos(); 
            }
            else
            {
                int quality = 512;

                for (var r = 0; r <= quality; ++r)
                {

                    var t0 = (float)r / quality - (1f / quality);
                    var t1 = (float)r / quality;

                    var p0 = GetPoint(t0);
                    var p1 = GetPoint(t1);

                    if (EditorDrawThickness)
                    {
                        var forward = (p1.position - p0.position).normalized;

                        var up0 = p0.rotation * Vector3.forward;
                        var up1 = p1.rotation * Vector3.forward;

                        var right0 = Vector3.Cross(forward, up0);
                        var right1 = Vector3.Cross(forward, up1);

                        var previous_offset = right0 * p0.scale.x;
                        var current_offset = right1 * p1.scale.x;

                        // between thickness
                        Gizmos.DrawLine(p0.position - previous_offset, p0.position + previous_offset);
                        Gizmos.DrawLine(p1.position - current_offset, p1.position + current_offset);

                        // between points
                        Gizmos.DrawLine(p0.position - previous_offset, p1.position - current_offset);
                        Gizmos.DrawLine(p0.position + previous_offset, p1.position + current_offset);

                    }
                    else
                    {
                        Gizmos.DrawLine(p0.position, p1.position);
                    }

                    if(EditorDrawRotations)
                    {
                        var position = p0.position;
                        var rotation = p0.rotation;

                        var up = rotation * Vector3.up;
                        var forward = rotation * Vector3.forward;
                        var right = rotation * Vector3.right;

                        var distance = 0.25f;

                        Gizmos.color = Color.green;
                        Gizmos.DrawRay(position, up * distance);

                        Gizmos.color = Color.blue;
                        Gizmos.DrawRay(position, forward * distance);

                        Gizmos.color = Color.red;
                        Gizmos.DrawRay(position, right * distance);
                    }
                }

                if(Mode == SplineMode.BSpline && selected)
                {
                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 1f); 
                    DrawLinearGizmos(); 
                }
            }

            DrawPointRotationGizmos(); 
        }

        private void DrawPointRotationGizmos()
        {
            if(!EditorDrawRotations)
            {
                return;
            }

            for(var i = 0; i < Points.Length; ++i)
            {
                var splinePoint = Points[i];

                if(SplineSpace == Space.Self)
                {
                    splinePoint = TransformSplinePoint(splinePoint);
                }

                var position = splinePoint.position;
                var rotation = splinePoint.rotation;

                var up = rotation * Vector3.up;
                var forward = rotation * Vector3.forward;
                var right = rotation * Vector3.right;

                var distance = 1f;

                Gizmos.color = Color.green;
                Gizmos.DrawRay(position, up * distance);
                
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(position, forward * distance);
                
                Gizmos.color = Color.red;
                Gizmos.DrawRay(position, right * distance);
            }
        }

        public void DrawAsHandles()
        {
            if (Mode == SplineMode.Linear)
            {
                for (var i = 1; i < Points.Length; ++i)
                {
                    var previous = Points[i - 1];
                    var current = Points[i];

                    if (SplineSpace == Space.Self)
                    {
                        previous = TransformSplinePoint(previous);
                        current = TransformSplinePoint(current);
                    }

                    UnityEditor.Handles.DrawLine(previous.position, current.position);
                }
            }
            else
            {
                int quality = 512;

                for (var r = 0; r <= quality; ++r)
                {

                    var t0 = (float)r / quality - (1f / quality);
                    var t1 = (float)r / quality;

                    var p0 = GetPoint(t0);
                    var p1 = GetPoint(t1);

                    UnityEditor.Handles.DrawLine(p0.position, p1.position);
                }
            }
        }
#endif

    }
}

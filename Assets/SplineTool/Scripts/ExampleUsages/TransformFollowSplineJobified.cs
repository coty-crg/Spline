using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace CorgiSpline
{
    [DefaultExecutionOrder(-1)]
    public class TransformFollowSplineJobified : MonoBehaviour
    {
        [Header("References")]
        public Spline FollowSpline;
        public Transform[] Transforms;

        [Header("Settings")]
        public float FollowSpeed = 1f;
        public bool FollowRotation;
        public bool RandomOnStart;

        [System.NonSerialized] private TransformAccessArray _TransformsAccess;
        [System.NonSerialized] private JobHandle _previousJobHandle;
        [System.NonSerialized] private NativeArray<float> _transformDistances;

        private void OnEnable()
        {
            _TransformsAccess = new TransformAccessArray(Transforms);
            _transformDistances = new NativeArray<float>(_TransformsAccess.length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for(var t = 0; t < _transformDistances.Length; ++t)
            {
                _transformDistances[0] = 0f; 
            }
        }

        private void OnDisable()
        {
            _previousJobHandle.Complete(); 
            _TransformsAccess.Dispose();
            _transformDistances.Dispose();
        }

        private void Start()
        {
            var distance = FollowSpline.UpdateDistanceProjectionsData();
            FollowSpline.UpdateNative(); 

            if (RandomOnStart)
            {
                UpdateTransformAccessArray();

                var job = new TransformInitializeRandomScatter()
                {
                    Distances = _transformDistances,
                    DistanceCacheLength = distance,
                    
                    Points = FollowSpline.NativePoints,
                    DistanceCache = FollowSpline.NativeDistanceCache,
                    Mode = FollowSpline.GetSplineMode(),
                    SplineSpace = FollowSpline.GetSplineSpace(),
                    localToWorldMatrix = FollowSpline.transform.localToWorldMatrix,
                    worldToLocalMatrix = FollowSpline.transform.worldToLocalMatrix,
                    ClosedSpline = FollowSpline.GetSplineClosed(),
                };

                var handle = job.Schedule(_TransformsAccess);
                handle.Complete();
            }
        }

        private void UpdateTransformAccessArray()
        {
            _TransformsAccess.Dispose();
            _TransformsAccess = new TransformAccessArray(Transforms, Environment.ProcessorCount);
        }

        private void Update()
        {
            _previousJobHandle.Complete();

            var distance = FollowSpline.UpdateDistanceProjectionsData();
            FollowSpline.UpdateNative();

            UpdateTransformAccessArray();

            var job = new TransformFollowSplineJob()
            {
                Distances = _transformDistances,
                FollowSpeed = FollowSpeed,
                FollowRotation = FollowRotation,
                deltaTime = Time.deltaTime,
                
                Points = FollowSpline.NativePoints,
                DistanceCache = FollowSpline.NativeDistanceCache,
                DistanceCacheLength = distance,

                Mode = FollowSpline.GetSplineMode(),
                SplineSpace = FollowSpline.GetSplineSpace(),
                localToWorldMatrix = FollowSpline.transform.localToWorldMatrix,
                worldToLocalMatrix = FollowSpline.transform.worldToLocalMatrix,
                ClosedSpline = FollowSpline.GetSplineClosed(),
            };

            _previousJobHandle = job.Schedule(_TransformsAccess);
        }

        private void LateUpdate()
        {
            _previousJobHandle.Complete();
        }

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
        private struct TransformFollowSplineJob : IJobParallelForTransform
        {
            // settings 
            public NativeArray<float> Distances;
            public float FollowSpeed;
            public bool FollowRotation;
            public float deltaTime;

            // Spline data
            [ReadOnly] public NativeArray<SplinePoint> Points;
            [ReadOnly] public NativeArray<float> DistanceCache;
            [ReadOnly] public float DistanceCacheLength;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 localToWorldMatrix;
            public Matrix4x4 worldToLocalMatrix;
            public bool ClosedSpline;

            public void Execute(int index, TransformAccess transform)
            {
                var d = Distances[index];

                d += FollowSpeed * deltaTime;
                d = Mathf.Repeat(d, DistanceCacheLength); 

                Distances[index] = d;

                var t = Spline.JobSafe_ProjectDistance(DistanceCache, d);
                var projectedPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

                transform.position = projectedPoint.position;

                if (FollowRotation)
                {
                    transform.rotation = projectedPoint.rotation;
                }
            }
        }

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
        private struct TransformInitializeRandomScatter : IJobParallelForTransform
        {
            public NativeArray<float> Distances;
            public float DistanceCacheLength;

            // Spline data
            [ReadOnly] public NativeArray<float> DistanceCache;
            [ReadOnly] public NativeArray<SplinePoint> Points;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 localToWorldMatrix;
            public Matrix4x4 worldToLocalMatrix;
            public bool ClosedSpline;

            public void Execute(int index, TransformAccess transform)
            {
                var seed = (uint)(index + 10000) * 100000;
                var random = new Unity.Mathematics.Random(seed);
                var t = random.NextFloat();

                var point = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                transform.position = point.position;

                var d = Spline.JobSafe_ProjectPercentToDistance(DistanceCache, DistanceCacheLength, t);
                Distances[index] = d; 
            }
        }
    }
}

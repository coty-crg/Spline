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
    public class TransformFollowSplineJobified : MonoBehaviour
    {
        public Spline FollowSpline;
        public float FollowSpeed = 1f;
        public bool FollowRotation;

        public bool RandomOnStart;
        public Transform[] Transforms;

        private TransformAccessArray _TransformsAccess;

        private void OnEnable()
        {
            _TransformsAccess = new TransformAccessArray(Transforms);

        }

        private void OnDisable()
        {
            _TransformsAccess.Dispose();
        }

        private void Start()
        {
            if (RandomOnStart)
            {
                UpdateTransformAccessArray();

                var job = new TransformInitializeRandomScatter()
                {
                    Points = FollowSpline.NativePoints,
                    Mode = FollowSpline.GetSplineMode(),
                    SplineSpace = FollowSpline.GetSplineSpace(),
                    localToWorldMatrix = FollowSpline.transform.localToWorldMatrix,
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
            UpdateTransformAccessArray();

            var job = new TransformFollowSplineJob()
            {
                FollowSpeed = FollowSpeed,
                FollowRotation = FollowRotation,

                deltaTime = Time.deltaTime,

                Points = FollowSpline.NativePoints,
                Mode = FollowSpline.GetSplineMode(),
                SplineSpace = FollowSpline.GetSplineSpace(),
                localToWorldMatrix = FollowSpline.transform.localToWorldMatrix,
                ClosedSpline = FollowSpline.GetSplineClosed(),
            };

            var handle = job.Schedule(_TransformsAccess);
            //handle.Complete(); 
        }

        [BurstCompile]
        private struct TransformFollowSplineJob : IJobParallelForTransform
        {
            // settings 
            public float FollowSpeed;
            public bool FollowRotation;
            public float deltaTime;

            // Spline data
            [ReadOnly] public NativeArray<SplinePoint> Points;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 localToWorldMatrix;
            public bool ClosedSpline;

            public void Execute(int index, TransformAccess transform)
            {
                var t = Spline.JobSafe_ProjectOnSpline_t(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, transform.position);

                var projectedForward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                var projectedPoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

                projectedPoint.position += projectedForward * (FollowSpeed * deltaTime);

                transform.position = projectedPoint.position;

                if (FollowRotation)
                {
                    var up = projectedPoint.rotation * Vector3.forward;
                    transform.rotation = Quaternion.LookRotation(projectedForward, up);
                }
            }
        }

        [BurstCompile]
        private struct TransformInitializeRandomScatter : IJobParallelForTransform
        {
            // Spline data
            [ReadOnly] public NativeArray<SplinePoint> Points;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 localToWorldMatrix;
            public bool ClosedSpline;

            public void Execute(int index, TransformAccess transform)
            {
                var seed = (uint)(index + 10000) * 100000;
                var random = new Unity.Mathematics.Random(seed);
                var t = random.NextFloat();

                var point = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);
                transform.position = point.position;
            }
        }
    }
}

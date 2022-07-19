using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace CorgiSpline
{
    public class RigidbodyFollowSplineJobified : MonoBehaviour
    {
        public Spline FollowSpline;

        public bool FollowPosition;
        public bool FollowVelocity;
        public bool FollowRotation;

        public Rigidbody[] Rigidbodies;

        public bool RandomStart;
        public float RandomMinSpeed;
        public float RandomMaxSpeed;

        private struct RigidbodyJobData
        {
            public Vector3 position;
            public Vector3 velocity;
            public Quaternion rotation;
        }

        private NativeArray<RigidbodyJobData> _rigidbodyData;

        private void OnEnable()
        {
            _rigidbodyData = new NativeArray<RigidbodyJobData>(Rigidbodies.Length, Allocator.Persistent);
        }

        private void OnDisable()
        {
            _rigidbodyData.Dispose();
        }

        private void Start()
        {
            if (RandomStart)
            {
                UpdateNativeArray();

                var job = new RigidbodyInitializeRandomScatter()
                {
                    RigidBodies = _rigidbodyData,

                    RandomMinSpeed = RandomMinSpeed,
                    RandomMaxSpeed = RandomMaxSpeed,

                    Points = FollowSpline.NativePoints,
                    Mode = FollowSpline.GetSplineMode(),
                    SplineSpace = FollowSpline.GetSplineSpace(),
                    localToWorldMatrix = FollowSpline.transform.localToWorldMatrix,
                    worldToLocalMatrix = FollowSpline.transform.worldToLocalMatrix,
                    ClosedSpline = FollowSpline.GetSplineClosed(),
                };

                var count = Rigidbodies.Length;
                var handle = job.Schedule(count, 32);
                handle.Complete();

                ReadbackNativeArray();
            }
        }

        private void UpdateNativeArray()
        {
            var count = Rigidbodies.Length;
            if (_rigidbodyData.Length < count)
            {
                _rigidbodyData.Dispose();
                _rigidbodyData = new NativeArray<RigidbodyJobData>(count, Allocator.Persistent);
            }

            for (var i = 0; i < count; ++i)
            {
                var rb = Rigidbodies[i];

                var data = new RigidbodyJobData()
                {
                    position = rb.position,
                    rotation = rb.rotation,
                    velocity = rb.velocity,
                };

                _rigidbodyData[i] = data;
            }
        }

        private void ReadbackNativeArray()
        {
            var count = Rigidbodies.Length;

            for (var i = 0; i < count; ++i)
            {
                var data = _rigidbodyData[i];
                var rb = Rigidbodies[i];

                rb.position = data.position;
                rb.velocity = data.velocity;
                rb.rotation = data.rotation;
            }
        }

        private void Update()
        {
            UpdateNativeArray();

            var job = new RigidbodyFollowSpline()
            {

                RigidBodies = _rigidbodyData,

                FollowPosition = FollowPosition,
                FollowRotation = FollowRotation,
                FollowVelocity = FollowVelocity,

                Points = FollowSpline.NativePoints,
                Mode = FollowSpline.GetSplineMode(),
                SplineSpace = FollowSpline.GetSplineSpace(),
                localToWorldMatrix = FollowSpline.transform.localToWorldMatrix,
                worldToLocalMatrix = FollowSpline.transform.worldToLocalMatrix,
                ClosedSpline = FollowSpline.GetSplineClosed(),
            };

            var count = Rigidbodies.Length;
            var handle = job.Schedule(count, 32);
            handle.Complete();

            ReadbackNativeArray();
        }

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
        private struct RigidbodyFollowSpline : IJobParallelFor
        {
            // data 
            public NativeArray<RigidbodyJobData> RigidBodies;

            // settings 
            public bool FollowPosition;
            public bool FollowVelocity;
            public bool FollowRotation;

            // Spline data
            [ReadOnly] public NativeArray<SplinePoint> Points;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 localToWorldMatrix;
            public Matrix4x4 worldToLocalMatrix;
            public bool ClosedSpline;

            public void Execute(int index)
            {
                var rb = RigidBodies[index];

                var t = Spline.JobSafe_ProjectOnSpline_t(Points, Mode, SplineSpace, worldToLocalMatrix, ClosedSpline, rb.position);
                var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, 0);
                var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);

                if (FollowPosition)
                {
                    rb.position = splinePoint.position;
                }

                if (FollowVelocity)
                {
                    var newDirection = Spline.VectorProject(rb.velocity, forward);
                    rb.velocity = newDirection.normalized * rb.velocity.magnitude;
                }

                if (FollowRotation)
                {
                    var up = splinePoint.rotation * Vector3.forward;
                    rb.rotation = Quaternion.LookRotation(forward, up);
                }

                RigidBodies[index] = rb;
            }
        }

#if CORGI_DETECTED_BURST
        [BurstCompile]
#endif
        private struct RigidbodyInitializeRandomScatter : IJobParallelFor
        {
            // data 
            public NativeArray<RigidbodyJobData> RigidBodies;

            // settings 
            public float RandomMinSpeed;
            public float RandomMaxSpeed;

            // Spline data
            [ReadOnly] public NativeArray<SplinePoint> Points;
            public SplineMode Mode;
            public Space SplineSpace;
            public Matrix4x4 localToWorldMatrix;
            public Matrix4x4 worldToLocalMatrix;
            public bool ClosedSpline; 

            public void Execute(int index)
            {
                var rb = RigidBodies[index];

                var seed = (uint)(index + 10000) * 100000;
                var random = new Unity.Mathematics.Random(seed);
                var t = random.NextFloat();

                var point = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, ClosedSpline, t);


                var offset_x = random.NextFloat(RandomMinSpeed, RandomMaxSpeed) * 0.1f;
                var offset_z = random.NextFloat(RandomMinSpeed, RandomMaxSpeed) * 0.1f;

                rb.position = point.position + new Vector3(offset_x, 0f, offset_z);

                // speed 
                var speed_x = random.NextFloat(RandomMinSpeed, RandomMaxSpeed);
                var speed_y = random.NextFloat(RandomMinSpeed, RandomMaxSpeed);
                var speed_z = random.NextFloat(RandomMinSpeed, RandomMaxSpeed);
                rb.velocity = new Vector3(speed_x, speed_y, speed_z);

                RigidBodies[index] = rb;
            }
        }
    }
}

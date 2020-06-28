using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[ExecuteInEditMode]
public class ParticleFollowSpline : MonoBehaviour
{
    public ParticleSystem TargetParticleSystem;
    public Spline TargetSpline;

    public bool FollowPosition;
    public bool FollowVelocity;
    public bool FollowRotation;

    private NativeArray<ParticleSystem.Particle> _particleCache;

    private void OnEnable()
    {
        if(TargetParticleSystem != null)
        {
            EnsureParticleCache(); 
        }
    }

    private void OnDisable()
    {
        if(_particleCache.IsCreated)
        {
            _particleCache.Dispose();
        }
    }

    private void EnsureParticleCache()
    {
        var max_particles = TargetParticleSystem.main.maxParticles;
        if (!_particleCache.IsCreated || _particleCache.Length < max_particles)
        {
            if(_particleCache.IsCreated)
            {
                _particleCache.Dispose();
            }

            _particleCache = new NativeArray<ParticleSystem.Particle>(max_particles, Allocator.Persistent); 
        }
    }

    private void LateUpdate()
    {
        if (!FollowPosition && !FollowVelocity && !FollowRotation) return;
        if (TargetParticleSystem == null) return;
        if (TargetSpline == null) return;

        EnsureParticleCache();
        UpdateEditor(); 
    }

    private void UpdateEditor()
    { 
        var particle_count = TargetParticleSystem.GetParticles(_particleCache);

        var mainModule = TargetParticleSystem.main;
        mainModule.simulationSpace = ParticleSystemSimulationSpace.World;

        var job = new ParticlesSplineFollow()
        {
            Particles = _particleCache,

            Points = TargetSpline.NativePoints,
            Mode = TargetSpline.GetSplineMode(),
            SplineSpace = TargetSpline.GetSplineSpace(),
            localToWorldMatrix = TargetSpline.transform.localToWorldMatrix,

            FollowPosition = FollowPosition,
            FollowVelocity = FollowVelocity,
            FollowRotation = FollowRotation,
        };

        var handle = job.Schedule(particle_count, 64);
        handle.Complete();

        TargetParticleSystem.SetParticles(_particleCache, particle_count);
    }

    [BurstCompile]
    private struct ParticlesSplineFollow : IJobParallelFor
    {
        // Particle System data 
        public NativeArray<ParticleSystem.Particle> Particles;
            
        // Spline data
        [ReadOnly] public NativeArray<SplinePoint> Points;
        public SplineMode Mode;
        public Space SplineSpace;
        public Matrix4x4 localToWorldMatrix;

        // Component data
        public bool FollowPosition;
        public bool FollowVelocity;
        public bool FollowRotation;

        public void Execute(int index)
        {
            var particle = Particles[index];

            var t = Spline.JobSafe_ProjectOnSpline_t(Points, Mode, SplineSpace, localToWorldMatrix, particle.position);
            var splinePoint = Spline.JobSafe_GetPoint(Points, Mode, SplineSpace, localToWorldMatrix, 0);

            if (FollowPosition)
            {
                particle.position = splinePoint.position;
            }
            
            if (FollowVelocity)
            {
                var forward = Spline.JobSafe_GetForward(Points, Mode, SplineSpace, localToWorldMatrix, t);
                particle.velocity = Spline.VectorProject(particle.velocity, forward);
            }
            
            if (FollowRotation)
            {
                particle.rotation3D = splinePoint.rotation.eulerAngles;
            }

            Particles[index] = particle;
        }
    }


}

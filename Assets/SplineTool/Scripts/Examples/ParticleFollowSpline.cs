using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class ParticleFollowSpline : MonoBehaviour
{
    public ParticleSystem TargetParticleSystem;
    public Spline TargetSpline;

    public bool FollowPosition;
    public bool FollowVelocity;
    public bool FollowRotation;

    private ParticleSystem.Particle[] _particleCache;

    private void EnsureParticleCache()
    {
        var max_particles = TargetParticleSystem.main.maxParticles;
        if (_particleCache == null || _particleCache.Length < max_particles)
        {
            _particleCache = new ParticleSystem.Particle[max_particles];
        }
    }

    private void LateUpdate()
    {
        if (!FollowPosition && !FollowVelocity && !FollowRotation) return;
        if (TargetParticleSystem == null) return;
        if (TargetSpline == null) return;

        EnsureParticleCache();
        var particle_count = TargetParticleSystem.GetParticles(_particleCache);

        var mainModule = TargetParticleSystem.main;
        mainModule.simulationSpace = ParticleSystemSimulationSpace.World;

        for (var i = 0; i < particle_count; ++i)
        {
            var particle = _particleCache[i];

            var t = TargetSpline.ProjectOnSpline_t(particle.position);
            var splinePoint = TargetSpline.GetPoint(t);

            if(FollowPosition)
            {
                particle.position = splinePoint.position;
            }

            if(FollowVelocity)
            {
                var forward = TargetSpline.GetForward(t);
                particle.velocity = Vector3.Project(particle.velocity, forward); 
            }

            if(FollowRotation)
            {
                particle.rotation3D = splinePoint.rotation.eulerAngles;
            }

            _particleCache[i] = particle; 
        }

        TargetParticleSystem.SetParticles(_particleCache, particle_count); 
    }

}

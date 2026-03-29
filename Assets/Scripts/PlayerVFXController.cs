using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Fragsurf.Movement {

    public class PlayerVFXController : MonoBehaviour {

        [Header("References")]
        [SerializeField] private SurfCharacter surfCharacter;

        [Header("Slide Trail Settings")]
        [SerializeField] private TrailRenderer slideTrailRenderer;
        [SerializeField] private Vector3 trailOffset = new Vector3(0, 0.1f, 0);

        [Header("Dash Burst Settings")]
        [SerializeField] private ParticleSystem dashBurstPrefab;
        [SerializeField] private int dashBurstCount = 20;
        private List<ParticleSystem> dashPool = new List<ParticleSystem>();

        [Header("Double Jump Settings")]
        [SerializeField] private ParticleSystem doubleJumpPrefab;
        [SerializeField] private int doubleJumpBurstCount = 15;
        private List<ParticleSystem> jumpPool = new List<ParticleSystem>();

        [Header("Melee Fire Settings")]
        [SerializeField] private ParticleSystem meleeFireParticles;
        [SerializeField] private Transform meleeWeaponTransform;
        private List<ParticleSystem> meleePool = new List<ParticleSystem>();
        private ParticleSystem currentMeleeParticles;

        private bool IsSliding => surfCharacter.moveData.sliding && surfCharacter.moveData.slidingEnabled;

        private void Start() {
            if (surfCharacter == null) surfCharacter = GetComponent<SurfCharacter>();

            if (slideTrailRenderer != null) {
                slideTrailRenderer.emitting = false;
            }
        }

        private void Update() {
            UpdateSlideTrail();
        }

        private void UpdateSlideTrail() {
            if (slideTrailRenderer == null) return;

            bool sliding = IsSliding;
            slideTrailRenderer.emitting = sliding;

            if (sliding) {
                // Raycast down to find exact ground point for the trail
                Vector3 rayStart = surfCharacter.transform.position + Vector3.up * 0.1f;
                LayerMask groundMask = SurfPhysics.groundLayerMask;
                
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2f, groundMask, QueryTriggerInteraction.Ignore)) {
                    // Snap to surface with a tiny offset to prevent clipping
                    slideTrailRenderer.transform.position = hit.point + hit.normal * 0.02f;
                    
                    // Align with surface normal and movement direction
                    Vector3 forward = surfCharacter.moveData.velocity.normalized;
                    if (forward.sqrMagnitude < 0.001f) forward = surfCharacter.transform.forward;
                    slideTrailRenderer.transform.rotation = Quaternion.LookRotation(forward, hit.normal);
                } else {
                    // Fallback if raycast fails
                    Vector3 spawnPos = surfCharacter.lowerVfxSpawnPoint != null ? surfCharacter.lowerVfxSpawnPoint.position : surfCharacter.transform.position + trailOffset;
                    slideTrailRenderer.transform.position = spawnPos;
                    slideTrailRenderer.transform.rotation = Quaternion.Euler(-90, 0, 0);
                }
            }
        }

        public void OnMeleeStart() {
            if (meleeFireParticles == null) return;
            
            currentMeleeParticles = GetFromPool(meleePool, meleeFireParticles);
            if (meleeWeaponTransform != null) {
                currentMeleeParticles.transform.SetParent(meleeWeaponTransform);
                currentMeleeParticles.transform.localPosition = Vector3.zero;
                currentMeleeParticles.transform.localRotation = Quaternion.identity;
            } else {
                currentMeleeParticles.transform.position = transform.position;
                currentMeleeParticles.transform.rotation = transform.rotation;
            }

            if (!currentMeleeParticles.isPlaying) {
                currentMeleeParticles.Play();
            }
        }

        public void OnMeleeEnd() {
            if (currentMeleeParticles != null && currentMeleeParticles.isPlaying) {
                currentMeleeParticles.Stop();
                currentMeleeParticles = null;
            }
        }

        public void OnDash(Vector3 dashDirection) {
            if (dashBurstPrefab == null) return;

            ParticleSystem ps = GetFromPool(dashPool, dashBurstPrefab);
            
            Vector3 spawnPos = surfCharacter.lowerVfxSpawnPoint != null ? surfCharacter.lowerVfxSpawnPoint.position : surfCharacter.transform.position;
            ps.transform.position = spawnPos;
            ps.transform.rotation = Quaternion.LookRotation(-dashDirection);
            
            ps.Emit(dashBurstCount);
        }

        public void OnDoubleJump() {
            if (doubleJumpPrefab == null) return;

            ParticleSystem ps = GetFromPool(jumpPool, doubleJumpPrefab);

            Vector3 spawnPos = surfCharacter.lowerVfxSpawnPoint != null ? surfCharacter.lowerVfxSpawnPoint.position : surfCharacter.transform.position;
            ps.transform.position = spawnPos;
            
            ps.Emit(doubleJumpBurstCount);
        }

        private ParticleSystem GetFromPool(List<ParticleSystem> pool, ParticleSystem prefab) {
            // Find an available system in the pool
            for (int i = 0; i < pool.Count; i++) {
                if (pool[i] != null && !pool[i].isEmitting && pool[i].particleCount == 0) {
                    return pool[i];
                }
            }

            // If none found, create a new one (expand pool)
            ParticleSystem newPs = Instantiate(prefab, transform);
            
            // Ensure simulation space is world so particles stay put when player moves
            var main = newPs.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            
            pool.Add(newPs);
            return newPs;
        }
    }
}

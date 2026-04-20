using UnityEngine;
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

        private MoveData _cachedState;
        private bool _hasCachedState;

        private bool IsSliding => _hasCachedState && _cachedState != null && _cachedState.sliding && _cachedState.slidingEnabled;

        private void Start() {
            if (surfCharacter == null) surfCharacter = GetComponent<SurfCharacter>();

            if (slideTrailRenderer != null) {
                slideTrailRenderer.emitting = false;
            }
        }

        private void Update() {
            UpdateSlideTrail();
        }

        /// <summary>
        /// Presentation-only snapshot from the simulation layer.
        /// The VFX system reads this data, but never writes back into gameplay state.
        /// </summary>
        public void ApplyState(MoveData state, MoveData prevState, bool isNewTick) {
            _cachedState = state;
            _hasCachedState = state != null;

            if (slideTrailRenderer != null && isNewTick) {
                slideTrailRenderer.emitting = IsSliding;
            }
        }

        private void UpdateSlideTrail() {
            if (slideTrailRenderer == null) return;

            bool sliding = IsSliding;
            slideTrailRenderer.emitting = sliding;

            if (sliding) {
                Vector3 spawnPos = surfCharacter != null && surfCharacter.lowerVfxSpawnPoint != null
                    ? surfCharacter.lowerVfxSpawnPoint.position
                    : GetPresentationOrigin() + trailOffset;

                slideTrailRenderer.transform.position = spawnPos;

                Vector3 forward = GetPresentationForward();
                if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
                slideTrailRenderer.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
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

        private Vector3 GetPresentationOrigin() {
            if (_hasCachedState && _cachedState != null)
                return _cachedState.origin;

            if (surfCharacter != null)
                return surfCharacter.transform.position;

            return transform.position;
        }

        private Vector3 GetPresentationForward() {
            if (_hasCachedState && _cachedState != null) {
                Vector3 forward = new Vector3(_cachedState.velocity.x, 0f, _cachedState.velocity.z);
                if (forward.sqrMagnitude >= 0.001f)
                    return forward.normalized;

                return Quaternion.Euler(0f, _cachedState.viewAngles.y, 0f) * Vector3.forward;
            }

            if (surfCharacter != null)
                return surfCharacter.transform.forward;

            return transform.forward;
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

using UnityEngine;
using System.Collections.Generic;

namespace Fragsurf.Movement {

    public class PlayerVFXController : MonoBehaviour {

        [System.Flags]
        private enum MeleePlasmaActiveState {
            Charging = 1 << 0,
            Lunging = 1 << 1,
            Recovery = 1 << 2
        }

        [Header("References")]
        [SerializeField] private SurfCharacter surfCharacter;

        [Header("Slide Trail Settings")]
        [SerializeField] private TrailRenderer slideTrailRenderer;
        [SerializeField] private Vector3 trailOffset = new Vector3(0f, -0.05f, 0f);
        [Min(0.01f)]
        [SerializeField] private float slideTrailLifetime = 0.22f;
        [Min(0f)]
        [SerializeField] private float slideTrailWidthMultiplier = 1.15f;
        [Range(0f, 1f)]
        [SerializeField] private float slideTrailStartAlpha = 0.2f;
        [Range(0f, 1f)]
        [SerializeField] private float slideTrailMidAlpha = 0.1f;

        [Header("Dash Burst Settings")]
        [SerializeField] private ParticleSystem dashBurstPrefab;
        [SerializeField] private int dashBurstCount = 20;
        private List<ParticleSystem> dashPool = new List<ParticleSystem>();

        [Header("Dash Line Settings")]
        [SerializeField] private bool spawnDashLines = true;
        [SerializeField] private Material dashLineMaterial;
        [SerializeField] private Vector2Int dashLineCountRange = new Vector2Int(4, 6);
        [SerializeField] private Color dashLineColor = Color.white;
        [SerializeField] private Vector2 dashLineLengthRange = new Vector2(2.2f, 4.2f);
        [SerializeField] private Vector2 dashLineWidthRange = new Vector2(0.08f, 0.14f);
        [SerializeField] private float dashLineLifetime = 0.28f;
        [SerializeField] private float dashLineHeight = 1f;
        [SerializeField] private float dashLineBehindDistance = 0.9f;
        [SerializeField] private float dashLineSideSpread = 0.75f;
        [SerializeField] private float dashLineVerticalSpread = 0.4f;
        private readonly List<DashLineEffect> dashLinePool = new List<DashLineEffect>();
        private Material _defaultDashLineMaterial;
        private bool _warnedMissingDashLineMaterial;

        [Header("Cloud Poof Settings")]
        [SerializeField] private CloudPoofEffect cloudPoofPrefab;
        [SerializeField] private float dashPoofBehindDistance = 0.9f;
        [SerializeField] private float jumpPoofUnderDistance = 0.8f;
        private readonly List<CloudPoofEffect> cloudPoofPool = new List<CloudPoofEffect>();

        [Header("Double Jump Settings")]
        [SerializeField] private ParticleSystem doubleJumpPrefab;
        [SerializeField] private int doubleJumpBurstCount = 15;
        private List<ParticleSystem> jumpPool = new List<ParticleSystem>();

        [Header("Melee Fire Settings")]
        [SerializeField] private ParticleSystem meleeFireParticles;
        [SerializeField] private Transform meleeWeaponTransform;
        private List<ParticleSystem> meleePool = new List<ParticleSystem>();
        private ParticleSystem currentMeleeParticles;

        [Header("Melee Model VFX Settings")]
        [Tooltip("Prefab spawned around the melee weapon while the selected melee state is active.")]
        [SerializeField] private GameObject meleePlasmaPrefab;
        [Tooltip("Optional parent for the spawned prefab. If empty, Melee Weapon Transform is used, then this object.")]
        [SerializeField] private Transform meleePlasmaParentOverride;
        [SerializeField] private bool meleePlasmaFollowParent = true;
        [SerializeField] private MeleePlasmaActiveState meleePlasmaActiveStates = MeleePlasmaActiveState.Charging | MeleePlasmaActiveState.Lunging;
        [SerializeField] private Vector3 meleePlasmaLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 meleePlasmaLocalRotation = Vector3.zero;
        [SerializeField] private Vector3 meleePlasmaLocalScale = Vector3.one;
        [Min(0f)]
        [SerializeField] private float meleePlasmaStartScaleMultiplier = 0.2f;
        [SerializeField] private Vector3 meleePlasmaRotationSpeed = Vector3.zero;
        [Min(0f)]
        [SerializeField] private float meleePlasmaPhaseInDuration = 0.04f;
        [Min(0f)]
        [SerializeField] private float meleePlasmaPhaseOutDuration = 0.1f;
        [Min(0.01f)]
        [SerializeField] private float meleePlasmaFallbackDuration = 0.35f;
        [SerializeField] private bool logMeleePlasmaDebug;
        private MeleePlasmaEffect _meleePlasmaInstance;
        private bool _meleePlasmaVfxActive;
        private MoveData.MeleeState _meleePlasmaCurrentState = MoveData.MeleeState.None;

        [Header("Parry Shield Settings")]
        [SerializeField] private bool logParryShieldDebug = true;
        [Tooltip("Prefab spawned when the parry window starts. It can be any GameObject with Renderer components.")]
        [SerializeField] private GameObject parryShieldPrefab;
        [SerializeField] private bool parryShieldFollowPlayer = true;
        [Tooltip("Seconds spent fading the parry prefab in. This is clamped to the actual parry duration.")]
        [Min(0f)]
        [SerializeField] private float parryShieldPhaseInDuration = 0.12f;
        [Tooltip("Seconds spent fading the parry prefab out after the functional parry window ends.")]
        [Min(0f)]
        [SerializeField] private float parryShieldPhaseOutDuration = 0.12f;
        [SerializeField] private Vector3 parryShieldCenterOffset = new Vector3(0f, 1.1f, 0f);
        [SerializeField] private Vector3 parryShieldRotationOffset = Vector3.zero;
        [SerializeField] private Vector3 parryShieldScale = Vector3.one;
        [SerializeField] private float parryShieldStartScaleMultiplier = 0.2f;
        private ParryShieldEffect _parryShieldInstance;
        private bool _parryShieldVfxActive;

        private MoveData _cachedState;
        private bool _hasCachedState;
        private bool _wasSlideTrailEmitting;
        private Vector3 _lastSlideTrailTargetPosition;
        private bool _hasLastSlideTrailTargetPosition;

        private bool IsSliding => _hasCachedState && _cachedState != null && _cachedState.sliding && _cachedState.slidingEnabled;

        private void Start() {
            if (surfCharacter == null) surfCharacter = GetComponent<SurfCharacter>();

            if (slideTrailRenderer != null) {
                // Prevent the trail GameObject from being destroyed when emission stops.
                slideTrailRenderer.autodestruct = false;
                slideTrailRenderer.emitting = false;
                ConfigureSlideTrailRenderer();
                slideTrailRenderer.Clear();
            }
        }

        private void Update() {
            UpdateSlideTrail();
        }

        private void OnDisable() {
            if (currentMeleeParticles != null && currentMeleeParticles.isPlaying) {
                currentMeleeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                currentMeleeParticles = null;
            }

            EndMeleePlasma(true);
        }

        /// <summary>
        /// Presentation-only snapshot from the simulation layer.
        /// The VFX system reads this data, but never writes back into gameplay state.
        /// </summary>
        public void ApplyState(MoveData state, MoveData prevState, bool isNewTick) {
            _cachedState = state;
            _hasCachedState = state != null;

            if (slideTrailRenderer != null && isNewTick)
                UpdateSlideTrail();

            UpdateMeleeFire(state);

            if (_parryShieldVfxActive && state != null && !state.isParrying) {
                EndParryShields();
            }

            UpdateMeleePlasma(state);
        }

        private void UpdateSlideTrail() {
            if (slideTrailRenderer == null) return;

            bool sliding = IsSliding;

            if (sliding) {
                Vector3 targetPos = GetPlayerRootPosition() + trailOffset;
                Vector3 spawnPos = _hasLastSlideTrailTargetPosition ? _lastSlideTrailTargetPosition : targetPos;

                slideTrailRenderer.transform.position = spawnPos;
                _lastSlideTrailTargetPosition = targetPos;
                _hasLastSlideTrailTargetPosition = true;

                if (!_wasSlideTrailEmitting) {
                    slideTrailRenderer.Clear();
                }
            } else {
                _hasLastSlideTrailTargetPosition = false;
            }

            slideTrailRenderer.emitting = sliding;
            _wasSlideTrailEmitting = sliding;
        }

        private void ConfigureSlideTrailRenderer() {
            if (slideTrailRenderer == null)
                return;

            slideTrailRenderer.time = slideTrailLifetime;
            slideTrailRenderer.widthMultiplier = slideTrailWidthMultiplier;

            Gradient source = slideTrailRenderer.colorGradient;
            GradientColorKey[] colorKeys = source != null && source.colorKeys != null && source.colorKeys.Length > 0
                ? source.colorKeys
                : new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };

            GradientAlphaKey[] alphaKeys = new[] {
                new GradientAlphaKey(Mathf.Clamp01(slideTrailStartAlpha), 0f),
                new GradientAlphaKey(Mathf.Clamp01(slideTrailMidAlpha), 0.65f),
                new GradientAlphaKey(0f, 1f)
            };

            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            slideTrailRenderer.colorGradient = gradient;
        }

        public void OnMeleeStart() {
            // Melee fire is now state-driven by UpdateMeleeFire and only plays while lunging.
        }

        public void OnMeleeEnd() {
            StopMeleeFire();
            EndMeleePlasma(false);
        }

        private void UpdateMeleeFire(MoveData state) {
            bool shouldPlay = state != null &&
                              state.moveType == MoveType.HeavyMelee &&
                              state.meleeState == MoveData.MeleeState.Lunging;

            if (shouldPlay) {
                StartMeleeFire();
            } else {
                StopMeleeFire();
            }
        }

        private void StartMeleeFire() {
            if (meleeFireParticles == null) return;

            if (currentMeleeParticles != null && currentMeleeParticles.isPlaying)
                return;

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

        private void StopMeleeFire() {
            if (currentMeleeParticles != null && currentMeleeParticles.isPlaying) {
                currentMeleeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                currentMeleeParticles = null;
            }
        }

        public void OnDash(Vector3 dashDirection) {
            Vector3 flatDashDirection = new Vector3(dashDirection.x, 0f, dashDirection.z);
            if (flatDashDirection.sqrMagnitude < 0.001f)
                flatDashDirection = GetPresentationForward();
            flatDashDirection.y = 0f;
            if (flatDashDirection.sqrMagnitude < 0.001f)
                flatDashDirection = transform.forward;
            flatDashDirection.y = 0f;
            if (flatDashDirection.sqrMagnitude < 0.001f)
                flatDashDirection = Vector3.forward;
            flatDashDirection.Normalize();

            Vector3 spawnPos = GetLowerVfxPosition() - flatDashDirection * dashPoofBehindDistance;
            Quaternion spawnRotation = Quaternion.LookRotation(-flatDashDirection, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            if (dashBurstPrefab != null) {
                ParticleSystem ps = GetFromPool(dashPool, dashBurstPrefab);
                ps.transform.position = spawnPos;
                ps.transform.rotation = spawnRotation;
                ps.Emit(dashBurstCount);
            }

            SpawnDashLines(GetFlatMomentumDirection(flatDashDirection));
        }

        public void OnDoubleJump() {
            Vector3 spawnPos = GetPlayerRootPosition() + Vector3.down * jumpPoofUnderDistance;
            Quaternion spawnRotation = Quaternion.identity;

            if (cloudPoofPrefab != null) {
                GetCloudPoofFromPool().Play(spawnPos, spawnRotation);
                return;
            }

            if (doubleJumpPrefab != null) {
                ParticleSystem ps = GetFromPool(jumpPool, doubleJumpPrefab);
                ps.transform.position = spawnPos;
                ps.Emit(doubleJumpBurstCount);
            }
        }

        public void OnParry(float activeDuration) {
            if (logParryShieldDebug) {
                Debug.Log($"[PlayerVFXController] OnParry received. activeDuration={activeDuration:0.000}, prefab={(parryShieldPrefab != null ? parryShieldPrefab.name : "NULL")}, followPlayer={parryShieldFollowPlayer}", this);
            }

            if (parryShieldPrefab == null) {
                Debug.LogWarning("[PlayerVFXController] Parry shield VFX skipped because Parry Shield Prefab is not assigned.", this);
                return;
            }

            Transform parryRoot = surfCharacter != null ? surfCharacter.transform : transform;
            float parryActiveDuration = Mathf.Max(0.01f, activeDuration);

            float phaseInDuration = Mathf.Clamp(parryShieldPhaseInDuration, 0f, parryActiveDuration);
            Vector3 endScale = parryShieldScale;
            Vector3 startScale = endScale * Mathf.Max(0f, parryShieldStartScaleMultiplier);
            _parryShieldVfxActive = true;

            ParryShieldEffect shield = GetParryShieldInstance();
            if (shield == null)
                return;

            if (logParryShieldDebug) {
                Debug.Log($"[PlayerVFXController] Spawning centered parry shield. localPosition={parryShieldCenterOffset}, parryActiveDuration={parryActiveDuration:0.000}, phaseIn={phaseInDuration:0.000}, phaseOutAfterParry={parryShieldPhaseOutDuration:0.000}", this);
            }

            shield.Play(
                parryRoot,
                transform,
                parryShieldFollowPlayer,
                parryShieldCenterOffset,
                parryShieldCenterOffset,
                Quaternion.Euler(parryShieldRotationOffset),
                startScale,
                endScale,
                parryActiveDuration,
                phaseInDuration,
                parryShieldPhaseOutDuration,
                logParryShieldDebug
            );
        }

        private void EndParryShields() {
            _parryShieldVfxActive = false;

            if (logParryShieldDebug) {
                Debug.Log($"[PlayerVFXController] Parry ended. Beginning shield phase-out. phaseOut={parryShieldPhaseOutDuration:0.000}", this);
            }

            if (_parryShieldInstance != null && !_parryShieldInstance.IsAvailable)
                _parryShieldInstance.BeginPhaseOut(parryShieldPhaseOutDuration);
        }

        private void UpdateMeleePlasma(MoveData state) {
            bool shouldPlay = IsMeleePlasmaStateActive(state);
            if (!shouldPlay) {
                if (_meleePlasmaVfxActive)
                    EndMeleePlasma(false);
                return;
            }

            float activeDuration = GetMeleePlasmaStateDuration(state);
            bool needsRestart = !_meleePlasmaVfxActive ||
                                _meleePlasmaCurrentState != state.meleeState ||
                                _meleePlasmaInstance == null;

            if (needsRestart)
                StartMeleePlasma(state, activeDuration);

            if (_meleePlasmaInstance != null)
                _meleePlasmaInstance.SetStateProgress(GetMeleePlasmaStateProgress(state, activeDuration));
        }

        private bool IsMeleePlasmaStateActive(MoveData state) {
            if (meleePlasmaPrefab == null || state == null || state.moveType != MoveType.HeavyMelee)
                return false;

            switch (state.meleeState) {
                case MoveData.MeleeState.Charging:
                    return (meleePlasmaActiveStates & MeleePlasmaActiveState.Charging) != 0;
                case MoveData.MeleeState.Lunging:
                    return (meleePlasmaActiveStates & MeleePlasmaActiveState.Lunging) != 0;
                case MoveData.MeleeState.Recovery:
                    return (meleePlasmaActiveStates & MeleePlasmaActiveState.Recovery) != 0;
                default:
                    return false;
            }
        }

        private void StartMeleePlasma(MoveData state, float activeDuration) {
            MeleePlasmaEffect plasma = GetMeleePlasmaInstance();
            if (plasma == null)
                return;

            Transform parentRoot = GetMeleePlasmaParent();
            _meleePlasmaVfxActive = true;
            _meleePlasmaCurrentState = state != null ? state.meleeState : MoveData.MeleeState.None;

            plasma.Play(
                parentRoot,
                transform,
                meleePlasmaFollowParent,
                meleePlasmaLocalPosition,
                Quaternion.Euler(meleePlasmaLocalRotation),
                meleePlasmaLocalScale,
                activeDuration,
                meleePlasmaPhaseInDuration,
                meleePlasmaPhaseOutDuration,
                meleePlasmaStartScaleMultiplier,
                meleePlasmaRotationSpeed,
                logMeleePlasmaDebug
            );

            if (logMeleePlasmaDebug) {
                Debug.Log($"[PlayerVFXController] Melee plasma started. state={_meleePlasmaCurrentState}, duration={activeDuration:0.000}, parent={(parentRoot != null ? parentRoot.name : "none")}, localPosition={meleePlasmaLocalPosition}, localRotation={meleePlasmaLocalRotation}, localScale={meleePlasmaLocalScale}", this);
            }
        }

        private void EndMeleePlasma(bool immediate) {
            _meleePlasmaVfxActive = false;
            _meleePlasmaCurrentState = MoveData.MeleeState.None;

            if (_meleePlasmaInstance == null || _meleePlasmaInstance.IsAvailable)
                return;

            _meleePlasmaInstance.BeginPhaseOut(immediate ? 0f : meleePlasmaPhaseOutDuration);
        }

        private Transform GetMeleePlasmaParent() {
            if (meleePlasmaParentOverride != null)
                return meleePlasmaParentOverride;

            if (meleeWeaponTransform != null)
                return meleeWeaponTransform;

            return transform;
        }

        private float GetMeleePlasmaStateDuration(MoveData state) {
            if (MovementConfigUnavailable())
                return Mathf.Max(0.01f, meleePlasmaFallbackDuration);

            switch (state.meleeState) {
                case MoveData.MeleeState.Charging:
                    return Mathf.Max(0.01f, surfCharacter.movementConfig.heavyMeleeChargeTime);
                case MoveData.MeleeState.Lunging:
                    return Mathf.Max(0.01f, surfCharacter.movementConfig.heavyMeleeLungeDuration);
                case MoveData.MeleeState.Recovery:
                    return Mathf.Max(0.01f, surfCharacter.movementConfig.heavyMeleeRecoveryDuration);
                default:
                    return Mathf.Max(0.01f, meleePlasmaFallbackDuration);
            }
        }

        private float GetMeleePlasmaStateProgress(MoveData state, float activeDuration) {
            if (state == null || activeDuration <= 0f)
                return 0f;

            return Mathf.Clamp01(state.meleeTimer / activeDuration);
        }

        private bool MovementConfigUnavailable() {
            return surfCharacter == null || surfCharacter.movementConfig == null;
        }

        private MeleePlasmaEffect GetMeleePlasmaInstance() {
            if (_meleePlasmaInstance != null)
                return _meleePlasmaInstance;

            if (meleePlasmaPrefab == null)
                return null;

            GameObject plasmaObject = Instantiate(meleePlasmaPrefab, transform);
            plasmaObject.name = $"{meleePlasmaPrefab.name} Melee Plasma";

            MeleePlasmaEffect plasma = plasmaObject.GetComponent<MeleePlasmaEffect>();
            if (plasma == null)
                plasma = plasmaObject.AddComponent<MeleePlasmaEffect>();

            plasmaObject.SetActive(false);
            _meleePlasmaInstance = plasma;
            return _meleePlasmaInstance;
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

        private Vector3 GetFlatMomentumDirection(Vector3 fallbackDirection) {
            if (_hasCachedState && _cachedState != null) {
                Vector3 momentum = new Vector3(_cachedState.velocity.x, 0f, _cachedState.velocity.z);
                if (momentum.sqrMagnitude >= 0.001f)
                    return momentum.normalized;
            }

            if (fallbackDirection.sqrMagnitude >= 0.001f)
                return fallbackDirection.normalized;

            return GetPresentationForward();
        }

        private Vector3 GetLowerVfxPosition() {
            if (surfCharacter != null && surfCharacter.lowerVfxSpawnPoint != null)
                return surfCharacter.lowerVfxSpawnPoint.position;

            if (surfCharacter != null)
                return surfCharacter.transform.position;

            return transform.position;
        }

        private Vector3 GetPlayerRootPosition() {
            if (surfCharacter != null)
                return surfCharacter.transform.position;

            return transform.position;
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

        private void SpawnDashLines(Vector3 dashDirection) {
            int lineCount = Random.Range(Mathf.Min(dashLineCountRange.x, dashLineCountRange.y), Mathf.Max(dashLineCountRange.x, dashLineCountRange.y) + 1);
            if (!spawnDashLines || lineCount <= 0)
                return;

            Vector3 right = Vector3.Cross(Vector3.up, dashDirection).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = transform.right;

            Vector3 basePosition = GetPlayerRootPosition()
                + Vector3.up * dashLineHeight
                - dashDirection * dashLineBehindDistance;

            for (int i = 0; i < lineCount; i++) {
                Vector3 randomOffset =
                    right * Random.Range(-dashLineSideSpread, dashLineSideSpread)
                    + Vector3.up * Random.Range(-dashLineVerticalSpread, dashLineVerticalSpread);

                float length = RandomRange(dashLineLengthRange);
                float width = RandomRange(dashLineWidthRange);
                Vector3 start = basePosition + randomOffset;
                Vector3 end = start + dashDirection * Mathf.Max(0f, length);

                GetDashLineFromPool().Play(start, end, dashLineColor, width, dashLineLifetime);
            }
        }

        private static float RandomRange(Vector2 range) {
            return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }

        private DashLineEffect GetDashLineFromPool() {
            for (int i = 0; i < dashLinePool.Count; i++) {
                if (dashLinePool[i] != null && dashLinePool[i].IsAvailable) {
                    return dashLinePool[i];
                }
            }

            GameObject lineObject = new GameObject("Dash Line Effect");
            lineObject.transform.SetParent(transform);

            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.sharedMaterial = dashLineMaterial != null ? dashLineMaterial : GetDefaultDashLineMaterial();
            if (lineRenderer.sharedMaterial == null && !_warnedMissingDashLineMaterial) {
                _warnedMissingDashLineMaterial = true;
                Debug.LogWarning("[PlayerVFXController] Dash lines need a material, but no compatible fallback shader was found. Assign Dash Line Material in the inspector.", this);
            }
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.sortingOrder = 20;

            DashLineEffect dashLine = lineObject.AddComponent<DashLineEffect>();
            lineObject.SetActive(false);
            dashLinePool.Add(dashLine);
            return dashLine;
        }

        private Material GetDefaultDashLineMaterial() {
            if (_defaultDashLineMaterial != null)
                return _defaultDashLineMaterial;

            Shader shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

            _defaultDashLineMaterial = shader != null ? new Material(shader) : null;
            ConfigureDashLineMaterial(_defaultDashLineMaterial);
            return _defaultDashLineMaterial;
        }

        private static void ConfigureDashLineMaterial(Material material) {
            if (material == null)
                return;

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private CloudPoofEffect GetCloudPoofFromPool() {
            for (int i = 0; i < cloudPoofPool.Count; i++) {
                if (cloudPoofPool[i] != null && cloudPoofPool[i].IsAvailable) {
                    return cloudPoofPool[i];
                }
            }

            CloudPoofEffect poof = Instantiate(cloudPoofPrefab, transform);
            poof.gameObject.SetActive(false);
            cloudPoofPool.Add(poof);
            return poof;
        }

        private ParryShieldEffect GetParryShieldInstance() {
            if (_parryShieldInstance != null)
                return _parryShieldInstance;

            GameObject shieldObject = Instantiate(parryShieldPrefab, transform);
            shieldObject.name = $"{parryShieldPrefab.name} Parry Shield";

            ParryShieldEffect shield = shieldObject.GetComponent<ParryShieldEffect>();
            if (shield == null)
                shield = shieldObject.AddComponent<ParryShieldEffect>();

            shieldObject.SetActive(false);
            _parryShieldInstance = shield;

            if (logParryShieldDebug) {
                Debug.Log($"[PlayerVFXController] Created parry shield instance '{shieldObject.name}'.", this);
            }

            return _parryShieldInstance;
        }
    }
}

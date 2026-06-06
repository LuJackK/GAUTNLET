using UnityEngine;

namespace Fragsurf.Movement {

    public class MeleePlasmaEffect : MonoBehaviour {

        private enum PlaybackState {
            Hidden,
            PhaseIn,
            Active,
            PhaseOut
        }

        private const float HiddenScaleMultiplier = 0f;

        private Transform _poolParent;
        private ParticleSystem[] _particleSystems;
        private Animator[] _animators;
        private bool _playing;
        private bool _useLocalSpace;
        private bool _logDebug;
        private float _age;
        private float _activeAge;
        private float _activeDuration;
        private float _phaseInDuration;
        private float _phaseOutDuration;
        private float _phaseOutStartScaleMultiplier;
        private float _stateProgress;
        private float _startScaleMultiplier;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private Quaternion _worldRotation;
        private Vector3 _localScale;
        private Vector3 _rotationSpeed;
        private PlaybackState _playbackState = PlaybackState.Hidden;

        public bool IsAvailable => !_playing;

        private void Awake() {
            CacheComponents();
        }

        public void Play(
            Transform parentRoot,
            Transform poolParent,
            bool followParent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            float activeDuration,
            float phaseInDuration,
            float phaseOutDuration,
            float startScaleMultiplier,
            Vector3 rotationSpeed,
            bool logDebug
        ) {
            _poolParent = poolParent;
            _logDebug = logDebug;
            _useLocalSpace = followParent && parentRoot != null;
            _localPosition = localPosition;
            _localRotation = localRotation;
            _localScale = localScale;
            _activeDuration = Mathf.Max(0.01f, activeDuration);
            _phaseInDuration = Mathf.Max(0f, phaseInDuration);
            _phaseOutDuration = Mathf.Max(0f, phaseOutDuration);
            _startScaleMultiplier = Mathf.Max(0f, startScaleMultiplier);
            _rotationSpeed = rotationSpeed;
            _age = 0f;
            _activeAge = 0f;
            _stateProgress = 0f;
            _playing = true;
            _playbackState = PlaybackState.PhaseIn;

            CacheComponents();
            gameObject.SetActive(true);

            if (_useLocalSpace) {
                transform.SetParent(parentRoot, false);
            } else {
                Vector3 worldPosition = parentRoot != null ? parentRoot.TransformPoint(localPosition) : localPosition;
                _worldRotation = parentRoot != null ? parentRoot.rotation * localRotation : localRotation;

                transform.SetParent(null, true);
                transform.position = worldPosition;
                transform.rotation = _worldRotation;
            }

            ApplyAnimatedTransform(_startScaleMultiplier);
            RestartAnimators();
            PlayParticles();

            if (_logDebug) {
                Debug.Log($"[MeleePlasmaEffect] Play '{name}'. parent={(parentRoot != null ? parentRoot.name : "none")}, follow={_useLocalSpace}, active={_activeDuration:0.000}, phaseIn={_phaseInDuration:0.000}, phaseOut={_phaseOutDuration:0.000}", this);
            }
        }

        public void SetStateProgress(float normalizedProgress) {
            _stateProgress = Mathf.Clamp01(normalizedProgress);
        }

        public void BeginPhaseOut(float phaseOutDuration) {
            if (!_playing || _playbackState == PlaybackState.PhaseOut)
                return;

            _phaseOutDuration = Mathf.Max(0f, phaseOutDuration);
            if (_phaseOutDuration <= 0f) {
                Stop();
                return;
            }

            _phaseOutStartScaleMultiplier = GetCurrentScaleMultiplier();
            _age = 0f;
            _playbackState = PlaybackState.PhaseOut;

            if (_logDebug) {
                Debug.Log($"[MeleePlasmaEffect] Begin phase-out '{name}'. phaseOut={_phaseOutDuration:0.000}", this);
            }
        }

        private void Update() {
            if (!_playing)
                return;

            float deltaTime = Time.deltaTime;
            _age += deltaTime;

            if (_playbackState == PlaybackState.PhaseIn) {
                _activeAge += deltaTime;
                float phaseT = _phaseInDuration > 0f ? Mathf.Clamp01(_age / _phaseInDuration) : 1f;
                ApplyAnimatedTransform(Mathf.Lerp(_startScaleMultiplier, 1f, Mathf.SmoothStep(0f, 1f, phaseT)));

                if (phaseT >= 1f) {
                    _age = 0f;
                    _playbackState = PlaybackState.Active;
                    ApplyAnimatedTransform(1f);
                }

                BeginPhaseOutWhenActiveWindowExpires();
                return;
            }

            if (_playbackState == PlaybackState.Active) {
                _activeAge += deltaTime;
                ApplyAnimatedTransform(1f);
                BeginPhaseOutWhenActiveWindowExpires();
                return;
            }

            if (_playbackState == PlaybackState.PhaseOut) {
                _activeAge += deltaTime;
                float fadeT = _phaseOutDuration > 0f ? Mathf.Clamp01(_age / _phaseOutDuration) : 1f;
                ApplyAnimatedTransform(Mathf.Lerp(_phaseOutStartScaleMultiplier, HiddenScaleMultiplier, Mathf.SmoothStep(0f, 1f, fadeT)));

                if (fadeT >= 1f)
                    Stop();
            }
        }

        private void BeginPhaseOutWhenActiveWindowExpires() {
            if (_activeDuration <= 0f)
                return;

            float progress = Mathf.Clamp01(_activeAge / _activeDuration);
            if (progress > _stateProgress)
                _stateProgress = progress;

            if (_activeAge >= _activeDuration)
                BeginPhaseOut(_phaseOutDuration);
        }

        private void Stop() {
            _playing = false;
            _playbackState = PlaybackState.Hidden;
            StopParticles();

            if (_poolParent != null)
                transform.SetParent(_poolParent, false);

            if (_logDebug) {
                Debug.Log($"[MeleePlasmaEffect] Stop '{name}'.", this);
            }

            gameObject.SetActive(false);
        }

        private void ApplyAnimatedTransform(float scaleMultiplier) {
            Quaternion spinRotation = Quaternion.Euler(_rotationSpeed * _activeAge);

            if (_useLocalSpace) {
                transform.localPosition = _localPosition;
                transform.localRotation = _localRotation * spinRotation;
            } else {
                transform.rotation = _worldRotation * spinRotation;
            }

            transform.localScale = _localScale * Mathf.Max(0f, scaleMultiplier);
        }

        private float GetCurrentScaleMultiplier() {
            float baseMagnitude = _localScale.sqrMagnitude;
            if (baseMagnitude <= 0.0001f)
                return 1f;

            return Mathf.Sqrt(transform.localScale.sqrMagnitude / baseMagnitude);
        }

        private void CacheComponents() {
            if (_particleSystems == null)
                _particleSystems = GetComponentsInChildren<ParticleSystem>(true);

            if (_animators == null)
                _animators = GetComponentsInChildren<Animator>(true);
        }

        private void RestartAnimators() {
            if (_animators == null)
                return;

            for (int i = 0; i < _animators.Length; i++) {
                Animator animator = _animators[i];
                if (animator == null)
                    continue;

                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);
            }
        }

        private void PlayParticles() {
            if (_particleSystems == null)
                return;

            for (int i = 0; i < _particleSystems.Length; i++) {
                ParticleSystem particleSystem = _particleSystems[i];
                if (particleSystem == null)
                    continue;

                particleSystem.Clear(true);
                particleSystem.Play(true);
            }
        }

        private void StopParticles() {
            if (_particleSystems == null)
                return;

            for (int i = 0; i < _particleSystems.Length; i++) {
                if (_particleSystems[i] != null)
                    _particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}

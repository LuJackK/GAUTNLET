using UnityEngine;

namespace Fragsurf.Movement {

    public class ParryShieldEffect : MonoBehaviour {

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Vector3 HiddenScale = Vector3.zero;

        private Transform _poolParent;
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _propertyBlock;

        private enum PlaybackState {
            Hidden,
            PhaseIn,
            Active,
            PhaseOut
        }

        private bool _playing;
        private bool _useLocalSpace;
        private float _age;
        private float _activeAge;
        private float _activeDuration;
        private float _phaseInDuration;
        private float _phaseOutDuration;
        private float _currentAlpha;
        private Vector3 _startPosition;
        private Vector3 _endPosition;
        private Quaternion _startRotation;
        private Quaternion _endRotation;
        private Vector3 _startScale;
        private Vector3 _endScale;
        private Vector3 _phaseOutStartScale;
        private float _phaseOutStartAlpha;
        private PlaybackState _playbackState = PlaybackState.Hidden;
        private bool _logDebug;

        public bool IsAvailable => !_playing;

        private void Awake() {
            CacheRenderers();
        }

        public void Play(
            Transform formationRoot,
            Transform poolParent,
            bool followRoot,
            Vector3 localStartPosition,
            Vector3 localEndPosition,
            Quaternion localEndRotation,
            Vector3 startScale,
            Vector3 endScale,
            float activeDuration,
            float phaseInDuration,
            float phaseOutDuration,
            bool logDebug
        ) {
            _poolParent = poolParent;
            _logDebug = logDebug;
            _useLocalSpace = followRoot && formationRoot != null;
            _activeDuration = Mathf.Max(0f, activeDuration);
            _phaseInDuration = Mathf.Max(0f, phaseInDuration);
            _phaseOutDuration = Mathf.Max(0f, phaseOutDuration);
            _currentAlpha = 0f;
            _startPosition = localStartPosition;
            _endPosition = localEndPosition;
            _startRotation = localEndRotation;
            _endRotation = localEndRotation;
            _startScale = startScale;
            _endScale = endScale;
            _age = 0f;
            _activeAge = 0f;
            _playing = true;
            _playbackState = PlaybackState.PhaseIn;

            CacheRenderers();
            gameObject.SetActive(true);

            if (_useLocalSpace) {
                transform.SetParent(formationRoot, false);
                ApplyTransform(_startPosition, _startRotation, _startScale);
            } else {
                Vector3 worldStart = formationRoot != null ? formationRoot.TransformPoint(localStartPosition) : localStartPosition;
                Vector3 worldEnd = formationRoot != null ? formationRoot.TransformPoint(localEndPosition) : localEndPosition;
                Quaternion worldRotation = formationRoot != null ? formationRoot.rotation * localEndRotation : localEndRotation;

                transform.SetParent(null, true);
                _startPosition = worldStart;
                _endPosition = worldEnd;
                _startRotation = worldRotation;
                _endRotation = worldRotation;
                ApplyTransform(_startPosition, _startRotation, _startScale);
            }

            SetAlpha(0f);

            if (_logDebug) {
                Debug.Log($"[ParryShieldEffect] Play '{name}'. useLocalSpace={_useLocalSpace}, renderers={(_renderers != null ? _renderers.Length : 0)}, active={_activeDuration:0.000}, phaseIn={_phaseInDuration:0.000}, phaseOut={_phaseOutDuration:0.000}, start={_startPosition}, end={_endPosition}, startScale={_startScale}, endScale={_endScale}", this);
            }
        }

        public void BeginPhaseOut(float phaseOutDuration) {
            if (!_playing || _playbackState == PlaybackState.PhaseOut)
                return;

            _phaseOutDuration = Mathf.Max(0f, phaseOutDuration);
            if (_phaseOutDuration <= 0f) {
                Stop();
                return;
            }

            _phaseOutStartScale = transform.localScale;
            _phaseOutStartAlpha = _currentAlpha;
            _age = 0f;
            _playbackState = PlaybackState.PhaseOut;

            ApplyTransform(_endPosition, _endRotation, _phaseOutStartScale);

            if (_logDebug) {
                Debug.Log($"[ParryShieldEffect] Begin phase-out '{name}'. phaseOut={_phaseOutDuration:0.000}, alpha={_phaseOutStartAlpha:0.000}, startScale={_phaseOutStartScale}, endScale={HiddenScale}", this);
            }
        }

        private void Update() {
            if (!_playing)
                return;

            float deltaTime = Time.deltaTime;
            _age += deltaTime;

            if (_playbackState == PlaybackState.PhaseIn) {
                _activeAge += deltaTime;
                float motionT = _phaseInDuration > 0f ? Mathf.Clamp01(_age / _phaseInDuration) : 1f;
                motionT = Mathf.SmoothStep(0f, 1f, motionT);

                ApplyTransform(
                    Vector3.LerpUnclamped(_startPosition, _endPosition, motionT),
                    Quaternion.SlerpUnclamped(_startRotation, _endRotation, motionT),
                    Vector3.LerpUnclamped(_startScale, _endScale, motionT)
                );
                SetAlpha(_phaseInDuration > 0f ? Mathf.Clamp01(_age / _phaseInDuration) : 1f);

                if (motionT >= 1f) {
                    _playbackState = PlaybackState.Active;
                    ApplyTransform(_endPosition, _endRotation, _endScale);
                    SetAlpha(1f);
                }

                BeginPhaseOutWhenParryWindowExpires();
                return;
            }

            if (_playbackState == PlaybackState.Active) {
                _activeAge += deltaTime;
                ApplyTransform(_endPosition, _endRotation, _endScale);
                SetAlpha(1f);
                BeginPhaseOutWhenParryWindowExpires();
                return;
            }

            if (_playbackState == PlaybackState.PhaseOut) {
                float fadeT = _phaseOutDuration > 0f ? Mathf.Clamp01(_age / _phaseOutDuration) : 1f;
                fadeT = Mathf.SmoothStep(0f, 1f, fadeT);
                ApplyTransform(
                    _endPosition,
                    _endRotation,
                    Vector3.LerpUnclamped(_phaseOutStartScale, HiddenScale, fadeT)
                );
                SetAlpha(Mathf.Lerp(_phaseOutStartAlpha, 0f, fadeT));

                if (fadeT >= 1f)
                    Stop();
            }
        }

        private void BeginPhaseOutWhenParryWindowExpires() {
            if (_activeDuration > 0f && _activeAge >= _activeDuration)
                BeginPhaseOut(_phaseOutDuration);
        }

        private void Stop() {
            _playing = false;
            _playbackState = PlaybackState.Hidden;
            SetAlpha(0f);

            if (_poolParent != null)
                transform.SetParent(_poolParent, false);

            if (_logDebug) {
                Debug.Log($"[ParryShieldEffect] Stop '{name}'. age={_age:0.000}", this);
            }

            gameObject.SetActive(false);
        }

        private void ApplyTransform(Vector3 position, Quaternion rotation, Vector3 scale) {
            if (_useLocalSpace) {
                transform.localPosition = position;
                transform.localRotation = rotation;
            } else {
                transform.position = position;
                transform.rotation = rotation;
            }

            transform.localScale = scale;
        }

        private void CacheRenderers() {
            if (_renderers != null)
                return;

            _propertyBlock = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];

            if (_renderers.Length == 0) {
                Debug.LogWarning($"[ParryShieldEffect] '{name}' has no Renderer components in children. The object can spawn but will not be visible.", this);
            }

            for (int i = 0; i < _renderers.Length; i++) {
                Material material = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                _baseColors[i] = GetMaterialColor(material);
            }
        }

        private void SetAlpha(float alpha) {
            if (_renderers == null)
                return;

            for (int i = 0; i < _renderers.Length; i++) {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                _currentAlpha = Mathf.Clamp01(alpha);
                Color color = _baseColors[i];
                color.a *= _currentAlpha;

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(ColorId, color);
                _propertyBlock.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private static Color GetMaterialColor(Material material) {
            if (material == null)
                return Color.white;

            if (material.HasProperty(BaseColorId))
                return material.GetColor(BaseColorId);

            if (material.HasProperty(ColorId))
                return material.GetColor(ColorId);

            return Color.white;
        }
    }
}

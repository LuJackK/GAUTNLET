using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fragsurf.Movement {

    public class PlayerSoundController : MonoBehaviour {

        [Serializable]
        private class SoundSet {
            [Tooltip("Minimum seconds before this event can play again.")]
            [SerializeField, Min(0f)] private float interval = 0f;
            [Tooltip("One random bundle is chosen each time this event plays. Clips inside that bundle play together.")]
            [SerializeField] private SoundBundle[] bundles;

            private int _lastBundleIndex = -1;
            private float _nextPlayTime;

            public void Play(PlayerSoundController owner, List<AudioSource> startedSources = null, bool loopUntilStopped = false) {
                if (owner == null || Time.time < _nextPlayTime)
                    return;

                SoundBundle bundle = GetRandomBundle();
                if (bundle == null)
                    return;

                bundle.Play(owner, startedSources, loopUntilStopped);
                _nextPlayTime = Time.time + interval;
            }

            private SoundBundle GetRandomBundle() {
                if (bundles == null || bundles.Length == 0)
                    return null;

                int validBundleCount = 0;
                for (int i = 0; i < bundles.Length; i++) {
                    if (bundles[i] != null && bundles[i].HasPlayableClip)
                        validBundleCount++;
                }

                if (validBundleCount == 0)
                    return null;

                int selectedIndex = UnityEngine.Random.Range(0, validBundleCount);
                for (int i = 0; i < bundles.Length; i++) {
                    if (bundles[i] == null || !bundles[i].HasPlayableClip)
                        continue;

                    if (selectedIndex == 0) {
                        if (validBundleCount > 1 && i == _lastBundleIndex)
                            return GetNextDifferentBundle(i);

                        _lastBundleIndex = i;
                        return bundles[i];
                    }

                    selectedIndex--;
                }

                return null;
            }

            private SoundBundle GetNextDifferentBundle(int currentIndex) {
                for (int offset = 1; offset < bundles.Length; offset++) {
                    int index = (currentIndex + offset) % bundles.Length;
                    if (bundles[index] == null || !bundles[index].HasPlayableClip)
                        continue;

                    _lastBundleIndex = index;
                    return bundles[index];
                }

                _lastBundleIndex = currentIndex;
                return bundles[currentIndex];
            }
        }

        [Serializable]
        private class SoundBundle {
            [SerializeField] private SoundLayer[] layers;

            public bool HasPlayableClip {
                get {
                    if (layers == null)
                        return false;

                    for (int i = 0; i < layers.Length; i++) {
                        if (layers[i] != null && layers[i].Clip != null)
                            return true;
                    }

                    return false;
                }
            }

            public void Play(PlayerSoundController owner, List<AudioSource> startedSources = null, bool loopUntilStopped = false) {
                if (layers == null)
                    return;

                for (int i = 0; i < layers.Length; i++) {
                    if (layers[i] != null)
                        owner.PlayLayer(layers[i], startedSources, loopUntilStopped);
                }
            }
        }

        [Serializable]
        private class SoundLayer {
            [SerializeField] private AudioClip clip;
            [SerializeField, Range(0f, 1f)] private float volume = 1f;
            [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
            [Tooltip("Seconds to wait before this layer starts after the event fires.")]
            [SerializeField, Min(0f)] private float startDelay = 0f;
            [Tooltip("Seconds to play before stopping. Use 0 to play the whole clip.")]
            [SerializeField, Min(0f)] private float duration = 0f;

            public AudioClip Clip => clip;
            public float Volume => volume;
            public float Pitch => pitch;
            public float StartDelay => startDelay;
            public float Duration => duration;
        }

        private struct ActiveLayer {
            public AudioSource Source;
            public float StopTime;

            public ActiveLayer(AudioSource source, float stopTime) {
                Source = source;
                StopTime = stopTime;
            }
        }

        [Header("References")]
        [SerializeField] private AudioSource audioSource;

        [Header("Melee")]
        [SerializeField] private SoundSet meleeCollideWithPlayerSounds = new SoundSet();
        [SerializeField] private SoundSet meleeChargingSounds = new SoundSet();
        [SerializeField] private SoundSet meleeLungingSounds = new SoundSet();

        [Header("Parry")]
        [SerializeField] private SoundSet parryingSounds = new SoundSet();
        [SerializeField] private SoundSet successfulParrySounds = new SoundSet();

        [Header("Movement")]
        [SerializeField] private SoundSet dashSounds = new SoundSet();
        [SerializeField] private SoundSet doubleJumpSounds = new SoundSet();
        [SerializeField] private SoundSet slidingSounds = new SoundSet();

        private readonly List<AudioSource> _pooledSources = new List<AudioSource>();
        private readonly List<ActiveLayer> _activeLayers = new List<ActiveLayer>();
        private readonly List<AudioSource> _activeMeleeChargingLayers = new List<AudioSource>();
        private readonly List<AudioSource> _activeSlidingLayers = new List<AudioSource>();

        private void Awake() {
            EnsureAudioSource();
        }

        private void Update() {
            for (int i = _activeLayers.Count - 1; i >= 0; i--) {
                ActiveLayer activeLayer = _activeLayers[i];
                if (activeLayer.Source == null || Time.time >= activeLayer.StopTime) {
                    StopAndRelease(activeLayer.Source);
                    _activeLayers.RemoveAt(i);
                }
            }
        }

        private void OnDisable() {
            StopStateSound(_activeMeleeChargingLayers);
            StopStateSound(_activeSlidingLayers);

            for (int i = _activeLayers.Count - 1; i >= 0; i--)
                StopAndRelease(_activeLayers[i].Source);

            _activeLayers.Clear();
        }

        public void ApplyState(MoveData state, MoveData prevState, bool isNewTick) {
            if (!isNewTick)
                return;

            if (state == null) {
                StopStateSound(_activeMeleeChargingLayers);
                StopStateSound(_activeSlidingLayers);
                return;
            }

            EnsureAudioSource();

            UpdateStateSound(
                IsMeleeState(state, MoveData.MeleeState.Charging),
                IsMeleeState(prevState, MoveData.MeleeState.Charging),
                meleeChargingSounds,
                _activeMeleeChargingLayers
            );

            if (StartedMeleeState(state, prevState, MoveData.MeleeState.Lunging))
                meleeLungingSounds.Play(this);

            if (state.meleeHitThisFrame)
                meleeCollideWithPlayerSounds.Play(this);

            if (state.parryStartedThisFrame)
                parryingSounds.Play(this);

            if (state.parrySuccessThisFrame)
                successfulParrySounds.Play(this);

            if (state.dashStartedThisFrame)
                dashSounds.Play(this);

            if (state.doubleJumpedThisFrame)
                doubleJumpSounds.Play(this);

            UpdateStateSound(
                IsSliding(state),
                IsSliding(prevState),
                slidingSounds,
                _activeSlidingLayers
            );
        }

        private void UpdateStateSound(bool isActive, bool wasActive, SoundSet sounds, List<AudioSource> activeSources) {
            if (isActive) {
                if (!wasActive || !HasAnyActiveSource(activeSources))
                    sounds.Play(this, activeSources, true);
            } else if (wasActive || activeSources.Count > 0) {
                StopStateSound(activeSources);
            }
        }

        private bool HasAnyActiveSource(List<AudioSource> sources) {
            for (int i = sources.Count - 1; i >= 0; i--) {
                AudioSource source = sources[i];
                if (source == null || !source.gameObject.activeSelf) {
                    sources.RemoveAt(i);
                    continue;
                }

                return true;
            }

            return false;
        }

        private void StopStateSound(List<AudioSource> sources) {
            for (int i = sources.Count - 1; i >= 0; i--) {
                AudioSource source = sources[i];
                RemoveActiveLayer(source);
                StopAndRelease(source);
                sources.RemoveAt(i);
            }
        }

        private void RemoveActiveLayer(AudioSource source) {
            for (int i = _activeLayers.Count - 1; i >= 0; i--) {
                if (_activeLayers[i].Source == source)
                    _activeLayers.RemoveAt(i);
            }
        }

        private void PlayLayer(SoundLayer layer, List<AudioSource> startedSources = null, bool loopUntilStopped = false) {
            if (layer == null || layer.Clip == null)
                return;

            EnsureAudioSource();

            AudioSource source = GetAvailableSource();
            CopyAudioSourceSettings(audioSource, source);
            source.clip = layer.Clip;
            source.volume = layer.Volume;
            source.pitch = layer.Pitch;
            source.loop = loopUntilStopped;
            source.gameObject.SetActive(true);

            if (layer.StartDelay > 0f)
                source.PlayDelayed(layer.StartDelay);
            else
                source.Play();

            if (startedSources != null)
                startedSources.Add(source);

            if (loopUntilStopped)
                return;

            _activeLayers.Add(new ActiveLayer(source, Time.time + layer.StartDelay + GetLayerDuration(layer)));
        }

        private void EnsureAudioSource() {
            if (audioSource != null)
                return;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
        }

        private AudioSource GetAvailableSource() {
            for (int i = 0; i < _pooledSources.Count; i++) {
                AudioSource source = _pooledSources[i];
                if (source != null && !source.gameObject.activeSelf)
                    return source;
            }

            GameObject sourceObject = new GameObject("Player SFX Layer");
            sourceObject.transform.SetParent(transform, false);
            AudioSource audioSourceLayer = sourceObject.AddComponent<AudioSource>();
            audioSourceLayer.playOnAwake = false;
            sourceObject.SetActive(false);
            _pooledSources.Add(audioSourceLayer);
            return audioSourceLayer;
        }

        private void StopAndRelease(AudioSource source) {
            if (source == null)
                return;

            source.Stop();
            source.clip = null;
            source.loop = false;
            source.gameObject.SetActive(false);
        }

        private static float GetLayerDuration(SoundLayer layer) {
            return layer.Duration > 0f
                ? layer.Duration
                : layer.Clip.length / Mathf.Max(0.01f, Mathf.Abs(layer.Pitch));
        }

        private static void CopyAudioSourceSettings(AudioSource source, AudioSource target) {
            if (source == null || target == null)
                return;

            target.outputAudioMixerGroup = source.outputAudioMixerGroup;
            target.spatialBlend = source.spatialBlend;
            target.minDistance = source.minDistance;
            target.maxDistance = source.maxDistance;
            target.rolloffMode = source.rolloffMode;
            target.priority = source.priority;
            target.dopplerLevel = source.dopplerLevel;
        }

        private static bool StartedMeleeState(MoveData state, MoveData prevState, MoveData.MeleeState meleeState) {
            bool isState = IsMeleeState(state, meleeState);
            bool wasState = IsMeleeState(prevState, meleeState);
            return isState && !wasState;
        }

        private static bool IsMeleeState(MoveData state, MoveData.MeleeState meleeState) {
            return state != null && state.moveType == MoveType.HeavyMelee && state.meleeState == meleeState;
        }

        private static bool IsSliding(MoveData state) {
            return state != null && state.slidingEnabled && state.sliding;
        }
    }
}

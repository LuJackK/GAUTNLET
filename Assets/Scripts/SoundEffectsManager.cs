using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public sealed class SoundEffectsManager : MonoBehaviour
{
    public static SoundEffectsManager Instance { get; private set; }

    [Header("Pool")]
    [SerializeField] private int initialPoolSize = 16;
    [SerializeField] private int maxPoolSize = 48;
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Defaults")]
    [SerializeField] private AudioMixerGroup outputMixerGroup;
    [SerializeField, Range(0f, 1f)] private float defaultVolume = 1f;
    [SerializeField, Range(-3f, 3f)] private float defaultPitch = 1f;
    [SerializeField, Range(0f, 1f)] private float defaultSpatialBlend = 1f;
    [SerializeField] private float defaultMinDistance = 1f;
    [SerializeField] private float defaultMaxDistance = 35f;
    [SerializeField] private AudioRolloffMode defaultRolloffMode = AudioRolloffMode.Logarithmic;

    private readonly List<ManagedSoundEffect> sources = new List<ManagedSoundEffect>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        int poolSize = Mathf.Max(0, initialPoolSize);
        for (int i = 0; i < poolSize; i++)
            CreateSource();
    }

    private void Update()
    {
        for (int i = 0; i < sources.Count; i++)
            sources[i].Tick();
    }

    public static ManagedSoundEffect Play(SoundEffectRequest request)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[SoundEffectsManager] No SoundEffectsManager exists in the scene.");
            return null;
        }

        return Instance.PlayInternal(request);
    }

    public static ManagedSoundEffect Play(AudioClip clip, Transform sourceTransform)
    {
        return Play(new SoundEffectRequest(clip, sourceTransform));
    }

    public static ManagedSoundEffect PlayFollowing(AudioClip clip, Transform sourceTransform)
    {
        SoundEffectRequest request = new SoundEffectRequest(clip, sourceTransform)
        {
            FollowTransform = true
        };

        return Play(request);
    }

    public static ManagedSoundEffect PlayAtPosition(AudioClip clip, Vector3 position)
    {
        return Play(SoundEffectRequest.AtPosition(clip, position));
    }

    private ManagedSoundEffect PlayInternal(SoundEffectRequest request)
    {
        if (request.Clip == null)
            return null;

        ManagedSoundEffect source = GetAvailableSource();
        ConfigureSource(source.AudioSource, request);
        source.Play(request, this);
        return source;
    }

    private void ConfigureSource(AudioSource source, SoundEffectRequest request)
    {
        source.outputAudioMixerGroup = request.OutputMixerGroup != null ? request.OutputMixerGroup : outputMixerGroup;
        source.volume = Mathf.Clamp01(request.Volume >= 0f ? request.Volume : defaultVolume);
        source.pitch = Mathf.Clamp(request.Pitch != 0f ? request.Pitch : defaultPitch, -3f, 3f);
        source.spatialBlend = Mathf.Clamp01(request.SpatialBlend >= 0f ? request.SpatialBlend : defaultSpatialBlend);
        source.minDistance = Mathf.Max(0f, request.MinDistance >= 0f ? request.MinDistance : defaultMinDistance);
        source.maxDistance = Mathf.Max(source.minDistance, request.MaxDistance >= 0f ? request.MaxDistance : defaultMaxDistance);
        source.rolloffMode = request.RolloffMode;
        source.loop = request.Loop;
        source.priority = Mathf.Clamp(request.Priority > 0 ? request.Priority : 128, 0, 256);
    }

    private ManagedSoundEffect GetAvailableSource()
    {
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i].IsAvailable)
                return sources[i];
        }

        if (sources.Count < Mathf.Max(1, maxPoolSize))
            return CreateSource();

        ManagedSoundEffect oldest = sources[0];
        for (int i = 1; i < sources.Count; i++)
        {
            if (sources[i].StartedAt < oldest.StartedAt)
                oldest = sources[i];
        }

        oldest.Stop();
        return oldest;
    }

    private ManagedSoundEffect CreateSource()
    {
        GameObject sourceObject = new GameObject("Managed SFX Source");
        sourceObject.transform.SetParent(transform);

        AudioSource audioSource = sourceObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        ManagedSoundEffect managedSource = new ManagedSoundEffect(audioSource);
        sources.Add(managedSource);
        sourceObject.SetActive(false);
        return managedSource;
    }
}

public sealed class ManagedSoundEffect
{
    public AudioSource AudioSource { get; }
    public bool IsAvailable => !AudioSource.gameObject.activeSelf || !AudioSource.isPlaying;
    public float StartedAt { get; private set; }

    private Transform followTarget;
    private Vector3 followOffset;
    private bool followTransform;

    public ManagedSoundEffect(AudioSource audioSource)
    {
        AudioSource = audioSource;
    }

    public void Play(SoundEffectRequest request, SoundEffectsManager manager)
    {
        followTarget = request.SourceTransform;
        followOffset = request.Offset;
        followTransform = request.FollowTransform && followTarget != null;

        Transform audioTransform = AudioSource.transform;
        audioTransform.SetParent(manager.transform, true);
        audioTransform.position = GetStartPosition(request);
        audioTransform.rotation = followTarget != null ? followTarget.rotation : Quaternion.identity;

        AudioSource.gameObject.SetActive(true);
        AudioSource.clip = request.Clip;
        StartedAt = Time.time;
        AudioSource.Play();
    }

    public void Tick()
    {
        if (!AudioSource.gameObject.activeSelf)
            return;

        if (followTransform)
        {
            if (followTarget == null)
            {
                Stop();
                return;
            }

            AudioSource.transform.position = followTarget.position + followOffset;
        }

        if (!AudioSource.isPlaying)
            Stop();
    }

    public void Stop()
    {
        AudioSource.Stop();
        AudioSource.clip = null;
        AudioSource.transform.SetParent(SoundEffectsManager.Instance != null ? SoundEffectsManager.Instance.transform : null);
        AudioSource.gameObject.SetActive(false);

        followTarget = null;
        followOffset = Vector3.zero;
        followTransform = false;
    }

    private Vector3 GetStartPosition(SoundEffectRequest request)
    {
        if (request.UseWorldPosition)
            return request.WorldPosition + request.Offset;

        if (request.SourceTransform != null)
            return request.SourceTransform.position + request.Offset;

        return request.Offset;
    }
}

public struct SoundEffectRequest
{
    public AudioClip Clip;
    public Transform SourceTransform;
    public Vector3 WorldPosition;
    public bool UseWorldPosition;
    public bool FollowTransform;
    public Vector3 Offset;

    public float Volume;
    public float Pitch;
    public float SpatialBlend;
    public float MinDistance;
    public float MaxDistance;
    public AudioRolloffMode RolloffMode;
    public bool Loop;
    public int Priority;
    public AudioMixerGroup OutputMixerGroup;

    public SoundEffectRequest(AudioClip clip, Transform sourceTransform)
    {
        Clip = clip;
        SourceTransform = sourceTransform;
        WorldPosition = Vector3.zero;
        UseWorldPosition = false;
        FollowTransform = false;
        Offset = Vector3.zero;

        Volume = -1f;
        Pitch = 0f;
        SpatialBlend = -1f;
        MinDistance = -1f;
        MaxDistance = -1f;
        RolloffMode = AudioRolloffMode.Logarithmic;
        Loop = false;
        Priority = 128;
        OutputMixerGroup = null;
    }

    public static SoundEffectRequest AtPosition(AudioClip clip, Vector3 position)
    {
        SoundEffectRequest request = new SoundEffectRequest(clip, null)
        {
            WorldPosition = position,
            UseWorldPosition = true
        };

        return request;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SceneMusicPlayer : MonoBehaviour
{
    public static SceneMusicPlayer Instance { get; private set; }

    [Header("Scene Playlists")]
    [SerializeField] private List<SceneMusicPlaylist> scenePlaylists = new List<SceneMusicPlaylist>();

    [Header("Playback")]
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool playForCurrentSceneOnStart = true;
    [SerializeField] private bool stopWhenSceneHasNoPlaylist = true;
    [SerializeField, Min(0f)] private float fadeSeconds = 1f;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField] private AudioMixerGroup outputMixerGroup;

    private AudioSource activeSource;
    private AudioSource fadingSource;
    private Coroutine fadeRoutine;
    private Coroutine sceneRefreshRoutine;
    private SceneMusicPlaylist currentPlaylist;
    private string currentSceneName;
    private int currentTrackIndex = -1;

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

        activeSource = CreateMusicSource("Scene Music Source A");
        fadingSource = CreateMusicSource("Scene Music Source B");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void Start()
    {
        if (playForCurrentSceneOnStart)
            PlayForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void Update()
    {
        if (currentPlaylist == null || currentPlaylist.Tracks.Count == 0)
            return;

        if (activeSource.clip != null && !activeSource.isPlaying && fadeRoutine == null)
            PlayNextTrack(false);
    }

    public void PlayForScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        SceneMusicPlaylist playlist = FindPlaylist(sceneName);
        if (playlist == null || !TrySelectFirstTrack(playlist, out AudioClip firstClip))
        {
            currentPlaylist = null;
            currentSceneName = sceneName;
            currentTrackIndex = -1;

            if (stopWhenSceneHasNoPlaylist)
                FadeToClip(null);

            return;
        }

        if (currentPlaylist == playlist && currentSceneName == sceneName && activeSource.isPlaying)
            return;

        currentPlaylist = playlist;
        currentSceneName = sceneName;
        FadeToClip(firstClip);
    }

    public void StopMusic()
    {
        currentPlaylist = null;
        currentSceneName = string.Empty;
        currentTrackIndex = -1;
        FadeToClip(null);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshAfterSceneActivation(scene.name);
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        PlayForScene(newScene.name);
    }

    private void RefreshAfterSceneActivation(string fallbackSceneName)
    {
        if (sceneRefreshRoutine != null)
            StopCoroutine(sceneRefreshRoutine);

        sceneRefreshRoutine = StartCoroutine(RefreshAfterSceneActivationRoutine(fallbackSceneName));
    }

    private IEnumerator RefreshAfterSceneActivationRoutine(string fallbackSceneName)
    {
        yield return null;

        Scene activeScene = SceneManager.GetActiveScene();
        PlayForScene(activeScene.IsValid() ? activeScene.name : fallbackSceneName);
        sceneRefreshRoutine = null;
    }

    private void PlayNextTrack(bool fade)
    {
        if (currentPlaylist == null || currentPlaylist.Tracks.Count == 0)
            return;

        if (!TrySelectNextTrack(out AudioClip clip))
            return;

        if (fade)
            FadeToClip(clip);
        else
            PlayImmediately(clip);
    }

    private bool TrySelectFirstTrack(SceneMusicPlaylist playlist, out AudioClip clip)
    {
        clip = null;

        if (playlist == null || playlist.Tracks == null)
            return false;

        for (int i = 0; i < playlist.Tracks.Count; i++)
        {
            if (playlist.Tracks[i] == null)
                continue;

            currentTrackIndex = i;
            clip = playlist.Tracks[i];
            return true;
        }

        return false;
    }

    private bool TrySelectNextTrack(out AudioClip clip)
    {
        clip = null;

        if (currentPlaylist == null || currentPlaylist.Tracks == null || currentPlaylist.Tracks.Count == 0)
            return false;

        for (int i = 0; i < currentPlaylist.Tracks.Count; i++)
        {
            currentTrackIndex++;
            if (currentTrackIndex >= currentPlaylist.Tracks.Count)
                currentTrackIndex = 0;

            if (currentPlaylist.Tracks[currentTrackIndex] == null)
                continue;

            clip = currentPlaylist.Tracks[currentTrackIndex];
            return true;
        }

        return false;
    }

    private SceneMusicPlaylist FindPlaylist(string sceneName)
    {
        for (int i = 0; i < scenePlaylists.Count; i++)
        {
            SceneMusicPlaylist playlist = scenePlaylists[i];
            if (playlist == null)
                continue;

            if (string.Equals(playlist.SceneName, sceneName, System.StringComparison.OrdinalIgnoreCase))
                return playlist;
        }

        return null;
    }

    private AudioSource CreateMusicSource(string sourceName)
    {
        GameObject sourceObject = new GameObject(sourceName);
        sourceObject.transform.SetParent(transform);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.outputAudioMixerGroup = outputMixerGroup;
        return source;
    }

    private void FadeToClip(AudioClip clip)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadingSource.Stop();
        fadingSource.clip = null;
        fadingSource.volume = 0f;

        fadeRoutine = StartCoroutine(FadeToClipRoutine(clip));
    }

    private IEnumerator FadeToClipRoutine(AudioClip clip)
    {
        AudioSource oldSource = activeSource;
        AudioSource newSource = fadingSource;

        if (clip != null)
        {
            newSource.outputAudioMixerGroup = outputMixerGroup;
            newSource.clip = clip;
            newSource.volume = 0f;
            newSource.Play();
        }

        float startOldVolume = oldSource.volume;
        float elapsed = 0f;
        float duration = Mathf.Max(0f, fadeSeconds);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

            oldSource.volume = Mathf.Lerp(startOldVolume, 0f, t);
            if (clip != null)
                newSource.volume = Mathf.Lerp(0f, volume, t);

            yield return null;
        }

        oldSource.Stop();
        oldSource.clip = null;
        oldSource.volume = 0f;

        if (clip != null)
            newSource.volume = volume;

        activeSource = newSource;
        fadingSource = oldSource;
        fadeRoutine = null;
    }

    private void PlayImmediately(AudioClip clip)
    {
        activeSource.Stop();
        activeSource.outputAudioMixerGroup = outputMixerGroup;
        activeSource.clip = clip;
        activeSource.volume = volume;
        activeSource.Play();
    }
}

[System.Serializable]
public sealed class SceneMusicPlaylist
{
    [SerializeField] private string sceneName;
    [SerializeField] private List<AudioClip> tracks = new List<AudioClip>();

    public string SceneName => sceneName;
    public List<AudioClip> Tracks => tracks;
}

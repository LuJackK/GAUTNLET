using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class AtmosphereProfileApplier : MonoBehaviour
{
    [SerializeField] private AtmosphereProfile profile;
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool reapplyOnSceneLoaded = true;
    [SerializeField] private bool keepAcrossScenes = true;

    private static AtmosphereProfileApplier persistentInstance;

    public AtmosphereProfile Profile => profile;

    private void Awake()
    {
        if (keepAcrossScenes)
        {
            if (persistentInstance != null && persistentInstance != this)
            {
                Destroy(gameObject);
                return;
            }

            persistentInstance = this;
            DontDestroyOnLoad(gameObject);
        }

        if (applyOnAwake)
        {
            Apply();
        }
    }

    private void OnEnable()
    {
        if (reapplyOnSceneLoaded)
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }
    }

    private void OnDisable()
    {
        if (reapplyOnSceneLoaded)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        if (persistentInstance == this)
        {
            persistentInstance = null;
        }
    }

    public void SetProfile(AtmosphereProfile atmosphereProfile)
    {
        profile = atmosphereProfile;
    }

    public void Apply()
    {
        if (profile != null)
        {
            profile.Apply();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply();
    }
}

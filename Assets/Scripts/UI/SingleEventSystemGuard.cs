using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using FishNet.Object;

namespace GAUNTLET.UI
{
    /// <summary>
    /// Ensures only one active EventSystem exists after scene transitions.
    /// Prevents duplicate EventSystem warnings and input focus conflicts.
    /// </summary>
    public static class SingleEventSystemGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnforceSingleEventSystem("Initial");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnforceSingleEventSystem($"SceneLoaded:{scene.name}");
        }

        private static void EnforceSingleEventSystem(string phase)
        {
            EventSystem[] systems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            if (systems == null || systems.Length <= 1)
                return;

            EventSystem keep = null;

            // Prefer a non-networked (scene-level) EventSystem.
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].isActiveAndEnabled && !IsOnNetworkedObjectPath(systems[i]))
                {
                    keep = systems[i];
                    break;
                }
            }

            // Otherwise keep any active EventSystem.
            for (int i = 0; i < systems.Length; i++)
            {
                if (keep == null && systems[i] != null && systems[i].isActiveAndEnabled)
                {
                    keep = systems[i];
                    break;
                }
            }

            if (keep == null)
                keep = systems[0];

            for (int i = 0; i < systems.Length; i++)
            {
                EventSystem es = systems[i];
                if (es == null || es == keep)
                    continue;

                if (IsOnNetworkedObjectPath(es))
                {
                    BaseInputModule[] modules = es.GetComponents<BaseInputModule>();
                    for (int j = 0; j < modules.Length; j++)
                    {
                        if (modules[j] != null)
                            Object.Destroy(modules[j]);
                    }

                    Object.Destroy(es);
                }
                else
                {
                    es.gameObject.SetActive(false);
                }
            }

            Debug.Log($"[SingleEventSystemGuard] {phase}: kept '{keep.name}', disabled {systems.Length - 1} duplicate EventSystem(s).");
        }

        private static bool IsOnNetworkedObjectPath(EventSystem system)
        {
            return system != null && system.GetComponentInParent<NetworkObject>() != null;
        }
    }
}

using System;
using UnityEngine;
using TMPro;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting.Tugboat;
using System.Collections;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

public class MenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";
    [SerializeField] private string gameMapSceneName = "Map1";

    [Header("Networking")]
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private GameObject menuCanvas;
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private MapSelectScreen mapSelectScreen;
    private Tugboat tugboat;
    private bool isStartingHost;

    private void Start()
    {
        DontDestroyOnLoad(gameObject);

        if (networkManager == null)
        {
            networkManager = InstanceFinder.NetworkManager;
        }

        if (networkManager != null)
        {
            tugboat = networkManager.TransportManager.GetTransport<Tugboat>();
        }
        else
        {
            Debug.LogError("MenuController: NetworkManager not found!");
        }

        ResolveMapSelectScreen();
        if (mapSelectScreen != null)
        {
            mapSelectScreen.MapSelected.RemoveListener(OnMapSelectedForHosting);
            mapSelectScreen.MapSelected.AddListener(OnMapSelectedForHosting);
            mapSelectScreen.Hide();
        }
    }

    private void OnDestroy()
    {
        if (mapSelectScreen != null)
        {
            mapSelectScreen.MapSelected.RemoveListener(OnMapSelectedForHosting);
        }
    }

    public void OnTutorialClicked()
    {
        if (networkManager == null) return;

        Debug.Log("Starting Tutorial (Local Host)...");
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();

        MenuCoroutineRunner.Run(LoadTutorialWhenStarted());
    }

    private System.Collections.IEnumerator LoadTutorialWhenStarted()
    {
        while (networkManager != null && !networkManager.ServerManager.Started)
        {
            yield return null;
        }

        if (networkManager != null)
        {
            // Use global scene loading so late joiners also receive this scene.
            SceneLookupData lookup = new SceneLookupData(tutorialSceneName);
            SceneLoadData sld = new SceneLoadData(lookup);
            sld.ReplaceScenes = ReplaceOption.All;
            sld.PreferredActiveScene = new PreferredScene(lookup);
            networkManager.SceneManager.LoadGlobalScenes(sld);

            if (menuCanvas != null) menuCanvas.SetActive(false);
        }
    }

    public void OnJoinClicked()
    {
        if (networkManager == null || tugboat == null) return;

        string ip = "localhost";
        if (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
        {
            ip = ipInputField.text;
        }

        Debug.Log($"Joining Game at {ip}...");
        tugboat.SetClientAddress(ip);
        networkManager.ClientManager.StartConnection();
        
        if (menuCanvas != null) menuCanvas.SetActive(false);
    }

    public void OnHostClicked()
    {
        if (networkManager == null) return;

        ResolveMapSelectScreen();
        if (mapSelectScreen != null && mapSelectScreen.HasSelectableMaps)
        {
            mapSelectScreen.Show();
            return;
        }

        StartHostingGame(gameMapSceneName);
    }

    private void ResolveMapSelectScreen()
    {
        if (mapSelectScreen != null)
            return;

        mapSelectScreen = GetComponent<MapSelectScreen>();
        if (mapSelectScreen == null)
            mapSelectScreen = FindFirstObjectByType<MapSelectScreen>(FindObjectsInactive.Include);
    }

    private void OnMapSelectedForHosting(string selectedSceneName)
    {
        StartHostingGame(string.IsNullOrEmpty(selectedSceneName) ? gameMapSceneName : selectedSceneName);
    }

    private void StartHostingGame(string selectedSceneName)
    {
        if (networkManager == null) return;
        if (isStartingHost) return;
        isStartingHost = true;

        MenuCoroutineRunner.Run(StartHostAfterMapLoad(selectedSceneName));
    }

    private IEnumerator StartHostAfterMapLoad(string selectedSceneName)
    {
        Debug.Log($"Hosting Game on map: {selectedSceneName}...");
        networkManager.ServerManager.StartConnection();

        // Wait until the server is actually active
        while (networkManager != null && !networkManager.ServerManager.Started)
        {
            yield return null;
        }

        if (networkManager == null || string.IsNullOrEmpty(selectedSceneName))
        {
            isStartingHost = false;
            yield break;
        }

        bool loadFinished = false;
        bool loadSucceeded = false;
        Action<SceneLoadEndEventArgs> loadEndHandler = args =>
        {
            if (!args.QueueData.AsServer)
                return;

            loadFinished = true;
            loadSucceeded = DidLoadScene(args, selectedSceneName);
        };

        Debug.Log($"Server started! Loading scene before host client joins: {selectedSceneName}...");
        networkManager.SceneManager.OnLoadEnd += loadEndHandler;

        SceneLookupData lookup = new SceneLookupData(selectedSceneName);
        SceneLoadData sld = new SceneLoadData(lookup);
        // Global load keeps host and future joining clients on the same map.
        sld.ReplaceScenes = ReplaceOption.All;
        sld.PreferredActiveScene = new PreferredScene(lookup);
        networkManager.SceneManager.LoadGlobalScenes(sld);

        const int maxWaitFrames = 600;
        int waitedFrames = 0;
        while (!loadFinished && waitedFrames < maxWaitFrames)
        {
            if (UnitySceneManager.GetSceneByName(selectedSceneName).isLoaded)
            {
                loadFinished = true;
                loadSucceeded = true;
                break;
            }

            waitedFrames++;
            yield return null;
        }

        networkManager.SceneManager.OnLoadEnd -= loadEndHandler;

        if (!loadSucceeded)
        {
            Debug.LogError($"MenuController: Timed out or failed while loading map '{selectedSceneName}'. Host client will not start in the menu scene.");
            isStartingHost = false;
            yield break;
        }

        if (menuCanvas != null) menuCanvas.SetActive(false);

        Debug.Log($"Map '{selectedSceneName}' loaded. Starting local host client...");
        networkManager.ClientManager.StartConnection();
    }

    private static bool DidLoadScene(SceneLoadEndEventArgs args, string sceneName)
    {
        for (int i = 0; i < args.LoadedScenes.Length; i++)
        {
            if (args.LoadedScenes[i].name == sceneName)
                return true;
        }

        for (int i = 0; i < args.SkippedSceneNames.Length; i++)
        {
            if (args.SkippedSceneNames[i] == sceneName)
                return true;
        }

        return UnitySceneManager.GetSceneByName(sceneName).isLoaded;
    }

    private sealed class MenuCoroutineRunner : MonoBehaviour
    {
        private static MenuCoroutineRunner instance;

        public static void Run(IEnumerator routine)
        {
            if (routine == null)
                return;

            if (instance == null)
            {
                GameObject runnerObject = new GameObject("Menu Coroutine Runner");
                DontDestroyOnLoad(runnerObject);
                instance = runnerObject.AddComponent<MenuCoroutineRunner>();
            }

            instance.StartCoroutine(routine);
        }
    }
}

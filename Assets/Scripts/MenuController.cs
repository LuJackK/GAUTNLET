using UnityEngine;
using TMPro;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting.Tugboat;

public class MenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";
    [SerializeField] private string gameMapSceneName = "Map1";

    [Header("Networking")]
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private GameObject menuCanvas;
    [SerializeField] private NetworkManager networkManager;
    private Tugboat tugboat;

    private void Start()
    {
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
    }

    public void OnTutorialClicked()
    {
        if (networkManager == null) return;

        Debug.Log("Starting Tutorial (Local Host)...");
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        
        if (menuCanvas != null) menuCanvas.SetActive(false);

        StartCoroutine(LoadTutorialWhenStarted());
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

        Debug.Log("Hosting Game...");
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        
        if (menuCanvas != null) menuCanvas.SetActive(false);

        StartCoroutine(LoadSceneWhenStarted());
    }

    private System.Collections.IEnumerator LoadSceneWhenStarted()
    {
        // Wait until the server is actually active
        while (networkManager != null && !networkManager.ServerManager.Started)
        {
            yield return null;
        }

        if (networkManager != null && !string.IsNullOrEmpty(gameMapSceneName))
        {
            Debug.Log($"Server started! Loading scene: {gameMapSceneName}...");
            SceneLookupData lookup = new SceneLookupData(gameMapSceneName);
            SceneLoadData sld = new SceneLoadData(lookup);
            // Global load keeps host and future joining clients on the same map.
            sld.ReplaceScenes = ReplaceOption.All;
            sld.PreferredActiveScene = new PreferredScene(lookup);
            networkManager.SceneManager.LoadGlobalScenes(sld);
        }
    }
}

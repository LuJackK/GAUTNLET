using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";

    public void OnTutorialClicked()
    {
        Debug.Log("Loading Tutorial Scene...");
        SceneManager.LoadScene(tutorialSceneName);
    }

    public void OnJoinClicked()
    {
        Debug.Log("Join clicked - Logic not yet implemented");
    }

    public void OnHostClicked()
    {
        Debug.Log("Host clicked - Logic not yet implemented");
    }
}

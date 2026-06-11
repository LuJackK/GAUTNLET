using FishNet;
using FishNet.Managing;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GAUNTLET.UI
{
    [AddComponentMenu("GAUNTLET/UI/In-Game Menu")]
    public class InGameMenu : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "MenuScreen";

        [Header("Appearance")]
        [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color panelColor = new Color(0.055f, 0.055f, 0.065f, 0.96f);
        [SerializeField] private Color buttonColor = new Color(0.14f, 0.14f, 0.17f, 1f);
        [SerializeField] private Color buttonHighlightColor = new Color(0.9f, 0.18f, 0.04f, 1f);

        private const string MenuObjectName = "In-Game Menu";

        private static InGameMenu instance;

        private GameObject root;
        private Button resumeButton;
        private CursorLockMode previousLockState;
        private bool previousCursorVisible;
        private bool isOpen;
        private bool isReturningToMenu;

        public static bool IsOpen => instance != null && instance.isOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            GameObject menuObject = new GameObject(MenuObjectName);
            DontDestroyOnLoad(menuObject);
            instance = menuObject.AddComponent<InGameMenu>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            BuildMenu();
            SetOpen(false, false);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (isOpen)
                RestoreGameplayState();
        }

        private void Update()
        {
            if (isReturningToMenu)
                return;

            if (Input.GetKeyDown(toggleKey) && CanOpenInCurrentScene())
                SetOpen(!isOpen, true);
        }

        public void ResumeGame()
        {
            SetOpen(false, true);
        }

        public void ReturnToMainMenu()
        {
            if (isReturningToMenu)
                return;

            isReturningToMenu = true;
            CloseForExit();
            StopNetworkConnections();
            SceneManager.LoadScene(mainMenuSceneName);
        }

        public void QuitGame()
        {
            CloseForExit();
            StopNetworkConnections();

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            isReturningToMenu = false;

            if (scene.name == mainMenuSceneName)
                CloseForExit();
        }

        private bool CanOpenInCurrentScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return activeScene.IsValid()
                && activeScene.isLoaded
                && activeScene.name != mainMenuSceneName;
        }

        private void SetOpen(bool open, bool rememberGameplayState)
        {
            if (open == isOpen)
            {
                if (root != null)
                    root.SetActive(open);

                return;
            }

            isOpen = open;

            if (root != null)
                root.SetActive(open);

            if (open)
            {
                if (rememberGameplayState)
                    CaptureGameplayState();

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SelectButton(resumeButton);
            }
            else
            {
                RestoreGameplayState();
            }
        }

        private void CaptureGameplayState()
        {
            previousLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
        }

        private void RestoreGameplayState()
        {
            Cursor.lockState = previousLockState;
            Cursor.visible = previousCursorVisible;
        }

        private void CloseForExit()
        {
            isOpen = false;

            if (root != null)
                root.SetActive(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void StopNetworkConnections()
        {
            NetworkManager networkManager = InstanceFinder.NetworkManager;
            if (networkManager == null)
                return;

            if (networkManager.ClientManager.Started)
                networkManager.ClientManager.StopConnection();

            if (networkManager.ServerManager.Started)
                networkManager.ServerManager.StopConnection(true);
        }

        private void BuildMenu()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            root = new GameObject("Root", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas.transform, false);

            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            root.GetComponent<Image>().color = overlayColor;

            GameObject panel = CreatePanel(root.transform);
            CreateTitle(panel.transform);

            Transform buttonStack = CreateButtonStack(panel.transform);
            resumeButton = CreateButton(buttonStack, "Resume", ResumeGame);
            CreateButton(buttonStack, "Main Menu", ReturnToMainMenu);
            CreateButton(buttonStack, "Quit Game", QuitGame);
        }

        private GameObject CreatePanel(Transform parent)
        {
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(440f, 420f);

            panel.GetComponent<Image>().color = panelColor;

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(42, 42, 36, 36);
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return panel;
        }

        private void CreateTitle(Transform parent)
        {
            GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            titleObject.transform.SetParent(parent, false);

            LayoutElement layoutElement = titleObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 64f;

            TextMeshProUGUI title = titleObject.GetComponent<TextMeshProUGUI>();
            title.text = "Menu";
            title.alignment = TextAlignmentOptions.Center;
            title.color = Color.white;
            title.fontSize = 48f;
            title.enableAutoSizing = true;
            title.fontSizeMin = 24f;
            title.fontSizeMax = 48f;
        }

        private Transform CreateButtonStack(Transform parent)
        {
            GameObject stack = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            stack.transform.SetParent(parent, false);

            LayoutElement layoutElement = stack.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 198f;

            VerticalLayoutGroup layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return stack.transform;
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 58f;

            Image image = buttonObject.GetComponent<Image>();
            image.color = buttonColor;

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = buttonHighlightColor;
            colors.selectedColor = buttonHighlightColor;
            colors.pressedColor = buttonHighlightColor * 0.8f;
            button.colors = colors;
            button.onClick.AddListener(action);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);

            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(14f, 0f);
            labelRect.offsetMax = new Vector2(-14f, 0f);

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 26f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 26f;

            return button;
        }

        private static void SelectButton(Button button)
        {
            if (button == null || EventSystem.current == null)
                return;

            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[AddComponentMenu("GAUNTLET/UI/Map Select Screen")]
public class MapSelectScreen : MonoBehaviour
{
    [Serializable]
    public class MapOption
    {
        public string displayName = "Map";
        public Sprite previewImage;
        public string sceneName = "Map1";
    }

    [Header("Maps")]
    [SerializeField] private List<MapOption> maps = new List<MapOption>();

    [Header("Screen References")]
    [SerializeField] private Canvas menuCanvas;
    [SerializeField] private GameObject mainMenuRoot;
    [SerializeField] private GameObject screenRoot;
    [SerializeField] private Transform mapButtonContainer;
    [SerializeField] private GameObject mapButtonPrefab;
    [SerializeField] private Button backButton;

    [Header("Generated Layout")]
    [SerializeField] private Vector2 mapButtonSize = new Vector2(190f, 190f);
    [SerializeField] private float mapButtonSpacing = 18f;
    [SerializeField] private Color cardColor = new Color(0.06f, 0.06f, 0.07f, 0.92f);
    [SerializeField] private Color cardHighlightColor = new Color(0.9f, 0.18f, 0.04f, 1f);
    [SerializeField] private Color screenTint = new Color(0f, 0f, 0f, 0.55f);

    [Header("Events")]
    [SerializeField] private UnityEvent<string> mapSelected = new UnityEvent<string>();

    private readonly List<GameObject> spawnedButtons = new List<GameObject>();

    public UnityEvent<string> MapSelected => mapSelected;

    public bool HasSelectableMaps
    {
        get
        {
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && !string.IsNullOrEmpty(maps[i].sceneName))
                    return true;
            }

            return false;
        }
    }

    private void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(Hide);
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(Hide);
    }

    public void Show()
    {
        EnsureScreen();
        RebuildMapButtons();

        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(false);

        if (screenRoot != null)
            screenRoot.SetActive(true);
    }

    public void Hide()
    {
        if (screenRoot != null)
            screenRoot.SetActive(false);

        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(true);
    }

    private void SelectMap(MapOption mapOption)
    {
        if (mapOption == null || string.IsNullOrEmpty(mapOption.sceneName))
            return;

        mapSelected.Invoke(mapOption.sceneName);
    }

    private void RebuildMapButtons()
    {
        if (mapButtonContainer == null)
            return;

        ClearSpawnedButtons();
        EnsureContainerLayout();

        for (int i = 0; i < maps.Count; i++)
        {
            MapOption mapOption = maps[i];
            if (mapOption == null || string.IsNullOrEmpty(mapOption.sceneName))
                continue;

            GameObject buttonObject = mapButtonPrefab != null
                ? Instantiate(mapButtonPrefab, mapButtonContainer)
                : CreateDefaultMapButton(mapButtonContainer);

            buttonObject.name = $"MapButton_{GetDisplayName(mapOption)}";
            ConfigureMapButton(buttonObject, mapOption);
            spawnedButtons.Add(buttonObject);
        }
    }

    private void ClearSpawnedButtons()
    {
        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            if (spawnedButtons[i] != null)
                Destroy(spawnedButtons[i]);
        }

        spawnedButtons.Clear();
    }

    private void ConfigureMapButton(GameObject buttonObject, MapOption mapOption)
    {
        Button button = buttonObject.GetComponent<Button>();
        if (button == null)
            button = buttonObject.AddComponent<Button>();

        Image[] images = buttonObject.GetComponentsInChildren<Image>(true);
        Image preview = FindChildImage(images, buttonObject.transform);
        if (preview != null)
        {
            preview.sprite = mapOption.previewImage;
            preview.color = mapOption.previewImage != null ? Color.white : new Color(1f, 1f, 1f, 0.18f);
            preview.preserveAspect = true;
        }

        TMP_Text label = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = GetDisplayName(mapOption);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => SelectMap(mapOption));
    }

    private Image FindChildImage(Image[] images, Transform root)
    {
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].transform != root)
                return images[i];
        }

        return images.Length > 0 ? images[0] : null;
    }

    private GameObject CreateDefaultMapButton(Transform parent)
    {
        GameObject root = new GameObject("MapButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.transform.SetParent(parent, false);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = mapButtonSize;

        Image background = root.GetComponent<Image>();
        background.color = cardColor;

        Button button = root.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = cardHighlightColor;
        colors.pressedColor = cardHighlightColor * 0.8f;
        colors.selectedColor = cardHighlightColor;
        button.colors = colors;

        LayoutElement layoutElement = root.GetComponent<LayoutElement>();
        layoutElement.preferredWidth = mapButtonSize.x;
        layoutElement.preferredHeight = mapButtonSize.y;

        GameObject previewObject = new GameObject("Preview", typeof(RectTransform), typeof(Image));
        previewObject.transform.SetParent(root.transform, false);
        RectTransform previewRect = (RectTransform)previewObject.transform;
        previewRect.anchorMin = new Vector2(0.08f, 0.28f);
        previewRect.anchorMax = new Vector2(0.92f, 0.92f);
        previewRect.offsetMin = Vector2.zero;
        previewRect.offsetMax = Vector2.zero;
        previewObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

        GameObject labelObject = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(root.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = new Vector2(0.08f, 0.04f);
        labelRect.anchorMax = new Vector2(0.92f, 0.25f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 26f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 26f;

        return root;
    }

    private void EnsureScreen()
    {
        if (screenRoot != null && mapButtonContainer != null)
            return;

        if (menuCanvas == null)
            menuCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

        if (screenRoot == null && menuCanvas != null)
            CreateDefaultScreen(menuCanvas.transform);
    }

    private void CreateDefaultScreen(Transform parent)
    {
        screenRoot = new GameObject("Map Select Screen", typeof(RectTransform), typeof(Image));
        screenRoot.transform.SetParent(parent, false);
        RectTransform screenRect = (RectTransform)screenRoot.transform;
        screenRect.anchorMin = Vector2.zero;
        screenRect.anchorMax = Vector2.one;
        screenRect.offsetMin = Vector2.zero;
        screenRect.offsetMax = Vector2.zero;
        screenRoot.GetComponent<Image>().color = screenTint;

        GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleObject.transform.SetParent(screenRoot.transform, false);
        RectTransform titleRect = (RectTransform)titleObject.transform;
        titleRect.anchorMin = new Vector2(0.1f, 0.82f);
        titleRect.anchorMax = new Vector2(0.9f, 0.95f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        TextMeshProUGUI title = titleObject.GetComponent<TextMeshProUGUI>();
        title.text = "Select Map";
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        title.fontSize = 52f;
        title.enableAutoSizing = true;
        title.fontSizeMin = 24f;
        title.fontSizeMax = 52f;

        GameObject containerObject = new GameObject("Map Button Container", typeof(RectTransform));
        containerObject.transform.SetParent(screenRoot.transform, false);
        RectTransform containerRect = (RectTransform)containerObject.transform;
        containerRect.anchorMin = new Vector2(0.08f, 0.22f);
        containerRect.anchorMax = new Vector2(0.92f, 0.78f);
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;
        mapButtonContainer = containerObject.transform;

        backButton = CreateBackButton(screenRoot.transform);
        backButton.onClick.AddListener(Hide);
    }

    private Button CreateBackButton(Transform parent)
    {
        GameObject root = new GameObject("Back", typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0.42f, 0.07f);
        rect.anchorMax = new Vector2(0.58f, 0.15f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = cardColor;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(root.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = "Back";
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 24f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 24f;

        return root.GetComponent<Button>();
    }

    private void EnsureContainerLayout()
    {
        if (mapButtonContainer == null)
            return;

        if (mapButtonContainer.GetComponent<LayoutGroup>() != null)
            return;

        GridLayoutGroup grid = mapButtonContainer.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = mapButtonSize;
        grid.spacing = new Vector2(mapButtonSpacing, mapButtonSpacing);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
    }

    private string GetDisplayName(MapOption mapOption)
    {
        if (!string.IsNullOrEmpty(mapOption.displayName))
            return mapOption.displayName;

        return mapOption.sceneName;
    }
}

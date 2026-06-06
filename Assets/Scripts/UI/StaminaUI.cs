using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Fragsurf.Movement;

public class StaminaUI : MonoBehaviour
{
    [Header("References")]
    public SurfCharacter player;
    public NetworkedCharacter networkedPlayer;
    public Transform barContainer;
    public GameObject barPrefab;
    public RectTransform healthContainer;
    public RectTransform staminaContainer;
    public GameObject healthItemPrefab;
    public Sprite healthItemSprite;
    public Sprite staminaFillSprite;

    [Header("Settings")]
    public bool autoFindPlayer = true;
    public bool autoCreateHud = true;
    public int fallbackMaxHealth = 3;
    public Vector2 barSize = new Vector2(34f, 180f);
    public float screenSideInset = 42f;
    public float healthItemSpacing = 4f;
    public float separatorThickness = 2f;
    public float regenPreviewSeconds = 1f;
    public Color activeColor = new Color(0.25f, 1f, 0.03f, 1f);
    public Color emptyColor = new Color(1, 1, 1, 0.2f);
    public Color healthColor = Color.red;
    public Color healthEmptyColor = new Color(0.35f, 0f, 0f, 0.25f);
    public Color staminaRegenPreviewColor = new Color(0.5f, 1f, 1f, 0.4f);
    public Color silverBorderColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);

    private readonly List<Image> _healthImages = new List<Image>();
    private readonly List<Image> _separatorImages = new List<Image>();
    private int _maxStaminaCached = -1;
    private int _maxHealthCached = -1;
    private Image _staminaFillImage;
    private Image _staminaRegenPreviewImage;
    private RectTransform _staminaFillRect;
    private RectTransform _staminaRegenPreviewRect;

    void Start()
    {
        ResolvePlayerReferences();
        if (IsPlayerReady())
            RebuildBars();
    }

    void Update()
    {
        ResolvePlayerReferences();

        if (!IsPlayerReady())
            return;

        if (_staminaFillImage == null || _healthImages.Count == 0)
            RebuildBars();

        int currentMax = Mathf.Max(1, Mathf.CeilToInt(player.movementConfig.maxStamina));
        int currentMaxHealth = ResolveMaxHealth();
        if (currentMax != _maxStaminaCached || currentMaxHealth != _maxHealthCached)
            RebuildBars();

        UpdateHealthItems(ResolveCurrentHealth());
        UpdateStaminaFill();
    }

    void RebuildBars()
    {
        EnsureHudContainers();

        if (healthContainer != null) {
            ClearChildren(healthContainer);
            _healthImages.Clear();
        }

        if (staminaContainer != null) {
            ClearChildren(staminaContainer);
            _separatorImages.Clear();
            _staminaFillImage = null;
            _staminaRegenPreviewImage = null;
            _staminaFillRect = null;
            _staminaRegenPreviewRect = null;
        }

        if (player == null || player.movementConfig == null)
            return;

        _maxHealthCached = ResolveMaxHealth();
        _maxStaminaCached = Mathf.Max(1, Mathf.CeilToInt(player.movementConfig.maxStamina));

        BuildHealthItems();
        BuildStaminaBar();
        UpdateHealthItems(ResolveCurrentHealth());
        UpdateStaminaFill();
    }

    private void ResolvePlayerReferences()
    {
        if (autoFindPlayer && TryResolveOwnedNetworkedPlayer(out NetworkedCharacter ownedPlayer)) {
            if (networkedPlayer != ownedPlayer) {
                networkedPlayer = ownedPlayer;
                player = ownedPlayer.GetComponent<SurfCharacter>();
                _staminaFillImage = null;
            }
            return;
        }

        if (networkedPlayer != null && player == null)
            player = networkedPlayer.GetComponent<SurfCharacter>();

        if (player != null) {
            if (networkedPlayer == null)
                networkedPlayer = player.GetComponent<NetworkedCharacter>();
            return;
        }

        if (!autoFindPlayer)
            return;

        if (HasNetworkedCharacters())
            return;

        SurfCharacter foundPlayer = FindFirstObjectByType<SurfCharacter>();
        if (foundPlayer != null) {
            player = foundPlayer;
            networkedPlayer = foundPlayer.GetComponent<NetworkedCharacter>();
        }
    }

    private bool TryResolveOwnedNetworkedPlayer(out NetworkedCharacter ownedPlayer)
    {
        NetworkedCharacter[] networkedCharacters = FindObjectsByType<NetworkedCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (NetworkedCharacter candidate in networkedCharacters) {
            if (candidate != null && candidate.IsOwner) {
                ownedPlayer = candidate;
                return true;
            }
        }

        ownedPlayer = null;
        return false;
    }

    private bool HasNetworkedCharacters()
    {
        NetworkedCharacter[] networkedCharacters = FindObjectsByType<NetworkedCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (NetworkedCharacter candidate in networkedCharacters) {
            if (candidate != null)
                return true;
        }

        return false;
    }

    private void EnsureHudContainers()
    {
        if (staminaContainer == null && barContainer != null)
            staminaContainer = barContainer as RectTransform;

        if (!autoCreateHud)
            return;

        RectTransform canvasRoot = ResolveCanvasRoot();
        if (canvasRoot == null)
            return;

        if (healthContainer == null)
            healthContainer = EnsureContainer(canvasRoot, "Health Bar", true);

        if (staminaContainer == null)
            staminaContainer = EnsureContainer(canvasRoot, "Stamina Bar", false);
        if (barContainer == null)
            barContainer = staminaContainer;

        ConfigureSideContainer(healthContainer, true);
        ConfigureSideContainer(staminaContainer, false);
        ConfigureBarFrame(healthContainer);
        ConfigureBarFrame(staminaContainer);
    }

    private RectTransform ResolveCanvasRoot()
    {
        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null && barContainer != null)
            canvas = barContainer.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (canvas == null) {
            GameObject canvasObject = new GameObject("Vitals Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        return canvas.transform as RectTransform;
    }

    private RectTransform EnsureContainer(RectTransform canvasRoot, string containerName, bool leftSide)
    {
        Transform existing = canvasRoot.Find(containerName);
        GameObject containerObject = existing != null
            ? existing.gameObject
            : new GameObject(containerName, typeof(RectTransform));

        containerObject.transform.SetParent(canvasRoot, false);
        RectTransform rect = containerObject.GetComponent<RectTransform>();
        ConfigureSideContainer(rect, leftSide);
        return rect;
    }

    private void ConfigureSideContainer(RectTransform rect, bool leftSide)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(leftSide ? 0f : 1f, 0.5f);
        rect.anchorMax = new Vector2(leftSide ? 0f : 1f, 0.5f);
        rect.pivot = new Vector2(leftSide ? 0f : 1f, 0.5f);
        rect.anchoredPosition = new Vector2(leftSide ? screenSideInset : -screenSideInset, 0f);
        rect.sizeDelta = barSize;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        LayoutGroup layoutGroup = rect.GetComponent<LayoutGroup>();
        if (layoutGroup != null)
            layoutGroup.enabled = false;

        ContentSizeFitter contentSizeFitter = rect.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
            contentSizeFitter.enabled = false;
    }

    private void ConfigureBarFrame(RectTransform rect)
    {
        if (rect == null)
            return;

        Image frame = rect.GetComponent<Image>();
        if (frame == null)
            frame = rect.gameObject.AddComponent<Image>();

        frame.color = backgroundColor;
        frame.raycastTarget = false;

        Outline outline = rect.GetComponent<Outline>();
        if (outline == null)
            outline = rect.gameObject.AddComponent<Outline>();

        outline.effectColor = silverBorderColor;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;
    }

    private void BuildHealthItems()
    {
        if (healthContainer == null)
            return;

        int maxHealth = Mathf.Max(1, _maxHealthCached);
        float totalSpacing = Mathf.Max(0, maxHealth - 1) * healthItemSpacing;
        float itemHeight = Mathf.Max(1f, (barSize.y - totalSpacing) / maxHealth);

        for (int i = 0; i < maxHealth; i++) {
            GameObject slotObject = CreateDefaultImageObject("Health Item", healthContainer);
            RectTransform rect = slotObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, i * (itemHeight + healthItemSpacing));
            rect.sizeDelta = new Vector2(0f, itemHeight);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            Image slotImage = slotObject.GetComponent<Image>();
            slotImage.color = healthItemPrefab == null ? healthColor : Color.clear;

            GameObject itemObject = slotObject;
            if (healthItemPrefab != null) {
                itemObject = Instantiate(healthItemPrefab, rect);
                RectTransform itemRect = itemObject.GetComponent<RectTransform>();
                if (itemRect != null)
                    StretchToParent(itemRect);
                else {
                    itemObject.transform.localPosition = Vector3.zero;
                    itemObject.transform.localRotation = Quaternion.identity;
                    itemObject.transform.localScale = Vector3.one;
                }
            }

            Image image = healthItemPrefab != null ? ResolveFillImage(itemObject) : slotImage;
            if (image == null)
                image = slotImage;

            if (healthItemSprite != null)
                image.sprite = healthItemSprite;

            image.type = Image.Type.Simple;
            image.color = healthColor;
            image.raycastTarget = false;
            EnsureOutline(slotObject);
            _healthImages.Add(image);
        }
    }

    private void BuildStaminaBar()
    {
        if (staminaContainer == null)
            return;

        Image emptyImage = CreateImage("Empty", staminaContainer, emptyColor);
        StretchToParent(emptyImage.rectTransform);

        _staminaRegenPreviewImage = CreateFilledImage("Regen Preview", staminaContainer, staminaRegenPreviewColor);
        _staminaFillImage = CreateFilledImage("Fill", staminaContainer, activeColor);
        _staminaRegenPreviewRect = _staminaRegenPreviewImage.rectTransform;
        _staminaFillRect = _staminaFillImage.rectTransform;

        if (staminaFillSprite != null) {
            _staminaRegenPreviewImage.sprite = staminaFillSprite;
            _staminaFillImage.sprite = staminaFillSprite;
        } else if (barPrefab != null) {
            Image importedFill = ResolveFillImage(barPrefab);
            if (importedFill != null && importedFill.sprite != null) {
                _staminaRegenPreviewImage.sprite = importedFill.sprite;
                _staminaFillImage.sprite = importedFill.sprite;
            }
        }

        for (int i = 1; i < _maxStaminaCached; i++) {
            Image separator = CreateImage("Stamina Separator", staminaContainer, silverBorderColor);
            RectTransform separatorRect = separator.rectTransform;
            float normalizedPosition = i / (float)_maxStaminaCached;
            separatorRect.anchorMin = new Vector2(0f, normalizedPosition);
            separatorRect.anchorMax = new Vector2(1f, normalizedPosition);
            separatorRect.pivot = new Vector2(0.5f, 0.5f);
            separatorRect.anchoredPosition = Vector2.zero;
            separatorRect.sizeDelta = new Vector2(0f, separatorThickness);
            _separatorImages.Add(separator);
        }
    }

    private void UpdateHealthItems(int currentHealth)
    {
        currentHealth = Mathf.Clamp(currentHealth, 0, _healthImages.Count);
        for (int i = 0; i < _healthImages.Count; i++) {
            Image image = _healthImages[i];
            if (image == null)
                continue;

            image.color = i < currentHealth ? healthColor : healthEmptyColor;
        }
    }

    private void UpdateStaminaFill()
    {
        if (_staminaFillRect == null || _staminaRegenPreviewRect == null || _staminaRegenPreviewImage == null || player == null || player.movementConfig == null)
            return;

        float maxStamina = Mathf.Max(1f, player.movementConfig.maxStamina);
        float currentStamina = Mathf.Clamp(player.moveData.stamina, 0f, maxStamina);
        float fillAmount = currentStamina / maxStamina;
        SetVerticalSegment(_staminaFillRect, 0f, fillAmount);

        bool belowMax = currentStamina < maxStamina;
        float previewStamina = belowMax
            ? currentStamina + Mathf.Max(0f, player.movementConfig.staminaRegenRate) * Mathf.Max(0f, regenPreviewSeconds)
            : currentStamina;
        float previewAmount = Mathf.Clamp01(previewStamina / maxStamina);
        SetVerticalSegment(_staminaRegenPreviewRect, fillAmount, previewAmount);
        _staminaRegenPreviewImage.enabled = belowMax && previewAmount > fillAmount;
    }

    private int ResolveCurrentHealth()
    {
        if (networkedPlayer != null)
            return networkedPlayer.CurrentHealth;

        return ResolveMaxHealth();
    }

    private int ResolveMaxHealth()
    {
        if (networkedPlayer != null)
            return networkedPlayer.MaxHealth;

        return Mathf.Max(1, fallbackMaxHealth);
    }

    private bool IsPlayerReady()
    {
        return player != null && player.moveData != null && player.movementConfig != null;
    }

    private Image CreateFilledImage(string objectName, RectTransform parent, Color color)
    {
        Image image = CreateImage(objectName, parent, color);
        image.type = Image.Type.Simple;
        StretchToParent(image.rectTransform);
        return image;
    }

    private Image CreateImage(string objectName, RectTransform parent, Color color)
    {
        GameObject imageObject = CreateDefaultImageObject(objectName, parent);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private GameObject CreateDefaultImageObject(string objectName, Transform parent)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        return imageObject;
    }

    private Image ResolveFillImage(GameObject target)
    {
        if (target == null)
            return null;

        Transform fillChild = target.transform.Find("Fill");
        if (fillChild != null && fillChild.TryGetComponent(out Image fillImage))
            return fillImage;

        return target.GetComponentInChildren<Image>(true);
    }

    private void EnsureOutline(GameObject target)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null)
            outline = target.AddComponent<Outline>();

        outline.effectColor = silverBorderColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;
    }

    private void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void SetVerticalSegment(RectTransform rect, float normalizedMin, float normalizedMax)
    {
        normalizedMin = Mathf.Clamp01(normalizedMin);
        normalizedMax = Mathf.Clamp01(Mathf.Max(normalizedMin, normalizedMax));

        rect.anchorMin = new Vector2(0f, normalizedMin);
        rect.anchorMax = new Vector2(1f, normalizedMax);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void ClearChildren(Transform parent)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--) {
            Transform child = parent.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}

internal sealed class PlayerVitalsHudBootstrap : MonoBehaviour
{
    private static PlayerVitalsHudBootstrap _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (_instance != null)
            return;

        PlayerVitalsHudBootstrap existing = FindFirstObjectByType<PlayerVitalsHudBootstrap>();
        if (existing != null) {
            _instance = existing;
            return;
        }

        GameObject bootstrapObject = new GameObject("Player Vitals HUD Bootstrap");
        DontDestroyOnLoad(bootstrapObject);
        _instance = bootstrapObject.AddComponent<PlayerVitalsHudBootstrap>();
    }

    private void Update()
    {
        if (FindFirstObjectByType<StaminaUI>() != null)
            return;

        if (FindFirstObjectByType<SurfCharacter>() == null &&
            FindFirstObjectByType<NetworkedCharacter>() == null)
            return;

        GameObject hudObject = new GameObject("Player Vitals HUD");
        hudObject.AddComponent<StaminaUI>();
    }
}

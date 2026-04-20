using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Fragsurf.Movement;

public class StaminaUI : MonoBehaviour
{
    [Header("References")]
    public SurfCharacter player;
    public Transform barContainer;
    public GameObject barPrefab;

    [Header("Settings")]
    public Color activeColor = Color.white;
    public Color emptyColor = new Color(1, 1, 1, 0.2f);

    private List<Image> _fillImages = new List<Image>();
    private int _maxStaminaCached = -1;

    void Start()
    {
        if (player != null && player.movementConfig != null)
        {
            RebuildBars();
        }
    }

    void Update()
    {
        if (player == null || player.moveData == null || player.movementConfig == null)
            return;

        // Check if max stamina changed (rare but possible), rebuild if needed
        int currentMax = Mathf.CeilToInt(player.movementConfig.maxStamina);
        if (currentMax != _maxStaminaCached)
        {
            RebuildBars();
        }

        float currentStamina = player.moveData.stamina;

        // Update each bar
        for (int i = 0; i < _fillImages.Count; i++)
        {
            float fillValue = 0f;

            // Example: Stamina 2.5
            // Bar 0 (Index 0): 2.5 - 0 = 2.5 -> Clamped to 1
            // Bar 1 (Index 1): 2.5 - 1 = 1.5 -> Clamped to 1
            // Bar 2 (Index 2): 2.5 - 2 = 0.5 -> Clamped to 0.5
            
            fillValue = Mathf.Clamp01(currentStamina - i);
            
            _fillImages[i].fillAmount = fillValue;
        }
    }

    void RebuildBars()
    {
        // Clear existing
        foreach (Transform child in barContainer)
        {
            Destroy(child.gameObject);
        }
        _fillImages.Clear();

        if (player == null || player.movementConfig == null || barPrefab == null) return;

        _maxStaminaCached = Mathf.CeilToInt(player.movementConfig.maxStamina);

        for (int i = 0; i < _maxStaminaCached; i++)
        {
            GameObject newBar = Instantiate(barPrefab, barContainer);
            
            // Find the image component intended for filling. 
            // We assume the prefab has a script or specific naming, but for simplicity:
            // If the prefab root has an Image and it's Filled, use it.
            // Or look for a child named "Fill". 
            
            Image fillImg = null;
            
            // Checks for a "Fill" child first (recommended setup)
            Transform fillChild = newBar.transform.Find("Fill");
            if (fillChild != null)
            {
                fillImg = fillChild.GetComponent<Image>();
            }
            else
            {
                // Fallback: check root
                fillImg = newBar.GetComponent<Image>();
            }

            if (fillImg != null)
            {
                _fillImages.Add(fillImg);
                fillImg.type = Image.Type.Filled; // Ensure it's filled
            }
            else
            {
                 Debug.LogWarning("StaminaUI: Bar Prefab must have an Image component named 'Fill' or on the root!");
            }
        }
    }
}

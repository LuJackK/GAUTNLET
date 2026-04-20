using UnityEngine;
using TMPro; // Required for TextMeshPro
using Fragsurf.Movement; // Required to access SurfCharacter

public class VelocityDisplay : MonoBehaviour
{
    [Header("References")]
    public SurfCharacter player;
    public TMP_Text uiText; // Changed from 'Text' to 'TMP_Text'

    [Header("Settings")]
    public bool showHorizontalOnly = true; 

    void Update()
    {
        if (player == null || uiText == null) return;

        // Get velocity from the MoveData struct
        Vector3 velocity = player.moveData.velocity;

        // Calculate speed
        float speed;
        if (showHorizontalOnly)
        {
            // Ignore vertical (Y) movement for "Surfing/Bhop" style speed
            velocity.y = 0;
            speed = velocity.magnitude;
        }
        else
        {
            // True 3D speed (including falling)
            speed = velocity.magnitude;
        }

        // Display the text (F0 = no decimals)
        uiText.text = $"Vel: {speed:F0}";
    }
}
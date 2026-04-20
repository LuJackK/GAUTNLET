using UnityEngine;

public class RotateWithCamera : MonoBehaviour
{
    public Transform cameraTransform;

    void Update()
    {
        if (cameraTransform == null) return;

        // Take only the Y rotation (Yaw) from the camera
        Vector3 eulerRotation = new Vector3(0, cameraTransform.eulerAngles.y, 0);
        transform.rotation = Quaternion.Euler(eulerRotation);
    }
}
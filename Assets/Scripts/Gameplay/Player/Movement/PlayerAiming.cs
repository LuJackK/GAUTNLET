using UnityEngine;
using Unity.Cinemachine;
using Fragsurf.Movement;

public class PlayerAiming : MonoBehaviour
{
	[Header("References")]
	public Transform bodyTransform;

	[Header("Sensitivity")]
	public float sensitivityMultiplier = 1f;
	public float horizontalSensitivity = 1f;
	public float verticalSensitivity   = 1f;

	[Header("Restrictions")]
	public float minYRotation = -90f;
	public float maxYRotation = 90f;
    
    [Header("Turn Clamping")]
    public float maxTurnSpeed = -1f; // Negative = unlimited

    [Header("Ownership")]
    [SerializeField] private bool requireLocalAuthority = true;

	// The real rotation of the camera without recoil.
	private Vector3 realRotation;
    private NetworkedCharacter _networkedCharacter;

	[Header("Aimpunch")]
	[Tooltip("bigger number makes the response more damped, smaller is less damped, currently the system will overshoot, with larger damping values it won't")]
	public float punchDamping = 9.0f;

	[Tooltip("bigger number increases the speed at which the view corrects")]
	public float punchSpringConstant = 65.0f;

	[HideInInspector]
	public Vector2 punchAngle;

	[HideInInspector]
	public Vector2 punchAngleVel;

	[Header("Cinemachine")]
	public CinemachineCamera freeLookCamera;

	private void Start()
	{
        _networkedCharacter = GetComponentInParent<NetworkedCharacter>();

		// Lock the mouse
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible   = false;
	}

	private void Update()
	{
        if (!HasLocalPresentationAuthority())
            return;

		// Fix pausing
		if (Mathf.Abs(Time.timeScale) <= 0)
			return;

		DecayPunchAngle();

		// Input
		float xMovement = Input.GetAxisRaw("Mouse X") * horizontalSensitivity * sensitivityMultiplier;
        
        if (maxTurnSpeed > 0f) {
            xMovement = Mathf.Clamp(xMovement, -maxTurnSpeed, maxTurnSpeed);
        }

		float yMovement = -Input.GetAxisRaw("Mouse Y") * verticalSensitivity  * sensitivityMultiplier;

		// Calculate real rotation from input
		realRotation   = new Vector3(Mathf.Clamp(realRotation.x + yMovement, minYRotation, maxYRotation), realRotation.y + xMovement, realRotation.z);
		realRotation.z = Mathf.Lerp(realRotation.z, 0f, Time.deltaTime * 3f);

		// Apply real rotation to body.
		bodyTransform.eulerAngles = Vector3.Scale(realRotation, new Vector3(0f, 1f, 0f));

		// Apply rotation and recoil.
		Vector3 cameraEulerPunchApplied = realRotation;
		cameraEulerPunchApplied.x += punchAngle.x;
		cameraEulerPunchApplied.y += punchAngle.y;


		if (freeLookCamera != null)
		{
			// Drive Cinemachine 3.x (CinemachineCamera)
            var orbital = freeLookCamera.GetComponent<CinemachineOrbitalFollow>();
            
            // Auto-disable conflicting input driver if present.
            var inputController = freeLookCamera.GetComponent<CinemachineInputAxisController>();
            if (inputController != null && inputController.enabled) {
                Debug.LogWarning("Disabling CinemachineInputAxisController to allow PlayerAiming clamping control.");
                inputController.enabled = false;
            }

            if (orbital != null) {
                // This component is local-presentation only. Remote avatars should not drive it.
                if (maxTurnSpeed > 0f) {
                     // Debug.Log($"Clamping active. Raw Input: {xMovement}, Clamped: {Mathf.Clamp(xMovement, -maxTurnSpeed, maxTurnSpeed)}");
                }

                orbital.HorizontalAxis.Value = cameraEulerPunchApplied.y;
                orbital.VerticalAxis.Value = cameraEulerPunchApplied.x;
            }
            else
            {
                Debug.LogError("PlayerAiming: CinemachineCamera assigned but 'CinemachineOrbitalFollow' component missing!");
            }
		}
		else 
		{
			// Standard Camera
			transform.eulerAngles = cameraEulerPunchApplied;
		}
	}

	public void ViewPunch(Vector2 punchAmount)
	{
		// Remove previous recoil.
		punchAngle = Vector2.zero;

		// Recoil go up.
		punchAngleVel -= punchAmount * 20;
	}

	private void DecayPunchAngle()
	{
		if (punchAngle.sqrMagnitude > 0.001 || punchAngleVel.sqrMagnitude > 0.001)
		{
			punchAngle += punchAngleVel * Time.deltaTime;
			float damping = 1 - (punchDamping * Time.deltaTime);

			if (damping < 0)
				damping = 0;

			punchAngleVel *= damping;

			float springForceMagnitude = punchSpringConstant * Time.deltaTime;
			punchAngleVel -= punchAngle * springForceMagnitude;
		}
		else
		{
			punchAngle    = Vector2.zero;
			punchAngleVel = Vector2.zero;
		}
	}

    private bool HasLocalPresentationAuthority() {
        if (!requireLocalAuthority)
            return true;

        if (_networkedCharacter == null)
            return true;

        if (!_networkedCharacter.IsClientInitialized && !_networkedCharacter.IsServerInitialized)
            return true;

        return _networkedCharacter.IsOwner;
    }
}

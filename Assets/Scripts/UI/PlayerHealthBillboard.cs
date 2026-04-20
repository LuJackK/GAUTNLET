using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.UI
{
    public class PlayerHealthBillboard : MonoBehaviour
    {
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.25f, 0f);
        [SerializeField] private float fontSize = 10f;
        [SerializeField] private Color textColor = Color.white;

        private NetworkedCharacter _networkedCharacter;
        private SurfCharacter _surfCharacter;
        private Transform _labelRoot;
        private TextMesh _label;
        private Camera _cachedCamera;

        public void Initialize(NetworkedCharacter networkedCharacter, SurfCharacter surfCharacter)
        {
            if (_networkedCharacter == networkedCharacter && _label != null)
            {
                UpdateLabel(networkedCharacter.CurrentHealth);
                return;
            }

            if (_networkedCharacter != null)
                _networkedCharacter.HealthChanged -= NetworkedCharacter_HealthChanged;

            _networkedCharacter = networkedCharacter;
            _surfCharacter = surfCharacter;

            EnsureLabelObjects();

            if (_networkedCharacter != null)
            {
                _networkedCharacter.HealthChanged += NetworkedCharacter_HealthChanged;
                UpdateLabel(_networkedCharacter.CurrentHealth);
            }
        }

        private void OnDestroy()
        {
            if (_networkedCharacter != null)
                _networkedCharacter.HealthChanged -= NetworkedCharacter_HealthChanged;
        }

        private void LateUpdate()
        {
            if (_labelRoot == null || _networkedCharacter == null)
                return;

            bool shouldShow = !_networkedCharacter.IsOwner;
            if (_labelRoot != null && _labelRoot.gameObject.activeSelf != shouldShow)
                _labelRoot.gameObject.SetActive(shouldShow);

            if (!shouldShow)
                return;

            Transform anchor = ResolveAnchor();
            _labelRoot.position = anchor.position + localOffset;

            Camera cameraToFace = ResolveCamera();
            if (cameraToFace == null)
                return;

            Vector3 forward = _labelRoot.position - cameraToFace.transform.position;
            if (forward.sqrMagnitude > 0.0001f)
                _labelRoot.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void NetworkedCharacter_HealthChanged(int prev, int next, bool asServer)
        {
            UpdateLabel(next);
        }

        private void EnsureLabelObjects()
        {
            if (_labelRoot == null)
            {
                Transform existing = transform.Find("HealthBillboard");
                _labelRoot = existing;

                if (_labelRoot == null)
                {
                    GameObject root = new GameObject("HealthBillboard");
                    root.transform.SetParent(transform, false);
                    _labelRoot = root.transform;
                }
            }

            if (_label == null)
            {
                _label = _labelRoot.GetComponent<TextMesh>();
                if (_label == null)
                    _label = _labelRoot.gameObject.AddComponent<TextMesh>();

                _label.alignment = TextAlignment.Center;
                _label.anchor = TextAnchor.MiddleCenter;
                _label.fontSize = Mathf.RoundToInt(fontSize * 10f);
                _label.characterSize = 0.18f;
                _label.color = textColor;
                _label.text = string.Empty;

                MeshRenderer renderer = _label.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sortingOrder = 100;

                _label.transform.localScale = Vector3.one * 0.45f;
            }
        }

        private void UpdateLabel(int health)
        {
            if (_label == null)
                EnsureLabelObjects();

            if (_label != null)
                _label.text = health.ToString();
        }

        private Transform ResolveAnchor()
        {
            return transform;
        }

        private Camera ResolveCamera()
        {
            if (_cachedCamera != null && _cachedCamera.isActiveAndEnabled)
                return _cachedCamera;

            _cachedCamera = Camera.main;
            if (_cachedCamera != null && _cachedCamera.isActiveAndEnabled)
                return _cachedCamera;

            _cachedCamera = FindFirstObjectByType<Camera>();
            return _cachedCamera != null && _cachedCamera.isActiveAndEnabled ? _cachedCamera : null;
        }
    }
}

using UnityEngine;

namespace Fragsurf.Movement {

    [RequireComponent(typeof(LineRenderer))]
    public class DashLineEffect : MonoBehaviour {

        private LineRenderer _lineRenderer;
        private Color _startColor;
        private float _lifetime = 0.15f;
        private float _age;
        private bool _playing;

        public bool IsAvailable => !_playing;

        private void Awake() {
            CacheLineRenderer();
        }

        public void Play(Vector3 start, Vector3 end, Color color, float width, float lifetime) {
            CacheLineRenderer();

            _startColor = color;
            _lifetime = Mathf.Max(0.01f, lifetime);
            _age = 0f;
            _playing = true;

            gameObject.SetActive(true);

            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, start);
            _lineRenderer.SetPosition(1, end);
            _lineRenderer.startWidth = Mathf.Max(0f, width);
            _lineRenderer.endWidth = Mathf.Max(0f, width);
            _lineRenderer.startColor = _startColor;
            _lineRenderer.endColor = _startColor;
        }

        private void Update() {
            if (!_playing)
                return;

            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _lifetime);

            if (t >= 1f) {
                _playing = false;
                gameObject.SetActive(false);
            }
        }

        private void CacheLineRenderer() {
            if (_lineRenderer != null)
                return;

            _lineRenderer = GetComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.textureMode = LineTextureMode.Stretch;
            _lineRenderer.alignment = LineAlignment.View;
            _lineRenderer.numCapVertices = 2;
            _lineRenderer.numCornerVertices = 2;
            _lineRenderer.positionCount = 2;
        }
    }
}

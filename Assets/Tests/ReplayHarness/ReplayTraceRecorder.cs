using Fragsurf.Movement;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalInputCollector))]
    public sealed class ReplayTraceRecorder : MonoBehaviour {
        [SerializeField] private ReplayTraceAsset _targetAsset;
        [SerializeField] private bool _captureOnEnable = true;
        [SerializeField] private string _traceId = "live-capture";

        private LocalInputCollector _inputCollector;
        private SurfCharacter _surfCharacter;
        private bool _isCapturing;

        private void Awake() {
            _inputCollector = GetComponent<LocalInputCollector>();
            _surfCharacter = GetComponent<SurfCharacter>();
        }

        private void OnEnable() {
            if (_inputCollector != null)
                _inputCollector.InputGathered += HandleInputGathered;

            if (_captureOnEnable)
                BeginCapture();
        }

        private void OnDisable() {
            if (_inputCollector != null)
                _inputCollector.InputGathered -= HandleInputGathered;
        }

        [ContextMenu("Begin Capture")]
        public void BeginCapture() {
            if (_targetAsset == null)
                return;

            ReplayTraceDefinition trace = _targetAsset.trace ?? new ReplayTraceDefinition();
            trace.traceId = string.IsNullOrWhiteSpace(_traceId) ? "live-capture" : _traceId;
            trace.frames.Clear();

            if (_surfCharacter != null) {
                trace.initialPosition = _surfCharacter.transform.position;
                if (_surfCharacter.moveData != null) {
                    trace.initialYaw = _surfCharacter.moveData.viewAngles.y;
                    trace.initialPitch = _surfCharacter.moveData.viewAngles.x;
                }
            }

            _targetAsset.trace = trace;
            _isCapturing = true;
            MarkAssetDirty();
        }

        [ContextMenu("Stop Capture")]
        public void StopCapture() {
            _isCapturing = false;
            MarkAssetDirty();
        }

        [ContextMenu("Clear Capture")]
        public void ClearCapture() {
            if (_targetAsset == null)
                return;

            ReplayTraceDefinition trace = _targetAsset.trace ?? new ReplayTraceDefinition();
            trace.frames.Clear();
            _targetAsset.trace = trace;
            MarkAssetDirty();
        }

        private void HandleInputGathered(InputFrame input) {
            if (!_isCapturing || _targetAsset == null)
                return;

            ReplayTraceDefinition trace = _targetAsset.trace ?? new ReplayTraceDefinition();
            trace.frames.Add(ReplayTraceFrame.FromInputFrame(input));
            _targetAsset.trace = trace;
            MarkAssetDirty();
        }

        private void MarkAssetDirty() {
#if UNITY_EDITOR
            if (_targetAsset != null)
                UnityEditor.EditorUtility.SetDirty(_targetAsset);
#endif
        }
    }
}

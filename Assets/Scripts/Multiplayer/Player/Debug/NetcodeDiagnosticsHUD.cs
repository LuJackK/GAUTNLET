using System.Text;
using UnityEngine;

namespace Fragsurf.Movement {

    [DisallowMultipleComponent]
    public class NetcodeDiagnosticsHUD : MonoBehaviour {

        [Header("References")]
        [SerializeField] private NetworkedCharacter _networkedCharacter;
        [SerializeField] private RollbackManager _rollbackManager;
        [SerializeField] private SurfCharacter _surfCharacter;

        [Header("Display")]
        [SerializeField] private bool _showHud = true;
        [SerializeField] private bool _ownerOnly = true;
        [SerializeField] private Vector2 _screenOffset = new Vector2(16f, 16f);
        [SerializeField] private int _fontSize = 14;
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.65f);

        private GUIStyle _labelStyle;
        private Texture2D _backgroundTexture;
        private readonly StringBuilder _builder = new StringBuilder(512);

        private void Awake() {
            if (_networkedCharacter == null)
                _networkedCharacter = GetComponent<NetworkedCharacter>();
            if (_rollbackManager == null)
                _rollbackManager = GetComponent<RollbackManager>();
            if (_surfCharacter == null)
                _surfCharacter = GetComponent<SurfCharacter>();
        }

        private void OnGUI() {
            if (!_showHud)
                return;

            if (_ownerOnly && _networkedCharacter != null && !_networkedCharacter.IsOwner)
                return;

            if (_networkedCharacter == null && _rollbackManager == null && _surfCharacter == null)
                return;

            EnsureGuiResources();

            _builder.Clear();
            _builder.AppendLine("Netcode Diagnostics");

            if (_networkedCharacter != null) {
                _builder.Append("Mode: ").AppendLine(_networkedCharacter.UseFishNetPredictionPipeline ? "FishNet Prediction" : "Unknown");
                _builder.Append("Current Tick: ").Append(_networkedCharacter.RollbackTick)
                    .Append("  Rollbacks: ").Append(_networkedCharacter.RollbackCount)
                    .Append("  Last Corrected: ").AppendLine(_networkedCharacter.LastCorrectedTick.ToString());
                _builder.Append("Correction Gate: ").Append(_networkedCharacter.LastCorrectionDecision)
                    .Append("  Score: ").Append(_networkedCharacter.LastCorrectionWeightedScore.ToString("F2"))
                    .Append("  Observe Streak: ").AppendLine(_networkedCharacter.CorrectionObserveStreak.ToString());
                _builder.Append("Replicates: ").Append(_networkedCharacter.ReplicateTicksProcessed)
                    .Append("  Last Replicate: ").Append(_networkedCharacter.LastReplicateFrame)
                    .Append("  Reconciles: ").AppendLine(_networkedCharacter.ReconcilePacketsReceived.ToString());
                _builder.Append("Spectator Refresh: ").Append(_networkedCharacter.SpectatorPresentationApplications)
                    .Append("  Last Refresh: ").AppendLine(_networkedCharacter.LastSpectatorPresentationFrame.ToString());
                _builder.Append("Corrections: Ignored ").Append(_networkedCharacter.IgnoredCorrectionCount)
                    .Append("  Observed ").Append(_networkedCharacter.ObserveOnlyCorrectionCount)
                    .Append("  Hard ").Append(_networkedCharacter.HardCorrectionCount)
                    .Append("  Force ").AppendLine(_networkedCharacter.ForceCorrectionCount.ToString());
                _builder.Append("Missing Predicted: ").AppendLine(_networkedCharacter.MissingPredictedStateCount.ToString());
                if (!string.IsNullOrWhiteSpace(_networkedCharacter.LastCorrectionPrimaryReason))
                    _builder.Append("Correction Reason: ").AppendLine(_networkedCharacter.LastCorrectionPrimaryReason);
            } else if (_rollbackManager != null) {
                _builder.Append("Current Tick: ").Append(_rollbackManager.CurrentTick)
                    .Append("  Rollbacks: ").Append(_rollbackManager.RollbackCount)
                    .Append("  Last Corrected: ").AppendLine(_rollbackManager.LastCorrectedTick.ToString());
            }

            if (_surfCharacter != null && _surfCharacter.moveData != null) {
                Vector3 velocity = _surfCharacter.moveData.velocity;
                _builder.Append("Pos: ").Append(FormatVector(_surfCharacter.moveData.origin))
                    .Append("  Vel: ").AppendLine(FormatVector(velocity));
                _builder.Append("MoveType: ").Append(_surfCharacter.moveData.moveType)
                    .Append("  Grounded: ").Append(_surfCharacter.moveData.grounded)
                    .Append("  Sliding: ").Append(_surfCharacter.moveData.sliding)
                    .Append("  Dashing: ").AppendLine(_surfCharacter.moveData.isDashing.ToString());
            }

            string debugSummary = NetcodeDebugEngine.HudSummary;
            if (!string.IsNullOrWhiteSpace(debugSummary))
                _builder.AppendLine(debugSummary);

            string text = _builder.ToString();
            Vector2 size = _labelStyle.CalcSize(new GUIContent(text));
            Rect rect = new Rect(_screenOffset.x, _screenOffset.y, size.x + 16f, size.y + 12f);
            GUI.DrawTexture(rect, _backgroundTexture);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), text, _labelStyle);
        }

        private void EnsureGuiResources() {
            if (_labelStyle == null) {
                _labelStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = Mathf.Max(10, _fontSize),
                    normal = { textColor = _textColor },
                    richText = false
                };
            } else {
                _labelStyle.fontSize = Mathf.Max(10, _fontSize);
                _labelStyle.normal.textColor = _textColor;
            }

            if (_backgroundTexture == null) {
                _backgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _backgroundTexture.SetPixel(0, 0, _backgroundColor);
                _backgroundTexture.Apply();
            } else {
                _backgroundTexture.SetPixel(0, 0, _backgroundColor);
                _backgroundTexture.Apply();
            }
        }

        private static string FormatVector(Vector3 value) {
            return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Fragsurf.Movement {

    public enum NetcodeDebugCategory {
        Lifecycle,
        Ownership,
        Spawn,
        Tick,
        Prediction,
        Snapshot,
        Reconcile,
        Rollback,
        Divergence,
        Input,
        Scenario,
        Simulation
    }

    public enum NetcodeDebugSeverity {
        Trace,
        Info,
        Warning,
        Error
    }

    [Flags]
    public enum NetcodeDebugSuspectFlags {
        None = 0,
        SpawnOwnershipRace = 1 << 0,
        ProxySelfSimulation = 1 << 1,
        ReplayImpurity = 1 << 2,
        BurstReconcileDebt = 1 << 3,
        InputTimelineDisorder = 1 << 4,
        SnapshotWriterConflict = 1 << 5,
        PureSimulationLeak = 1 << 6,
        CorrectionPolicyPressure = 1 << 7
    }

    public struct NetcodeDebugContext {
        public int ObjectId;
        public int OwnerId;
        public int LocalClientId;
        public int Tick;
        public int Frame;
        public bool IsServerInitialized;
        public bool IsClientInitialized;
        public bool IsOwner;
        public bool HasLocalAuthority;
        public string MoveType;
    }

    [DisallowMultipleComponent]
    public class NetcodeDebugEngine : MonoBehaviour {

        public enum ScenarioPreset {
            None,
            SpawnOwnershipIsolation,
            BaselineMovement,
            BurstStress,
            FullIsolationSweep
        }

        [Serializable]
        private struct ScenarioPhaseDefinition {
            public string Name;
            [TextArea(2, 5)] public string Instructions;
            public float DurationSeconds;

            public ScenarioPhaseDefinition(string name, string instructions, float durationSeconds) {
                Name = name;
                Instructions = instructions;
                DurationSeconds = durationSeconds;
            }
        }

        private struct DebugEventRecord {
            public int Sequence;
            public float TimeSeconds;
            public NetcodeDebugCategory Category;
            public NetcodeDebugSeverity Severity;
            public NetcodeDebugContext Context;
            public NetcodeDebugSuspectFlags Suspects;
            public string Message;
        }

        public static NetcodeDebugEngine Instance { get; private set; }

        [Header("Runtime")]
        [SerializeField] private bool _engineEnabled = true;
        [SerializeField] private bool _showOverlay = true;
        [SerializeField] private bool _enableHotkeys = true;
        [SerializeField] private bool _includeTraceLogsInConsole = false;
        [SerializeField] private int _maxEvents = 256;

        [Header("Hotkeys")]
        [SerializeField] private KeyCode _toggleOverlayKey = KeyCode.F5;
        [SerializeField] private KeyCode _cycleScenarioKey = KeyCode.F6;
        [SerializeField] private KeyCode _toggleScenarioKey = KeyCode.F7;
        [SerializeField] private KeyCode _advanceScenarioKey = KeyCode.F8;

        [Header("Scenarios")]
        [SerializeField] private ScenarioPreset _selectedScenarioPreset = ScenarioPreset.FullIsolationSweep;
        [SerializeField] private bool _autoAdvanceScenario = true;

        [Header("Overlay")]
        [SerializeField] private Vector2 _overlayOffset = new Vector2(16f, 16f);
        [SerializeField] private int _fontSize = 13;
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.72f);

        private readonly List<DebugEventRecord> _events = new List<DebugEventRecord>(256);
        private readonly Dictionary<string, float> _throttleTimes = new Dictionary<string, float>(64);
        private readonly List<ScenarioPhaseDefinition> _activeScenarioPhases = new List<ScenarioPhaseDefinition>(8);
        private readonly StringBuilder _overlayBuilder = new StringBuilder(1024);

        private GUIStyle _labelStyle;
        private Texture2D _backgroundTexture;

        private int _sequence;
        private int _warningCount;
        private int _errorCount;
        private NetcodeDebugSuspectFlags _activeSuspects;

        private bool _scenarioRunning;
        private int _scenarioPhaseIndex = -1;
        private float _scenarioPhaseEndsAt;
        private string _scenarioStatus = "Idle";

        public static bool IsAvailable => Instance != null && Instance._engineEnabled;

        public static string HudSummary {
            get {
                if (Instance == null || !Instance._engineEnabled)
                    return string.Empty;

                return Instance.BuildHudSummary();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate() {
            if (!(Application.isEditor || Debug.isDebugBuild))
                return;

            if (FindFirstObjectByType<NetcodeDebugEngine>() != null)
                return;

            GameObject go = new GameObject("NetcodeDebugEngine");
            DontDestroyOnLoad(go);
            go.AddComponent<NetcodeDebugEngine>();
        }

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LogInternal(NetcodeDebugCategory.Lifecycle,
                        NetcodeDebugSeverity.Info,
                        default,
                        "Netcode debug engine initialized.",
                        NetcodeDebugSuspectFlags.None,
                        null,
                        0f);
        }

        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
        }

        private void Update() {
            if (!_engineEnabled)
                return;

            if (_enableHotkeys) {
                if (Input.GetKeyDown(_toggleOverlayKey))
                    _showOverlay = !_showOverlay;

                if (Input.GetKeyDown(_cycleScenarioKey))
                    CycleScenarioPreset();

                if (Input.GetKeyDown(_toggleScenarioKey)) {
                    if (_scenarioRunning)
                        StopScenario("Stopped manually.");
                    else
                        StartSelectedScenario();
                }

                if (Input.GetKeyDown(_advanceScenarioKey))
                    AdvanceScenario("Advanced manually.");
            }

            if (_scenarioRunning && _autoAdvanceScenario && Time.realtimeSinceStartup >= _scenarioPhaseEndsAt)
                AdvanceScenario("Advanced automatically.");
        }

        private void OnGUI() {
            if (!_engineEnabled || !_showOverlay || Application.isBatchMode)
                return;

            EnsureGuiResources();

            string text = BuildOverlayText();
            Vector2 size = _labelStyle.CalcSize(new GUIContent(text));
            Rect rect = new Rect(_overlayOffset.x, _overlayOffset.y, Mathf.Min(Screen.width - 32f, size.x + 20f), size.y + 14f);
            GUI.DrawTexture(rect, _backgroundTexture);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, rect.height - 14f), text, _labelStyle);
        }

        public static void Log(NetcodeDebugCategory category,
                               NetcodeDebugSeverity severity,
                               NetcodeDebugContext context,
                               string message,
                               NetcodeDebugSuspectFlags suspects = NetcodeDebugSuspectFlags.None,
                               string throttleKey = null,
                               float throttleSeconds = 0f) {
            if (Instance == null || !Instance._engineEnabled)
                return;

            Instance.LogInternal(category, severity, context, message, suspects, throttleKey, throttleSeconds);
        }

        public static void StartScenario(ScenarioPreset preset) {
            if (Instance == null || !Instance._engineEnabled)
                return;

            Instance._selectedScenarioPreset = preset;
            Instance.StartSelectedScenario();
        }

        private void StartSelectedScenario() {
            _activeScenarioPhases.Clear();
            _activeScenarioPhases.AddRange(BuildPreset(_selectedScenarioPreset));

            if (_activeScenarioPhases.Count == 0) {
                _scenarioStatus = "No scenario phases configured.";
                LogInternal(NetcodeDebugCategory.Scenario,
                            NetcodeDebugSeverity.Warning,
                            default,
                            $"Scenario preset '{_selectedScenarioPreset}' has no phases.",
                            NetcodeDebugSuspectFlags.None,
                            null,
                            0f);
                return;
            }

            _scenarioRunning = true;
            _scenarioPhaseIndex = -1;
            _scenarioStatus = $"Scenario '{_selectedScenarioPreset}' started.";

            LogInternal(NetcodeDebugCategory.Scenario,
                        NetcodeDebugSeverity.Info,
                        default,
                        $"Starting scenario preset '{_selectedScenarioPreset}'.",
                        NetcodeDebugSuspectFlags.None,
                        null,
                        0f);

            AdvanceScenario("Entered first phase.");
        }

        private void StopScenario(string reason) {
            string presetName = _selectedScenarioPreset.ToString();
            _scenarioRunning = false;
            _scenarioPhaseIndex = -1;
            _scenarioPhaseEndsAt = 0f;
            _scenarioStatus = reason;

            LogInternal(NetcodeDebugCategory.Scenario,
                        NetcodeDebugSeverity.Info,
                        default,
                        $"Scenario '{presetName}' stopped. {reason}",
                        NetcodeDebugSuspectFlags.None,
                        null,
                        0f);
        }

        private void AdvanceScenario(string transitionReason) {
            if (!_scenarioRunning)
                return;

            _scenarioPhaseIndex++;
            if (_scenarioPhaseIndex >= _activeScenarioPhases.Count) {
                StopScenario("Completed all phases.");
                return;
            }

            ScenarioPhaseDefinition phase = _activeScenarioPhases[_scenarioPhaseIndex];
            _scenarioPhaseEndsAt = Time.realtimeSinceStartup + Mathf.Max(0.1f, phase.DurationSeconds);
            _scenarioStatus = transitionReason;

            LogInternal(NetcodeDebugCategory.Scenario,
                        NetcodeDebugSeverity.Info,
                        default,
                        $"Scenario phase {_scenarioPhaseIndex + 1}/{_activeScenarioPhases.Count}: {phase.Name}. {phase.Instructions}",
                        NetcodeDebugSuspectFlags.None,
                        $"scenario-phase-{_scenarioPhaseIndex}",
                        0f);
        }

        private void CycleScenarioPreset() {
            Array values = Enum.GetValues(typeof(ScenarioPreset));
            int current = Array.IndexOf(values, _selectedScenarioPreset);
            current = (current + 1) % values.Length;
            _selectedScenarioPreset = (ScenarioPreset)values.GetValue(current);

            LogInternal(NetcodeDebugCategory.Scenario,
                        NetcodeDebugSeverity.Info,
                        default,
                        $"Selected scenario preset '{_selectedScenarioPreset}'.",
                        NetcodeDebugSuspectFlags.None,
                        null,
                        0f);
        }

        private void LogInternal(NetcodeDebugCategory category,
                                 NetcodeDebugSeverity severity,
                                 NetcodeDebugContext context,
                                 string message,
                                 NetcodeDebugSuspectFlags suspects,
                                 string throttleKey,
                                 float throttleSeconds) {
            if (!PassThrottle(throttleKey, throttleSeconds))
                return;

            _sequence++;

            DebugEventRecord record = new DebugEventRecord {
                Sequence = _sequence,
                TimeSeconds = Time.realtimeSinceStartup,
                Category = category,
                Severity = severity,
                Context = context,
                Suspects = suspects,
                Message = message ?? string.Empty
            };

            _events.Add(record);
            if (_events.Count > Mathf.Max(32, _maxEvents))
                _events.RemoveAt(0);

            if (severity == NetcodeDebugSeverity.Warning)
                _warningCount++;
            else if (severity == NetcodeDebugSeverity.Error)
                _errorCount++;

            _activeSuspects |= suspects;

            if (severity != NetcodeDebugSeverity.Trace || _includeTraceLogsInConsole)
                WriteToConsole(record);
        }

        private bool PassThrottle(string throttleKey, float throttleSeconds) {
            if (string.IsNullOrWhiteSpace(throttleKey) || throttleSeconds <= 0f)
                return true;

            float now = Time.realtimeSinceStartup;
            if (_throttleTimes.TryGetValue(throttleKey, out float lastTime) && (now - lastTime) < throttleSeconds)
                return false;

            _throttleTimes[throttleKey] = now;
            return true;
        }

        private void WriteToConsole(DebugEventRecord record) {
            string formatted = FormatRecord(record);
            if (record.Severity == NetcodeDebugSeverity.Error)
                Debug.LogError(formatted, this);
            else if (record.Severity == NetcodeDebugSeverity.Warning)
                Debug.LogWarning(formatted, this);
            else
                Debug.Log(formatted, this);
        }

        private string FormatRecord(DebugEventRecord record) {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("[NetDebug][");
            sb.Append(record.Category);
            sb.Append("][");
            sb.Append(record.Severity);
            sb.Append("][#");
            sb.Append(record.Sequence);
            sb.Append("] obj=");
            sb.Append(record.Context.ObjectId);
            sb.Append(" owner=");
            sb.Append(record.Context.OwnerId);
            sb.Append(" local=");
            sb.Append(record.Context.LocalClientId);
            sb.Append(" tick=");
            sb.Append(record.Context.Tick);
            sb.Append(" frame=");
            sb.Append(record.Context.Frame);
            sb.Append(" server=");
            sb.Append(record.Context.IsServerInitialized);
            sb.Append(" client=");
            sb.Append(record.Context.IsClientInitialized);
            sb.Append(" ownerLocal=");
            sb.Append(record.Context.HasLocalAuthority);

            if (!string.IsNullOrWhiteSpace(record.Context.MoveType)) {
                sb.Append(" move=");
                sb.Append(record.Context.MoveType);
            }

            if (record.Suspects != NetcodeDebugSuspectFlags.None) {
                sb.Append(" suspects=");
                sb.Append(record.Suspects);
            }

            sb.Append(" :: ");
            sb.Append(record.Message);
            return sb.ToString();
        }

        private void EnsureGuiResources() {
            if (_labelStyle == null) {
                _labelStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = Mathf.Max(10, _fontSize),
                    richText = false,
                    wordWrap = true
                };
                _labelStyle.normal.textColor = _textColor;
            } else {
                _labelStyle.fontSize = Mathf.Max(10, _fontSize);
                _labelStyle.normal.textColor = _textColor;
            }

            if (_backgroundTexture == null) {
                _backgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            }

            _backgroundTexture.SetPixel(0, 0, _backgroundColor);
            _backgroundTexture.Apply();
        }

        private string BuildOverlayText() {
            _overlayBuilder.Clear();
            _overlayBuilder.AppendLine("Netcode Debug Engine");
            _overlayBuilder.Append("Scenario Preset: ").AppendLine(_selectedScenarioPreset.ToString());
            _overlayBuilder.Append("Scenario State: ").AppendLine(_scenarioRunning ? "Running" : "Idle");

            if (_scenarioRunning && _scenarioPhaseIndex >= 0 && _scenarioPhaseIndex < _activeScenarioPhases.Count) {
                ScenarioPhaseDefinition phase = _activeScenarioPhases[_scenarioPhaseIndex];
                float remaining = Mathf.Max(0f, _scenarioPhaseEndsAt - Time.realtimeSinceStartup);
                _overlayBuilder.Append("Phase: ").Append(_scenarioPhaseIndex + 1).Append('/').Append(_activeScenarioPhases.Count)
                    .Append(" - ").Append(phase.Name).Append(" (").Append(remaining.ToString("F1")).AppendLine("s)");
                _overlayBuilder.AppendLine(phase.Instructions);
            } else {
                _overlayBuilder.Append("Scenario Note: ").AppendLine(_scenarioStatus);
            }

            _overlayBuilder.Append("Warnings: ").Append(_warningCount)
                .Append("  Errors: ").Append(_errorCount)
                .Append("  Suspects: ").AppendLine(_activeSuspects == NetcodeDebugSuspectFlags.None ? "None" : _activeSuspects.ToString());

            if (_events.Count > 0) {
                DebugEventRecord last = _events[_events.Count - 1];
                _overlayBuilder.Append("Last Event: ").AppendLine($"{last.Category}/{last.Severity}");
                _overlayBuilder.AppendLine(last.Message);
            } else {
                _overlayBuilder.AppendLine("Last Event: none");
            }

            _overlayBuilder.AppendLine("Hotkeys: F5 overlay, F6 cycle scenario, F7 start/stop, F8 next phase");
            return _overlayBuilder.ToString().TrimEnd();
        }

        private string BuildHudSummary() {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("Debug Scenario: ").Append(_selectedScenarioPreset);
            if (_scenarioRunning && _scenarioPhaseIndex >= 0 && _scenarioPhaseIndex < _activeScenarioPhases.Count) {
                ScenarioPhaseDefinition phase = _activeScenarioPhases[_scenarioPhaseIndex];
                sb.Append("  Phase: ").Append(phase.Name);
            } else {
                sb.Append("  Phase: Idle");
            }

            sb.AppendLine();
            sb.Append("Debug Suspects: ").Append(_activeSuspects == NetcodeDebugSuspectFlags.None ? "None" : _activeSuspects.ToString());
            sb.Append("  Warn: ").Append(_warningCount);
            sb.Append("  Err: ").Append(_errorCount);

            if (_events.Count > 0) {
                DebugEventRecord last = _events[_events.Count - 1];
                sb.AppendLine();
                sb.Append("Debug Last: ").Append(last.Category).Append(" - ").Append(last.Message);
            }

            return sb.ToString();
        }

        private static List<ScenarioPhaseDefinition> BuildPreset(ScenarioPreset preset) {
            List<ScenarioPhaseDefinition> phases = new List<ScenarioPhaseDefinition>(6);

            switch (preset) {
                case ScenarioPreset.SpawnOwnershipIsolation:
                    phases.Add(new ScenarioPhaseDefinition(
                        "Join Idle",
                        "Spawn host and client, then do not move. Watch for ownership flips, early corrections, or checksum mismatches during the first 5 seconds.",
                        5f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Stand Still",
                        "Continue standing still. If counters move here, the bug is likely spawn, ownership, or replay impurity rather than action logic.",
                        8f));
                    break;

                case ScenarioPreset.BaselineMovement:
                    phases.Add(new ScenarioPhaseDefinition(
                        "Move Only",
                        "Move with no jump, dash, crouch, or melee. Establish whether plain locomotion already diverges.",
                        10f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Move And Jump",
                        "Add jumping only. If divergence starts here, focus on edge-action handling before burst movement systems.",
                        10f));
                    break;

                case ScenarioPreset.BurstStress:
                    phases.Add(new ScenarioPhaseDefinition(
                        "Dash Slide Melee",
                        "Spam dash, crouch/slide, and melee chains. This phase is meant to pressure burst reconcile debt and scene-query nondeterminism.",
                        15f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Burst Recovery",
                        "Stop bursting and walk normally again. Watch whether corrections arrive late after burst defer is exhausted.",
                        8f));
                    break;

                case ScenarioPreset.FullIsolationSweep:
                    phases.Add(new ScenarioPhaseDefinition(
                        "Spawn Idle",
                        "Spawn both peers and do not move. Confirm whether the first bad frame appears before active movement begins.",
                        6f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Move Only",
                        "Move only. No jump, dash, crouch, or melee.",
                        10f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Move And Jump",
                        "Move and jump only. Keep other actions disabled mentally for this phase.",
                        10f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Burst Chain",
                        "Run dash, slide, and melee chains. Compare this phase against the earlier baseline phases.",
                        15f));
                    phases.Add(new ScenarioPhaseDefinition(
                        "Late Join / Rejoin",
                        "If possible, connect or reconnect a client during this phase. Watch spawn ownership and immediate snapshot convergence.",
                        15f));
                    break;
            }

            return phases;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fragsurf.Movement {
    [Serializable] public sealed class CaptureCommand {
        public string type, code, session, process, compatibility, note, id;
        public uint tick;
    }

    // One explicit peer; fixed command vocabulary; no remote paths or execution.
    // Pairing is for a trusted LAN. The code is sent in plaintext over TCP.
    public sealed class CaptureControlChannel : IDisposable {
        public readonly ConcurrentQueue<CaptureCommand> Incoming = new ConcurrentQueue<CaptureCommand>();
        private readonly ConcurrentQueue<CaptureCommand> outgoing = new ConcurrentQueue<CaptureCommand>();
        private volatile bool stopped;
        private volatile string error;
        private TcpClient client;
        private TcpListener listener;
        private volatile IPAddress remoteAddress;
        public string Error => error;
        public IPAddress RemoteAddress => remoteAddress;
        public CaptureControlChannel(bool host, string address, int port) {
            new Thread(() => Run(host, address, port)) { IsBackground = true, Name = "Capture coordination" }.Start();
        }
        public void Send(CaptureCommand command) {
            if (outgoing.Count >= 64) { error = "Control send queue overflow"; return; }
            outgoing.Enqueue(command);
        }
        private void Run(bool host, string address, int port) {
            try {
                if (host) {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start(1);
                    client = listener.AcceptTcpClient();
                    listener.Stop();
                } else { client = new TcpClient(); client.Connect(IPAddress.Parse(address), port); }
                remoteAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                client.NoDelay = true;
                client.ReceiveTimeout = 3000;
                client.SendTimeout = 3000;
                using (NetworkStream stream = client.GetStream())
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                    while (!stopped) {
                        while (outgoing.TryDequeue(out CaptureCommand command)) {
                            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(command));
                            if (bytes.Length > 16384) throw new IOException("Oversized control message");
                            writer.Write(bytes.Length); writer.Write(bytes); writer.Flush();
                        }
                        if (!client.Client.Poll(10000, SelectMode.SelectRead)) continue;
                        int size = reader.ReadInt32();
                        if (size < 2 || size > 16384) throw new IOException("Invalid control length");
                        byte[] data = reader.ReadBytes(size);
                        if (data.Length != size) throw new EndOfStreamException();
                        if (Incoming.Count >= 64) throw new IOException("Control receive queue overflow");
                        Incoming.Enqueue(JsonUtility.FromJson<CaptureCommand>(Encoding.UTF8.GetString(data)));
                    }
                }
            } catch (Exception ex) { if (!stopped) error = ex.GetType().Name + ": " + ex.Message; }
            finally { client?.Close(); listener?.Stop(); }
        }
        public void Dispose() { stopped = true; client?.Close(); listener?.Stop(); }
    }

    // Moves the joinee's finished bundle to the diagnostics host after a paired capture,
    // so both bundles end up on one PC. Only the two fixed bundle files cross; the
    // receiver picks the destination, checks peer address, code, session and process,
    // and caps sizes. Like the control channel this is for a trusted LAN: plaintext TCP.
    // No UnityEngine references, so it can be tested outside the player.
    public sealed class BundleTransfer : IDisposable {
        public const int DefaultPort = 28742;
        private const string Magic = "GAUNTLET-BUNDLE-1";
        private static readonly string[] Files = { "manifest.json", "events.jsonl" };
        private static readonly long[] MaxBytes = { 1L << 20, 16L << 30 };
        private volatile string status;
        private volatile bool done, stopped;
        private TcpListener listener;
        private TcpClient client;
        public string Status => status;
        public bool Done => done;

        private BundleTransfer(string initial) { status = initial; }

        public static BundleTransfer Receive(string code, string session, string peerProcess, IPAddress peerAddress,
                                             string destination, int port = DefaultPort, int timeoutSeconds = 600) {
            var transfer = new BundleTransfer("Waiting for peer bundle on TCP " + port);
            new Thread(() => transfer.RunReceive(code, session, peerProcess, peerAddress, destination, port, timeoutSeconds)) {
                IsBackground = true, Name = "Bundle receive" }.Start();
            return transfer;
        }

        public static BundleTransfer Send(string address, string code, string session, string process,
                                          string source, int port = DefaultPort, int connectSeconds = 60) {
            var transfer = new BundleTransfer("Sending bundle to diagnostics host");
            new Thread(() => transfer.RunSend(address, code, session, process, source, port, connectSeconds)) {
                IsBackground = true, Name = "Bundle send" }.Start();
            return transfer;
        }

        private void RunReceive(string code, string session, string peerProcess, IPAddress peerAddress,
                                string destination, int port, int timeoutSeconds) {
            var parts = new List<string>();
            try {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start(1);
                DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (!listener.Pending()) {
                    if (stopped) return;
                    if (DateTime.UtcNow > deadline) { status = "Peer bundle not received (timed out)"; return; }
                    Thread.Sleep(100);
                }
                client = listener.AcceptTcpClient();
                listener.Stop();
                client.ReceiveTimeout = 60000; client.SendTimeout = 60000;
                IPAddress remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                if (peerAddress != null && !Same(remote, peerAddress))
                    throw new IOException("Connection from " + remote + ", expected the paired peer " + peerAddress);
                using (NetworkStream stream = client.GetStream()) {
                    var header = new BinaryReader(stream, Encoding.UTF8, true);
                    if (ReadShort(header) != Magic || ReadShort(header) != code ||
                        ReadShort(header) != session || ReadShort(header) != peerProcess)
                        throw new IOException("Bundle header does not match this capture session");
                    Directory.CreateDirectory(destination);
                    long total = 0;
                    using (var gzip = new GZipStream(stream, CompressionMode.Decompress, true))
                    using (var reader = new BinaryReader(gzip, Encoding.UTF8, true)) {
                        for (int i = 0; i < Files.Length; i++) {
                            if (ReadShort(reader) != Files[i]) throw new IOException("Unexpected bundle file");
                            long length = reader.ReadInt64();
                            if (length < 0 || length > MaxBytes[i]) throw new IOException(Files[i] + " exceeds size limit");
                            string part = Path.Combine(destination, Files[i] + ".part");
                            parts.Add(part);
                            using (var file = new FileStream(part, FileMode.Create, FileAccess.Write))
                                Copy(gzip, file, length, ref total, "Receiving peer bundle");
                        }
                    }
                    for (int i = 0; i < Files.Length; i++) {
                        string final = Path.Combine(destination, Files[i]);
                        if (File.Exists(final)) File.Delete(final);
                        File.Move(parts[i], final);
                    }
                    parts.Clear();
                    stream.WriteByte(1); stream.Flush();
                    status = "Received peer bundle (" + Mb(total) + " MB): " + destination;
                    done = true;
                }
            } catch (Exception ex) {
                if (!stopped) status = "Peer bundle FAILED: " + ex.GetType().Name + ": " + ex.Message;
                foreach (string part in parts) try { File.Delete(part); } catch (Exception) { }
            } finally { client?.Close(); listener?.Stop(); }
        }

        private void RunSend(string address, string code, string session, string process,
                             string source, int port, int connectSeconds) {
            try {
                var paths = new string[Files.Length];
                for (int i = 0; i < Files.Length; i++) {
                    paths[i] = Path.Combine(source, Files[i]);
                    if (!File.Exists(paths[i])) throw new FileNotFoundException("Local bundle file missing", Files[i]);
                }
                DateTime deadline = DateTime.UtcNow.AddSeconds(connectSeconds);
                while (true) {
                    if (stopped) return;
                    try { client = new TcpClient(); client.Connect(IPAddress.Parse(address), port); break; }
                    catch (SocketException) {
                        client.Close();
                        if (DateTime.UtcNow > deadline) throw;
                        status = "Waiting for diagnostics host to accept bundle";
                        Thread.Sleep(500);
                    }
                }
                client.ReceiveTimeout = 120000; client.SendTimeout = 60000;
                long total = 0;
                using (NetworkStream stream = client.GetStream()) {
                    var header = new BinaryWriter(stream, Encoding.UTF8, true);
                    WriteShort(header, Magic); WriteShort(header, code); WriteShort(header, session); WriteShort(header, process);
                    header.Flush();
                    using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest, true))
                    using (var writer = new BinaryWriter(gzip, Encoding.UTF8, true)) {
                        for (int i = 0; i < Files.Length; i++) {
                            using (var file = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                                long length = file.Length;
                                WriteShort(writer, Files[i]); writer.Write(length); writer.Flush();
                                Copy(file, gzip, length, ref total, "Sending bundle");
                            }
                        }
                    }
                    status = "Sent " + Mb(total) + " MB; waiting for host to confirm";
                    if (stream.ReadByte() != 1) throw new IOException("Diagnostics host did not confirm the bundle");
                    status = "Bundle delivered to diagnostics host (" + Mb(total) + " MB)";
                    done = true;
                }
            } catch (Exception ex) {
                if (!stopped) status = "Bundle send FAILED: " + ex.GetType().Name + ": " + ex.Message;
            } finally { client?.Close(); }
        }

        private void Copy(Stream from, Stream to, long length, ref long total, string label) {
            var buffer = new byte[1 << 16];
            long remaining = length, reported = 0;
            while (remaining > 0) {
                if (stopped) throw new OperationCanceledException("Transfer cancelled");
                int read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0) throw new EndOfStreamException("Bundle stream ended early");
                to.Write(buffer, 0, read);
                remaining -= read; total += read;
                if (total - reported >= 8 << 20) { reported = total; status = label + ": " + Mb(total) + " MB"; }
            }
        }
        private static bool Same(IPAddress a, IPAddress b) =>
            (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).Equals(b.IsIPv4MappedToIPv6 ? b.MapToIPv4() : b);
        private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        private static void WriteShort(BinaryWriter writer, string text) {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            if (bytes.Length > 256) throw new IOException("Header field too long");
            writer.Write((short)bytes.Length); writer.Write(bytes);
        }
        private static string ReadShort(BinaryReader reader) {
            int length = reader.ReadInt16();
            if (length < 0 || length > 256) throw new IOException("Invalid header field");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
        public void Dispose() { stopped = true; client?.Close(); listener?.Stop(); }
    }

    public sealed class SessionCaptureCoordinator : MonoBehaviour {
        private string address = "127.0.0.1", code = "", note = "", status = "Join gameplay first, then pair diagnostics.";
        private string session, peer, process, compatibility;
        private bool host, paired, visible = true, readySent, peerReady, committed, acknowledged, sendStarted;
        private BundleTransfer transfer;
        private uint startTick;
        private float deadline, lastHeartbeat, lastPeer;
        private CaptureControlChannel channel;
        private TraceManifest manifest;
        private enum Stage { Idle, Pairing, Paired, Preparing, Armed, Recording, Exporting, Failed }
        private Stage stage;
        public KeyCode MarkerKey = KeyCode.F10;
        public KeyCode PanelKey = KeyCode.F9;
        private const int Port = 28741;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (FindFirstObjectByType<SessionCaptureCoordinator>() == null) {
                var go = new GameObject("SessionCaptureCoordinator");
                DontDestroyOnLoad(go); go.AddComponent<SessionCaptureCoordinator>();
            }
#endif
        }
        private void Awake() {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            Destroy(gameObject);
            return;
#else
            process = Guid.NewGuid().ToString("N");
            visible = false;
#endif
        }
        private uint Tick => InstanceFinder.TimeManager != null ? InstanceFinder.TimeManager.Tick : 0;
        private bool GameplayReady => InstanceFinder.NetworkManager != null &&
            (InstanceFinder.IsServerStarted || InstanceFinder.IsClientStarted);

        private TraceManifest MakeManifest() {
            TraceBuildStamp stamp = TraceBuildStamp.Load();
            var characters = FindObjectsByType<SurfCharacter>(FindObjectsSortMode.None);
            var configurations = new System.Collections.Generic.SortedSet<string>();
            foreach (var character in characters) configurations.Add(JsonUtility.ToJson(character.moveConfig));
            var roster = new System.Collections.Generic.List<string>();
            foreach (var character in FindObjectsByType<NetworkedCharacter>(FindObjectsSortMode.None))
                roster.Add(character.ObjectId + ":owner=" + character.OwnerId + ":epoch=" + character.MovementEpoch);
            roster.Sort(StringComparer.Ordinal);
            return new TraceManifest {
                session = session, process = process, peer = peer,
                build = Application.buildGUID, unity = Application.unityVersion,
                platform = SystemInfo.operatingSystem, cpu = SystemInfo.processorType,
                scene = SceneManager.GetActiveScene().path,
                revision = stamp.revision, sourceDirty = stamp.dirty, artifactHash = stamp.artifactHash, contentHash = stamp.contentHash,
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                topology = InstanceFinder.IsServerStarted ? (InstanceFinder.IsClientStarted ? "host client + server" : "server only") : "remote client",
                roster = string.Join("\n", roster),
                tickRate = InstanceFinder.TimeManager != null ? InstanceFinder.TimeManager.TickRate : 0,
                fixedDelta = Time.fixedDeltaTime,
                configuration = string.Join("\n", configurations),
                physics = JsonUtility.ToJson(new PhysicsSettings {
                    gravity = Physics.gravity, solver = Physics.defaultSolverIterations,
                    velocitySolver = Physics.defaultSolverVelocityIterations,
                    contactOffset = Physics.defaultContactOffset, autoSync = Physics.autoSyncTransforms,
                    queriesHitTriggers = Physics.queriesHitTriggers
                })
            };
        }
        [Serializable] private struct PhysicsSettings {
            public Vector3 gravity;
            public int solver, velocitySolver;
            public float contactOffset;
            public bool autoSync, queriesHitTriggers;
        }
        private static readonly string[] CompatibilityNames = {
            "build", "unity", "scene", "tickRate", "fixedDelta", "movementConfig", "physics", "artifactHash", "contentHash", "backend" };
        private static string[] CompatibilityValues(TraceManifest value) {
            return new[] { value.build, value.unity, value.scene, value.tickRate.ToString(),
                value.fixedDelta.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.configuration,
                value.physics, value.artifactHash, value.contentHash, value.backend };
        }
        private static string Hash(string text) {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text ?? "")));
        }
        private static string Compatibility(TraceManifest value) => Hash(string.Join("|", CompatibilityValues(value)));
        // One short hash per compared value, sent with "hello" so a mismatch can name the values that differ.
        private static string CompatibilityDigest(TraceManifest value) {
            string[] values = CompatibilityValues(value);
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = CompatibilityNames[i] + "=" + Hash(values[i]).Substring(0, 12);
            return string.Join(";", parts);
        }
        private static string CompatibilityDifferences(string local, string remote) {
            if (string.IsNullOrEmpty(remote)) return "peer sent no detail (older build?)";
            var differing = new System.Collections.Generic.List<string>();
            var theirs = new System.Collections.Generic.HashSet<string>(remote.Split(';'));
            foreach (string part in local.Split(';'))
                if (!theirs.Contains(part)) differing.Add(part.Substring(0, part.IndexOf('=')));
            return differing.Count > 0 ? string.Join(", ", differing) : "unknown";
        }
        private static void LogCompatibility(TraceManifest value) {
            string[] values = CompatibilityValues(value);
            var text = new StringBuilder("[Capture] Pairing compatibility inputs:\n");
            for (int i = 0; i < values.Length; i++) text.Append(CompatibilityNames[i]).Append(": ").Append(values[i]).Append('\n');
            Debug.Log(text.ToString());
        }
        private void Pair(bool asHost) {
            if (!GameplayReady) { status = "Start/join gameplay before pairing."; return; }
            if (code.Length < 6 || code.Length > 32) { status = "Enter a pairing code of 6–32 characters on both PCs."; return; }
            channel?.Dispose();
            host = asHost; paired = false; peer = null;
            session = asHost ? Guid.NewGuid().ToString("N") : null;
            manifest = MakeManifest();
            compatibility = Compatibility(manifest);
            LogCompatibility(manifest);
            channel = new CaptureControlChannel(host, address, Port);
            stage = Stage.Pairing; deadline = Time.realtimeSinceStartup + 60;
            Send("hello", message: CompatibilityDigest(manifest)); status = "Pairing on TCP " + Port;
        }
        private void Send(string type, uint tick = 0, string id = null, string message = null) {
            channel?.Send(new CaptureCommand { type = type, code = code, session = session,
                process = process, compatibility = type == "hello" ? compatibility : null, tick = tick, id = id, note = message });
        }
        private void Prepare() {
            if (!paired || stage != Stage.Paired) return;
            manifest = MakeManifest();
            if (Compatibility(manifest) != compatibility) { Fail("Configuration changed after pairing; pair again"); return; }
            string path = Path.Combine(Application.persistentDataPath, "NetcodeCaptures", session, process);
            NetcodeTraceRecorder.Writer = new TraceBundleWriter(path, manifest);
            readySent = peerReady = committed = acknowledged = sendStarted = false;
            stage = Stage.Preparing; deadline = Time.realtimeSinceStartup + 15;
            status = "Preparing both local bundle writers";
        }
        private void Fail(string reason) {
            status = "FAILED: " + reason;
            NetcodeTraceRecorder.Recording = false;
            NetcodeTraceRecorder.Writer?.Stop(reason);
            stage = Stage.Failed; paired = false;
            channel?.Dispose(); channel = null;
        }
        private void StopCapture() {
            NetcodeTraceRecorder.Recording = false;
            NetcodeTraceRecorder.Writer?.Stop();
            stage = Stage.Exporting; status = "Exporting local bundle";
            // The diagnostics host collects the joinee's bundle next to its own.
            if (host && peer != null && channel != null) {
                transfer?.Dispose();
                transfer = BundleTransfer.Receive(code, session, peer, channel.RemoteAddress,
                    Path.Combine(Application.persistentDataPath, "NetcodeCaptures", session, peer));
            }
        }
        private void Handle(CaptureCommand command) {
            if (command == null || command.code != code || command.process == process) { Fail("Pairing code or process mismatch"); return; }
            if (command.type == "hello" && stage == Stage.Pairing) {
                if (command.compatibility != compatibility) {
                    string differing = CompatibilityDifferences(CompatibilityDigest(manifest), command.note);
                    Debug.LogWarning("[Capture] Peer differs in: " + differing);
                    Fail("Differs from peer: " + differing + " (full values in Player.log)"); return;
                }
                if (!host) {
                    if (!Guid.TryParseExact(command.session, "N", out _)) { Fail("Invalid session ID"); return; }
                    session = command.session;
                }
                if (!Guid.TryParseExact(command.process, "N", out _)) { Fail("Invalid peer process ID"); return; }
                peer = command.process;
                Send("paired"); return;
            }
            if (command.session != session || command.process != peer) { Fail("Unexpected diagnostic peer/session"); return; }
            lastPeer = Time.realtimeSinceStartup;
            switch (command.type) {
                case "paired":
                    if (stage != Stage.Pairing) return;
                    paired = true; stage = Stage.Paired; status = "Paired; A can arm both recorders"; break;
                case "prepare": if (!host && stage == Stage.Paired) Prepare(); break;
                case "ready": if (stage == Stage.Preparing) peerReady = true; break;
                case "arm":
                    if (host || stage != Stage.Preparing || !readySent) { Fail("Unexpected arm"); break; }
                    if (unchecked((int)(command.tick - Tick)) < manifest.tickRate) { Fail("Start tick arrived too late"); break; }
                    startTick = command.tick; stage = Stage.Armed; Send("armed", startTick); break;
                case "armed":
                    if (host && stage == Stage.Armed && command.tick == startTick) { committed = true; Send("commit", startTick); } break;
                case "commit":
                    if (!host && stage == Stage.Armed && command.tick == startTick) { committed = true; acknowledged = true; Send("committed", startTick); } break;
                case "committed": if (host && stage == Stage.Armed && command.tick == startTick) acknowledged = true; break;
                case "marker":
                    if (stage == Stage.Recording) NetcodeTraceRecorder.Marker(command.id, command.note, unchecked((int)command.tick), Tick, true); break;
                case "stop": if (stage == Stage.Recording || stage == Stage.Armed || stage == Stage.Preparing) StopCapture(); break;
                case "heartbeat": break;
                default: Fail("Unknown control command"); break;
            }
        }
        private void Update() {
            if (Input.GetKeyDown(PanelKey)) visible = !visible;
            if (stage == Stage.Recording && Input.GetKeyDown(MarkerKey)) Mark();
            if (!host && stage == Stage.Exporting && !sendStarted && NetcodeTraceRecorder.Writer != null &&
                NetcodeTraceRecorder.Writer.Finished) {
                sendStarted = true;
                transfer?.Dispose();
                transfer = BundleTransfer.Send(address, code, session, process, NetcodeTraceRecorder.Writer.DirectoryPath);
            }
            if (channel == null) return;
            if (paired && !GameplayReady) { Fail("Gameplay disconnected; partial bundle retained"); return; }
            if (paired && SceneManager.GetActiveScene().path != manifest.scene) { Fail("Scene changed; pair a new capture"); return; }
            if (channel.Error != null) { Fail(channel.Error); return; }
            int budget = 16;
            while (budget-- > 0 && channel != null && channel.Incoming.TryDequeue(out CaptureCommand command)) Handle(command);
            if (channel == null) return;
            float now = Time.realtimeSinceStartup;
            if (paired && now - lastHeartbeat >= 0.5f) { Send("heartbeat", Tick); lastHeartbeat = now; }
            if (paired && now - lastPeer > 3) { Fail("Peer heartbeat timeout; partial bundle retained"); return; }
            if ((stage == Stage.Pairing || stage == Stage.Preparing || stage == Stage.Armed) && now > deadline) { Fail("Pair/start handshake timeout"); return; }
            if ((stage == Stage.Preparing || stage == Stage.Armed || stage == Stage.Recording) && NetcodeTraceRecorder.Writer.Error != null) { Fail(NetcodeTraceRecorder.Writer.Error); return; }
            if (stage == Stage.Preparing) {
                if (!readySent && NetcodeTraceRecorder.Writer.Ready) { readySent = true; Send("ready"); }
                if (host && readySent && peerReady) {
                    startTick = Tick + (uint)(manifest.tickRate * 5); stage = Stage.Armed; Send("arm", startTick);
                }
            }
            if (stage == Stage.Armed && unchecked((int)(Tick - startTick)) >= 0) {
                if (!committed || !acknowledged) { Fail("Both peers did not acknowledge start"); return; }
                NetcodeTraceRecorder.Recording = true; stage = Stage.Recording;
                NetcodeTraceRecorder.Marker("capture-start", "Agreed estimated server tick; causal mapping remains per snapshot", unchecked((int)startTick), Tick, false);
                status = "Recording paired session " + session;
            }
        }
        private void Mark() {
            string id = Guid.NewGuid().ToString("N");
            NetcodeTraceRecorder.Marker(id, note, unchecked((int)Tick), Tick, false);
            Send("marker", Tick, id, note);
        }
        private void OnGUI() {
            if (!visible || Application.isBatchMode) return;
            GUILayout.BeginArea(new Rect(20, 160, 490, 440), GUI.skin.box);
            GUILayout.Label("LAN capture — F9 panel / F10 marker");
            GUILayout.Label(status); GUILayout.Label(NetcodeTraceRecorder.Status);
            if (stage == Stage.Idle || stage == Stage.Failed || stage == Stage.Exporting) {
                if (NetcodeTraceRecorder.Writer == null || NetcodeTraceRecorder.Writer.Finished) {
                    GUILayout.Label("Peer IPv4 (joining B)"); address = GUILayout.TextField(address, 64);
                    GUILayout.Label("Shared pairing code"); code = GUILayout.TextField(code, 32);
                    if (GUILayout.Button("A: host diagnostics")) Pair(true);
                    if (GUILayout.Button("B: join diagnostics")) Pair(false);
                }
            }
            if (stage == Stage.Paired && host && GUILayout.Button("Arm both recorders")) { Send("prepare"); Prepare(); }
            if (stage == Stage.Recording) {
                note = GUILayout.TextField(note, 256);
                if (GUILayout.Button("Mark moment")) Mark();
                if (GUILayout.Button("Stop and export both")) { Send("stop"); StopCapture(); }
            }
            if (NetcodeTraceRecorder.Writer != null) GUILayout.Label(NetcodeTraceRecorder.Writer.DirectoryPath);
            if (transfer != null) {
                GUILayout.Label(transfer.Status);
                if (!transfer.Done) GUILayout.Label("Keep both games open until the bundle transfer finishes.");
            }
            GUILayout.EndArea();
        }
        private void OnDestroy() {
            channel?.Dispose(); transfer?.Dispose();
            NetcodeTraceRecorder.Recording = false; NetcodeTraceRecorder.Writer?.Stop();
        }
        private void OnApplicationQuit() {
            NetcodeTraceRecorder.Recording = false; NetcodeTraceRecorder.Writer?.Stop();
            NetcodeTraceRecorder.Writer?.Wait(1000);
        }
    }
}

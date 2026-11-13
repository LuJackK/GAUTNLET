using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Fragsurf.Movement {
    [Serializable]
    public sealed class TraceManifest {
        public int schema = 1;
        public string session, process, peer, build, unity, platform, cpu, scene, revision;
        public string artifactHash, contentHash, backend, topology, roster, physics;
        public bool sourceDirty;
        public string scope = "live movement observation; no complete world checkpoint; not replayable";
        public string exclusions = "transport loss, collision queries, combat decisions, RNG, pickups, camera pose, full world state";
        public string mode = "continuous bounded recording";
        public string tickMapping = "FishNet Tick is estimated server time; reconcile sourceTick and mapped tick provide causal mapping";
        public long frequency = Stopwatch.Frequency;
        public int tickRate;
        public float fixedDelta;
        public string configuration;
    }

    [Serializable]
    public sealed class TraceEvent {
        public string type, role, reason, marker;
        public long sequence, monotonic, overhead;
        public int renderFrame, entity = -1, owner = -1, tick = -1, sourceTick = -1, inputTick = -1, pass;
        public uint epoch, payloadEpoch, serverTick, localTick;
        public float delta;
        public bool hasInput, hasState;
        public InputFrame input;
        public TraceMovementState state;
    }

    [Serializable]
    public sealed class TraceFooter {
        public string type = "footer", error;
        public long accepted, dropped, written, overhead;
        public int peakQueue;
        public bool complete;
    }

    // Only detached DTOs cross this boundary. File access and JSON encoding run
    // on this worker, never on FishNet's simulation thread.
    public sealed class TraceBundleWriter {
        private readonly BlockingCollection<TraceEvent> queue;
        private readonly Thread worker;
        private readonly string directory;
        private readonly TraceManifest manifest;
        private readonly long maxBytes;
        private long accepted, dropped, written, overhead;
        private int peakQueue;
        private volatile string error;
        private volatile bool ready, finished;
        public bool Ready => ready && error == null;
        public bool Finished => finished;
        public string Error => error;
        public long Dropped => Interlocked.Read(ref dropped);
        public int QueueDepth => queue.Count;
        public string DirectoryPath => directory;

        public TraceBundleWriter(string directory, TraceManifest manifest, int capacity = 4096, long maxBytes = 4L * 1024 * 1024 * 1024) {
            this.directory = directory;
            this.manifest = manifest;
            this.maxBytes = maxBytes;
            queue = new BlockingCollection<TraceEvent>(capacity);
            worker = new Thread(Write) { IsBackground = true, Name = "Netcode trace writer" };
            worker.Start();
        }

        public void Enqueue(TraceEvent record) {
            if (error != null || queue.IsAddingCompleted || !queue.TryAdd(record)) {
                Interlocked.Increment(ref dropped);
                return;
            }
            Interlocked.Increment(ref accepted);
            Interlocked.Add(ref overhead, record.overhead);
            peakQueue = Math.Max(peakQueue, queue.Count);
        }

        public void Stop(string failure = null) {
            if (failure != null) error = failure;
            if (!queue.IsAddingCompleted) queue.CompleteAdding();
        }
        public bool Wait(int milliseconds) => worker.Join(milliseconds);

        private void Write() {
            try {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonUtility.ToJson(manifest, true));
                using (var file = new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var output = new StreamWriter(file)) {
                    ready = true;
                    long bytes = 0;
                    foreach (TraceEvent record in queue.GetConsumingEnumerable()) {
                        string json = JsonUtility.ToJson(record);
                        bytes += System.Text.Encoding.UTF8.GetByteCount(json) + 2;
                        if (bytes > maxBytes) { error = "Capture disk budget exceeded"; Interlocked.Increment(ref dropped); continue; }
                        output.WriteLine(json);
                        Interlocked.Increment(ref written);
                        if (written % 128 == 0) output.Flush();
                    }
                    output.WriteLine(JsonUtility.ToJson(new TraceFooter {
                        accepted = accepted, dropped = dropped, written = written, error = error,
                        peakQueue = peakQueue, overhead = overhead, complete = error == null && dropped == 0
                    }));
                }
            } catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            finally { ready = false; finished = true; }
        }
    }

    public static class NetcodeTraceRecorder {
        internal static TraceBundleWriter Writer;
        internal static bool Recording;
        private static long sequence;
        public static string Status => Writer == null ? "Capture idle" :
            (Writer.Error != null ? "Capture FAILED: " + Writer.Error : Recording ? "Recording" : Writer.Finished ? "Exported" : "Preparing / exporting") +
            " | queued " + Writer.QueueDepth + " | dropped " + Writer.Dropped;

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Record(NetworkedCharacter character, string type, int tick, MoveData state = null,
                                  InputFrame input = default, bool hasInput = false, string reason = null,
                                  int sourceTick = -1, int pass = 0, float delta = 0, uint payloadEpoch = uint.MaxValue) {
            if (!Recording || Writer == null) return;
            long start = Stopwatch.GetTimestamp();
            try {
                var time = character.TimeManager;
                var record = new TraceEvent {
                    type = type, tick = tick, sourceTick = sourceTick, inputTick = input.frame,
                    entity = character.ObjectId, owner = character.OwnerId, epoch = character.MovementEpoch,
                    payloadEpoch = payloadEpoch, hasState = state != null,
                    role = type == "presentation" ? "presentation" : character.IsServerInitialized ? "server" : character.IsOwner ? "owner" : "proxy",
                    localTick = time != null ? time.LocalTick : 0, serverTick = time != null ? time.Tick : 0,
                    state = state == null ? null : TraceMovementState.Copy(state), input = input,
                    hasInput = hasInput, reason = reason, pass = pass, delta = delta,
                    monotonic = start, renderFrame = Time.frameCount, sequence = ++sequence
                };
                record.overhead = Stopwatch.GetTimestamp() - start;
                Writer.Enqueue(record);
            } catch (Exception ex) {
                // A diagnostic failure must never alter the simulation result.
                Recording = false;
                Writer.Enqueue(new TraceEvent { type = "captureFailure", reason = ex.Message, sequence = ++sequence });
                Writer.Stop("Capture exception: " + ex.Message);
            }
        }

        public static void Marker(string id, string note, int originalTick, uint receiveTick, bool remote) {
            if (!Recording || Writer == null) return;
            Writer.Enqueue(new TraceEvent { type = remote ? "markerReceived" : "marker", marker = id,
                reason = note, tick = originalTick, serverTick = receiveTick, renderFrame = Time.frameCount,
                monotonic = Stopwatch.GetTimestamp(), sequence = ++sequence });
        }
    }
}

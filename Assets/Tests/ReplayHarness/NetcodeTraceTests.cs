using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Fragsurf.Movement;
using GAUNTLET.Rollback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NetcodeTraceTests {
    [Test] public void MovementContractMatchesExistingSerializerAndPreservesRawBits() {
        CollectionAssert.AreEquivalent(WorldMovementSerializer.Fields,
            typeof(TraceMovementState).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));
        var source = new MoveData { origin = new Vector3(-0f, float.PositiveInfinity, 1.2345678f), coyoteTimer = .072f };
        var copy = TraceMovementState.Copy(source);
        source.coyoteTimer = 3;
        Assert.AreEqual(TraceMovementState.Bits(.072f), copy.coyoteTimer);
        Assert.AreEqual(TraceMovementState.Bits(-0f), copy.origin.x);
        Assert.AreEqual(TraceMovementState.Bits(float.PositiveInfinity), copy.origin.y);
        Assert.AreEqual(TraceMovementState.Bits(1.2345678f), copy.origin.z);
        string json = JsonUtility.ToJson(copy);
        Assert.AreEqual(copy.origin.z, JsonUtility.FromJson<TraceMovementState>(json).origin.z);
    }

    [UnityTest] public IEnumerator WriterExportsDetachedEventsAndFooter() {
        string directory = Path.Combine(Application.temporaryCachePath, "TraceTest-" + Guid.NewGuid().ToString("N"));
        var writer = new TraceBundleWriter(directory, new TraceManifest { session = "test", process = "A" });
        try {
            float until = Time.realtimeSinceStartup + 5;
            while (!writer.Ready && writer.Error == null && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(writer.Ready, writer.Error);
            writer.Enqueue(new TraceEvent { type = "postTick", sequence = 1, state = TraceMovementState.Copy(new MoveData()) });
            writer.Stop();
            while (!writer.Finished && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(writer.Finished);
            string[] lines = File.ReadAllLines(Path.Combine(directory, "events.jsonl"));
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual("postTick", JsonUtility.FromJson<TraceEvent>(lines[0]).type);
            var footer = JsonUtility.FromJson<TraceFooter>(lines[1]);
            Assert.IsTrue(footer.complete);
            Assert.AreEqual(1, footer.written);
        } finally { writer.Stop(); writer.Wait(2000); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [UnityTest] public IEnumerator DiskLimitAndInvalidPathFailWithoutThrowingOnProducer() {
        string directory = Path.Combine(Application.temporaryCachePath, "TraceTest-" + Guid.NewGuid().ToString("N"));
        var writer = new TraceBundleWriter(directory, new TraceManifest(), 1, 1);
        writer.Enqueue(new TraceEvent { type = "over-budget" });
        writer.Stop();
        float until = Time.realtimeSinceStartup + 5;
        while (!writer.Finished && Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(writer.Finished);
        Assert.IsNotNull(writer.Error);
        Assert.Greater(writer.Dropped, 0);
        Assert.IsFalse(JsonUtility.FromJson<TraceFooter>(File.ReadAllLines(Path.Combine(directory, "events.jsonl")).Last()).complete);
        Directory.Delete(directory, true);
        File.WriteAllText(directory, "not a directory");
        var invalid = new TraceBundleWriter(directory, new TraceManifest());
        try {
            until = Time.realtimeSinceStartup + 5;
            while (!invalid.Finished && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(invalid.Error);
            Assert.DoesNotThrow(() => invalid.Enqueue(new TraceEvent()));
        } finally { invalid.Stop(); invalid.Wait(2000); File.Delete(directory); }
    }

    [UnityTest] public IEnumerator ControlChannelExchangesBoundedMessagesAndReportsDisconnect() {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using (var host = new CaptureControlChannel(true, "127.0.0.1", port)) {
            // Allow the dedicated listener thread to bind before connecting.
            yield return new WaitForSecondsRealtime(.1f);
            using (var peer = new CaptureControlChannel(false, "127.0.0.1", port)) {
                peer.Send(new CaptureCommand { type = "hello", code = "ABC123", session = "session" });
                float until = Time.realtimeSinceStartup + 5;
                while (host.Incoming.IsEmpty && host.Error == null && Time.realtimeSinceStartup < until) yield return null;
                Assert.IsNull(host.Error);
                Assert.IsTrue(host.Incoming.TryDequeue(out var received));
                Assert.AreEqual("ABC123", received.code);
                Assert.AreEqual("hello", received.type);
            }
            float timeout = Time.realtimeSinceStartup + 5;
            while (host.Error == null && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.IsNotNull(host.Error);
        }
    }
}

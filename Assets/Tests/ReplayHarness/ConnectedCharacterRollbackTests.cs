using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using Fragsurf.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Fragsurf.ReplayHarness {
    public class ConnectedCharacterRollbackTests {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<SinglePrefabObjects> _collections = new List<SinglePrefabObjects>();
        private NetworkManager _server;
        private NetworkManager _client;
        private NetworkedCharacter _serverPlayer;
        private NetworkedCharacter _clientPlayer;
        private bool _oldAutoCreate;
        private Action _sendInput;

        [UnityTest]
        public IEnumerator ConnectedOwnerReplaysOnce_WithLatencyAndRespawn() {
            _oldAutoCreate = GAUNTLET.Networking.PlayerSpawnService.AutoCreateEnabled;
            GAUNTLET.Networking.PlayerSpawnService.AutoCreateEnabled = false;
            if (GAUNTLET.Networking.PlayerSpawnService.Instance != null)
                Object.DestroyImmediate(GAUNTLET.Networking.PlayerSpawnService.Instance.gameObject);

            // Real socket connection, two independent managers, shared immutable
            // empty collision world. Rendering and physical keyboard input are
            // excluded so the test sends a known canonical input stream.
            _server = MakeManager("RollbackServer");
            _client = MakeManager("RollbackClient");
            NetworkObject prefab = MakePrefab();
            _server.SpawnablePrefabs.AddObject(prefab);
            _client.SpawnablePrefabs.AddObject(prefab);
            ushort port;
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            Assert.IsTrue(_server.ServerManager.StartConnection(port));
            yield return Until(() => _server.ServerManager.Started, "server start");
            Assert.IsTrue(_client.ClientManager.StartConnection("127.0.0.1", port));
            yield return Until(() => _server.ServerManager.Clients.Values.Any(c => c.LoadedStartScenes(true)), "client scene handshake");
            var connection = _server.ServerManager.Clients.Values.First(c => c.LoadedStartScenes(true));
            NetworkObject instance = Object.Instantiate(prefab, new Vector3(0f, 100f, 0f), Quaternion.identity);
            _objects.Add(instance.gameObject);
            instance.gameObject.SetActive(true);
            _server.ServerManager.Spawn(instance, connection);
            _server.SceneManager.AddOwnerToDefaultScene(instance);
            _serverPlayer = instance.GetComponent<NetworkedCharacter>();
            _serverPlayer.Character.enabled = false;
            _serverPlayer.ApplyAuthoritativeSpawnPoseServer(new Vector3(0f, 100f, 0f), Quaternion.identity);
            yield return Until(() => _client.ClientManager.Objects.Spawned.Values.Any(o => o.IsOwner), "owned client spawn");
            _clientPlayer = _client.ClientManager.Objects.Spawned.Values.First(o => o.IsOwner).GetComponent<NetworkedCharacter>();
            _objects.Add(_clientPlayer.gameObject);
            _clientPlayer.Character.enabled = false;
            Assert.IsTrue(_clientPlayer.IsOwner);
            Assert.IsFalse(_clientPlayer.IsServerInitialized, "Must exercise a remote owner rather than host shortcuts.");

            var normalTick = (Action)Delegate.CreateDelegate(typeof(Action), _clientPlayer,
                typeof(NetworkedCharacter).GetMethod("TimeManager_OnTick", BindingFlags.Instance | BindingFlags.NonPublic));
            _client.TimeManager.OnTick -= normalTick;
            MethodInfo runInputs = typeof(NetworkedCharacter).GetMethod("RunInputs", BindingFlags.Instance | BindingFlags.NonPublic);
            _sendInput = () => {
                int tick = (int)_client.TimeManager.LocalTick;
                InputFrame input = InputFrame.Create(tick, _clientPlayer.ObjectId, 0, 0, 127, 0, 0f, 0f);
                runInputs.Invoke(_clientPlayer, new object[] {
                    new NetworkedCharacterReplicateData(input, _clientPlayer.MovementEpoch), ReplicateState.Invalid, Channel.Unreliable
                });
            };
            _client.TimeManager.OnTick += _sendInput;
            yield return Until(() => _clientPlayer.MovementEpoch == 1 && _clientPlayer.ReconcilePacketsReceived > 10, "initial reconciliation");

            var latency = _client.TransportManager.LatencySimulator;
            latency.SetLatency(80);
            latency.SetOutOfOrder(0.1);
            latency.SetPacketLoss(0.05);
            latency.SetEnabled(true);
            yield return new WaitForSecondsRealtime(1f);

            var failures = new List<string>();
            int checkedReplayTicks = 0;
            bool monitor = true;
            bool checkingTick = false;
            float beforeTimer = 0f;
            var prediction = _client.GetComponent<FishNet.Managing.Predicting.PredictionManager>();
            prediction.OnPreReplicateReplay += (local, remote) => {
                checkingTick = monitor && _clientPlayer.IsBehaviourReconciling &&
                    _clientPlayer.LastCorrectedTick == (int)prediction.ClientStateTick;
                if (!checkingTick) return;
                if (_clientPlayer.CurrentMoveFrame != (int)local - 1 && failures.Count < 5)
                    failures.Add($"Before replay {local}: state was {_clientPlayer.CurrentMoveFrame}.");
                beforeTimer = _clientPlayer.CurrentMoveData.jumpTimer;
            };
            prediction.OnPostReplicateReplay += (local, remote) => {
                if (!checkingTick) return;
                checkedReplayTicks++;
                if (_clientPlayer.CurrentMoveFrame != (int)local && failures.Count < 5)
                    failures.Add($"After replay {local}: state was {_clientPlayer.CurrentMoveFrame}.");
                float advanced = _clientPlayer.CurrentMoveData.jumpTimer - beforeTimer;
                if (Mathf.Abs(advanced - (float)_client.TimeManager.TickDelta) > 0.00001f && failures.Count < 5)
                    failures.Add($"Replay {local} advanced timer by {advanced}, expected {_client.TimeManager.TickDelta}.");
            };
            Vector3 startingPosition = _serverPlayer.CurrentPosition;
            yield return new WaitForSecondsRealtime(1.5f);
            TestContext.WriteLine($"Checked {checkedReplayTicks} delayed replay ticks before respawn.");
            Assert.Greater(checkedReplayTicks, 20, "Must actually perform delayed owner replay.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Greater(_serverPlayer.CurrentPosition.z, startingPosition.z + 0.1f, "Server must consume the remote input stream.");

            monitor = false;
            _serverPlayer.CurrentMoveData.maxStaminaBonus = 2f;
            _serverPlayer.ApplyAuthoritativeSpawnPoseServer(new Vector3(30f, 100f, 40f), Quaternion.identity);
            yield return Until(() => _clientPlayer.MovementEpoch == 2 && _clientPlayer.CurrentPosition.x > 29f, "respawn generation");
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual(2f, _clientPlayer.CurrentMoveData.maxStaminaBonus, "Upgrade modifier must cross the real network.");
            checkedReplayTicks = 0;
            monitor = true;
            yield return new WaitForSecondsRealtime(1f);
            TestContext.WriteLine($"Checked {checkedReplayTicks} delayed replay ticks after respawn.");
            Assert.Greater(checkedReplayTicks, 20);
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Greater(_clientPlayer.CurrentPosition.x, 29f, "Old-life traffic must not undo respawn.");
        }

        private NetworkManager MakeManager(string name) {
            GameObject root = new GameObject(name);
            _objects.Add(root);
            root.SetActive(false);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex($"^SpawnablePrefabs is null on {name}\\."));
            NetworkManager manager = root.AddComponent<NetworkManager>();
            SetField(manager, "_persistence", NetworkManager.PersistenceType.AllowMultiple);
            SetField(manager, "_dontDestroyOnLoad", false);
            var collection = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _collections.Add(collection);
            manager.SpawnablePrefabs = collection;
            root.SetActive(true);
            manager.TimeManager.SetTickRate(60);
            return manager;
        }

        private NetworkObject MakePrefab() {
            GameObject root = new GameObject("SocketTestCharacterPrefab");
            _objects.Add(root);
            root.SetActive(false);
            var view = new GameObject("View");
            view.transform.SetParent(root.transform, false);
            var aiming = view.AddComponent<PlayerAiming>();
            aiming.bodyTransform = view.transform;
            aiming.enabled = false;
            var character = root.AddComponent<SurfCharacter>();
            character.viewTransform = view.transform;
            character.playerRotationTransform = view.transform;
            character.movementConfig.gravity = 0f;
            root.AddComponent<RollbackManager>();
            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_enablePrediction", true);
            root.AddComponent<NetworkedCharacter>();
            return nob;
        }

        private static void SetField(object target, string name, object value) {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            field.SetValue(target, value);
        }

        private static IEnumerator Until(Func<bool> predicate, string stage) {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(predicate(), $"Timed out waiting for {stage}.");
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            if (_client != null) {
                if (_sendInput != null) _client.TimeManager.OnTick -= _sendInput;
                _client.ClientManager.StopConnection();
            }
            if (_server != null) _server.ServerManager.StopConnection(true);
            yield return new WaitForSecondsRealtime(0.1f);
            foreach (GameObject go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var collection in _collections)
                Object.DestroyImmediate(collection);
            _objects.Clear();
            _collections.Clear();
            GAUNTLET.Networking.PlayerSpawnService.AutoCreateEnabled = _oldAutoCreate;
        }
    }
}



using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Prediction.Delegating;
using Fragsurf.Movement;
using GAUNTLET.Gameplay.Pickups;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Fragsurf.ReplayHarness {
    public class ReliableCharacterRollbackTests {
        private const float Dt = 1f / 60f;
        private GameObject _managerRoot;
        private GameObject _root;
        private SurfCharacter _character;
        private RollbackManager _rollback;
        private NetworkedCharacter _networked;
        private NetworkedCharacterPredictionReconcileService _service;
        private readonly Dictionary<int, InputFrame> _inputs = new Dictionary<int, InputFrame>();

        [SetUp]
        public void SetUp() {
            _managerRoot = new GameObject("RollbackTestNetworkManager");
            _managerRoot.SetActive(false);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("^SpawnablePrefabs is null on RollbackTestNetworkManager\\."));
            var manager = _managerRoot.AddComponent<FishNet.Managing.NetworkManager>();
            manager.SpawnablePrefabs = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
            _managerRoot.SetActive(true);

            _root = new GameObject("RollbackCharacter");
            _root.SetActive(false);
            _root.transform.position = Vector3.up * 100f;
            GameObject view = new GameObject("View");
            view.transform.SetParent(_root.transform, false);
            PlayerAiming aiming = view.AddComponent<PlayerAiming>();
            aiming.bodyTransform = view.transform;
            aiming.enabled = false;
            _character = _root.AddComponent<SurfCharacter>();
            _character.viewTransform = view.transform;
            _character.playerRotationTransform = view.transform;
            _character.solidCollider = false;
            _rollback = _root.AddComponent<RollbackManager>();
            var networkObject = _root.AddComponent<NetworkObject>();
            _networked = _root.AddComponent<NetworkedCharacter>();
            FishNet.Managing.Object.ManagedObjects.InitializePrefab(networkObject, 0);
            _root.SetActive(true);
            _character.enabled = false;
            _service = (NetworkedCharacterPredictionReconcileService)typeof(NetworkedCharacter)
                .GetField("_predictionReconcileService", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_networked);
            _character.moveData.frame = 0;
            _character.moveData.origin = Vector3.up * 100f;
            _character.moveData.velocity = new Vector3(2f, 3f, 4f);
            _rollback.Initialize(_character);
            _inputs.Clear();
            for (int tick = 1; tick <= 30; tick++)
                _inputs[tick] = InputFrame.Create(tick, _networked.ObjectId, 0, 40, 100, 0, 20f, 0f);
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_root);
            var prefabs = _managerRoot.GetComponent<FishNet.Managing.NetworkManager>().SpawnablePrefabs;
            Object.DestroyImmediate(_managerRoot);
            Object.DestroyImmediate(prefabs);
        }

        private void Advance(int from, int to) {
            for (int tick = from; tick <= to; tick++)
                _rollback.SimulatePredictedTick(_inputs[tick], Dt, false);
        }

        [TestCase(0f)]
        [TestCase(0.01f)]
        [TestCase(1f)]
        public void ReconcileRestoresOnly_ThenFishNetReplayAdvancesExactlyOnce(float correction) {
            Advance(1, 12);
            MoveData present = _character.moveData.Clone();
            Assert.IsTrue(_rollback.TryGetPredictedState(4, out MoveData baseline));
            baseline.origin.x += correction;
            _character.moveData = baseline;
            NetworkedCharacterReconcileData packet = WireRoundTrip(_service.BuildReconcileData(900));
            packet.SetTick(4); // Server time differs from the owner's input clock.
            _character.moveData = present;

            _service.ApplyReconcile(packet, Dt);
            Assert.AreEqual(4, _rollback.CurrentTick, "Reconcile must not run a nested replay.");
            Assert.AreEqual(baseline.origin, _character.moveData.origin);
            if (correction < 0.25f)
                Assert.AreEqual(CorrectionDecision.Ignore, _service.Diagnostics.LastDecision);

            InputFrame last = default;
            bool hasLast = false;
            // Exercise the vendored FishNet history dispatcher, not a second
            // handwritten loop that bypasses Replicate_Replay_Authoritative.
            MethodInfo replay = typeof(NetworkBehaviour).GetMethod("Replicate_Replay_Authoritative",
                BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(typeof(NetworkedCharacterReplicateData));
            Type historyType = replay.GetParameters()[2].ParameterType;
            object history = Activator.CreateInstance(historyType);
            Type entryType = historyType.GetGenericArguments()[0];
            ReplicateUserLogicDelegate<NetworkedCharacterReplicateData> replayLogic = (data, state, channel) =>
                _service.RunReplicate(data, state, Dt, ref last, ref hasLast);
            for (int tick = 5; tick <= 12; tick++) {
                var input = WireRoundTrip(new NetworkedCharacterReplicateData(_inputs[tick]));
                object entry = Activator.CreateInstance(entryType, input, FishNet.Transporting.Channel.Unreliable, (uint)tick, true);
                historyType.GetMethod("Add").Invoke(history, new[] { entry });
                replay.Invoke(_networked, new object[] { (uint)tick, replayLogic, history });
                Assert.AreEqual(tick, _character.moveData.frame);
                Assert.AreEqual(tick, _rollback.CurrentTick);
            }
            MoveData replayed = _character.moveData.Clone();
            _character.LoadState(baseline);
            _rollback.Initialize(_character);
            Advance(5, 12);
            AssertExactState(_character.moveData, replayed);
        }

        [TestCase(5)]
        [TestCase(8)]
        [TestCase(12)]
        public void MissingReplayInputLeavesStateAndHistoryUntouched(int missingTick) {
            Advance(1, 12);
            MoveData present = _character.moveData.Clone();
            Assert.IsTrue(_rollback.TryGetPredictedState(4, out MoveData baseline));
            baseline.origin.x += 10f;
            Assert.IsFalse(_rollback.ApplyAuthoritativeCorrection(baseline, Dt,
                tick => tick == missingTick ? InputFrame.Empty() : _inputs[tick], _networked.ObjectId));
            Assert.IsNotEmpty(_rollback.LastReplayError);
            Assert.AreEqual(12, _rollback.CurrentTick);
            AssertExactState(present, _character.moveData);
            Assert.IsTrue(_rollback.TryGetPredictedState(4, out MoveData retained));
            Assert.AreNotEqual(baseline.origin, retained.origin);
            Assert.Zero(_rollback.RollbackCount);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WrongReplayIdentityIsRejectedBeforeRestore(bool wrongTick) {
            Advance(1, 12);
            MoveData present = _character.moveData.Clone();
            _rollback.TryGetPredictedState(4, out MoveData baseline);
            Assert.IsFalse(_rollback.ApplyAuthoritativeCorrection(baseline, Dt, tick => {
                InputFrame input = _inputs[tick];
                if (tick == 7) {
                    if (wrongTick) input.frame++;
                    else input.characterObjectId++;
                }
                return input;
            }, _networked.ObjectId));
            AssertExactState(present, _character.moveData);
        }

        [Test]
        public void OversizedReplayIsRejectedWithoutCallingProvider() {
            _character.moveData.frame = 300;
            _rollback.Initialize(_character);
            MoveData snapshot = _character.moveData.Clone();
            snapshot.frame = 0;
            Assert.IsFalse(_rollback.ApplyAuthoritativeCorrection(snapshot, Dt, tick => throw new Exception("Must not enumerate.")));
            Assert.AreEqual(300, _rollback.CurrentTick);
            Assert.AreEqual(300, _character.moveData.frame);
        }

        [Test]
        public void RepeatedRollbackMatchesEverySavedFieldExactly() {
            Advance(1, 30);
            MoveData expected = _character.moveData.Clone();
            foreach (int cut in new[] { 1, 7, 20, 7 }) {
                Assert.IsTrue(_rollback.TryGetPredictedState(cut, out MoveData snapshot));
                Assert.IsTrue(_rollback.ApplyAuthoritativeCorrection(snapshot, Dt, tick => _inputs[tick], _networked.ObjectId));
                AssertExactState(expected, _character.moveData);
            }
        }

        [TestCase(7)]
        [TestCase(20)]
        public void LoadIntoFreshCharacterPreservesEveryFutureTick(int cut) {
            _character.moveData.maxStaminaBonus = 2f;
            _character.moveData.staminaRegenBonus = 1.5f;
            _character.moveData.stamina = 0.5f;
            _character.moveData.staminaRegenTimer = 0.15f;
            _character.moveData.slidingEnabled = true;
            for (int tick = 1; tick <= 30; tick++) {
                byte buttons = tick >= 10 && tick <= 24 ? InputFrame.BTN_CROUCH : (byte)0;
                if (tick == 15) buttons |= InputFrame.BTN_DASH;
                _inputs[tick] = InputFrame.Create(tick, _networked.ObjectId, buttons, 40, 100,
                    tick == 15 ? InputFrame.BTN_DASH : (byte)0, 20f, 0f);
            }
            Advance(1, 30);
            Assert.IsTrue(_rollback.TryGetPredictedState(cut, out MoveData snapshot));
            GameObject freshRoot = new GameObject("FreshSnapshotReceiver");
            freshRoot.SetActive(false);
            try {
                var view = new GameObject("View");
                view.transform.SetParent(freshRoot.transform, false);
                var aiming = view.AddComponent<PlayerAiming>();
                aiming.bodyTransform = view.transform;
                aiming.enabled = false;
                var fresh = freshRoot.AddComponent<SurfCharacter>();
                fresh.viewTransform = view.transform;
                fresh.playerRotationTransform = view.transform;
                // Identical immutable rules, independent component/controller state.
                fresh.movementConfig = _character.movementConfig;
                freshRoot.SetActive(true);
                fresh.enabled = false;
                fresh.LoadState(snapshot);
                Assert.AreSame(freshRoot.transform, fresh.moveData.playerTransform);
                Assert.AreSame(view.transform, fresh.moveData.viewTransform);
                AssertExactState(snapshot, fresh.moveData);
                for (int tick = cut + 1; tick <= 30; tick++) {
                    fresh.SimulationTick(fresh.moveData, _inputs[tick], Dt, false);
                    Assert.IsTrue(_rollback.TryGetPredictedState(tick, out MoveData expected));
                    AssertExactState(expected, fresh.moveData);
                }
            } finally { Object.DestroyImmediate(freshRoot); }
        }

        [Test]
        public void MissingInputRepeatsRestoredHeldControlsWithoutPressEdges() {
            InputFrame previous = InputFrame.Create(7, _networked.ObjectId, InputFrame.BTN_DASH, 45, 127,
                InputFrame.BTN_DASH, 123f, -12f);
            var missing = default(NetworkedCharacterReplicateData);
            missing.SetTick(8);
            Assert.IsTrue(NetworkedCharacterPredictionReconcileService.TryResolveInput(missing,
                ReplicateState.Ticked, previous, _networked.ObjectId, out InputFrame result));
            Assert.AreEqual(8, result.frame);
            Assert.AreEqual(previous.buttons, result.buttons);
            Assert.AreEqual(previous.lookYaw100, result.lookYaw100);
            Assert.AreEqual(previous.stickY, result.stickY);
            Assert.Zero(result.justPressed);

            var invalid = new NetworkedCharacterReplicateData(InputFrame.Empty());
            invalid.SetTick(9);
            Assert.IsTrue(NetworkedCharacterPredictionReconcileService.TryResolveInput(invalid,
                ReplicateState.Created, previous, _networked.ObjectId, out result));
            Assert.AreEqual(previous.lookYaw100, result.lookYaw100, "Stamping a tick must not turn an absent payload into neutral input.");
            Assert.Zero(result.justPressed);
        }

        [Test]
        public void CreatedInputPreservesPressEdgesAndRejectsWrongEntity() {
            InputFrame pressed = InputFrame.Create(7, _networked.ObjectId, InputFrame.BTN_BLOCK, 0, 0, InputFrame.BTN_BLOCK, 0f, 0f);
            var data = new NetworkedCharacterReplicateData(pressed);
            data.SetTick(8);
            Assert.IsTrue(NetworkedCharacterPredictionReconcileService.TryResolveInput(data,
                ReplicateState.Created | ReplicateState.Replayed, InputFrame.Empty(), _networked.ObjectId, out InputFrame result));
            Assert.AreEqual(InputFrame.BTN_BLOCK, result.justPressed);
            data.Input.characterObjectId++;
            Assert.IsFalse(NetworkedCharacterPredictionReconcileService.TryResolveInput(data,
                ReplicateState.Created, InputFrame.Empty(), _networked.ObjectId, out result));
        }

        [Test]
        public void UpgradeSnapshotRestoresRulesWithoutMutatingBaseConfiguration() {
            var capacity = ScriptableObject.CreateInstance<MaxStaminaUpgradeEffectDefinition>();
            var regen = ScriptableObject.CreateInstance<StaminaRegenUpgradeEffectDefinition>();
            try {
                float baseMax = _character.movementConfig.maxStamina;
                float baseRegen = _character.movementConfig.staminaRegenRate;
                MoveData before = _character.moveData.Clone();
                var context = new PickupEffectContext(null, null, null,
                    new PickupEffectTarget(_root, _character, _networked), true);
                Assert.IsTrue(capacity.TryApply(context));
                Assert.IsTrue(regen.TryApply(context));
                MoveData upgraded = _character.moveData.Clone();
                Assert.AreEqual(baseMax + 2f, _character.MaxStamina);
                Assert.AreEqual(baseRegen + 1f, _character.StaminaRegenRate);
                Assert.AreEqual(baseMax, _character.movementConfig.maxStamina);
                Assert.AreEqual(baseRegen, _character.movementConfig.staminaRegenRate);
                Assert.AreEqual(upgraded.maxStaminaBonus, _character.BuildRespawnState(Vector3.zero, 0f, 0f).maxStaminaBonus);
                NetworkedCharacterReconcileData packet = WireRoundTrip(_service.BuildReconcileData(0));
                packet.SetTick(0);
                _character.LoadState(before);
                Assert.AreEqual(baseMax, _character.MaxStamina);
                Assert.AreEqual(baseRegen, _character.StaminaRegenRate);
                _service.ApplyReconcile(packet, Dt);
                Assert.AreEqual(baseMax + 2f, _character.MaxStamina);
                Assert.AreEqual(baseRegen + 1f, _character.StaminaRegenRate);
                _character.moveData.stamina = 0f;
                _character.moveData.staminaRegenTimer = 0f;
                Advance(1, 1);
                Assert.AreEqual((baseRegen + 1f) * Dt, _character.moveData.stamina);
            } finally {
                Object.DestroyImmediate(capacity);
                Object.DestroyImmediate(regen);
            }
        }

        [Test]
        public void CoyoteTimer_ReconcileWireRoundTripAndRespawn() {
            _character.moveData.coyoteTimer = 0.075f;
            NetworkedCharacterReconcileData packet = WireRoundTrip(_service.BuildReconcileData(0));
            packet.SetTick(0);
            _character.moveData.coyoteTimer = 0f;
            _service.ApplyReconcile(packet, Dt);
            Assert.AreEqual(0.075f, _character.moveData.coyoteTimer);
            Assert.Zero(_character.BuildRespawnState(Vector3.up * 10f, 0f, 0f).coyoteTimer);
        }

        [Test]
        public void OldEpochReconcileAndItsReplayCannotMoveNewLife() {
            Advance(1, 4);
            NetworkedCharacterReconcileData old = _service.BuildReconcileData(4);
            Assert.IsTrue(_networked.AcceptMovementEpoch(1));
            _character.LoadState(_character.BuildRespawnState(new Vector3(30f, 100f, 40f), 0f, 0f));
            _rollback.Initialize(_character);
            MoveData spawned = _character.moveData.Clone();
            _service.ApplyReconcile(old, Dt);
            InputFrame last = default;
            bool hasLast = false;
            var data = new NetworkedCharacterReplicateData(_inputs[5], 0);
            data.SetTick(5);
            _service.RunReplicate(data, ReplicateState.Replayed | ReplicateState.Created, Dt, ref last, ref hasLast);
            AssertExactState(spawned, _character.moveData);
            Assert.IsFalse(_rollback.TryGetPredictedState(4, out _));
        }

        [Test]
        public void CapsuleRestoreAndCrouchUseIdenticalMinimumHeight() {
            GameObject colliderRoot = _character.collider.gameObject;
            Object.DestroyImmediate(_character.collider);
            var capsule = colliderRoot.AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.isTrigger = true;
            typeof(SurfCharacter).GetField("_collider", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_character, capsule);
            MoveData snapshot = _character.moveData.Clone();
            snapshot.crouchingHeight = 0.1f;
            snapshot.crouching = true;
            _character.LoadState(snapshot);
            Assert.AreEqual(1f, capsule.height, "LoadState must clamp to the capsule diameter.");
            var crouch = InputFrame.Create(1, _networked.ObjectId, InputFrame.BTN_CROUCH, 0, 0, 0, 0f, 0f);
            _character.SimulationTick(_character.moveData, crouch, Dt, false);
            Assert.AreEqual(1f, capsule.height, "The next crouch tick must preserve the restored geometry.");
        }

        private T WireRoundTrip<T>(T value) {
            var manager = _managerRoot.GetComponent<FishNet.Managing.NetworkManager>();
            var writer = FishNet.Serializing.WriterPool.Retrieve(manager);
            try {
                writer.Write(value);
                var reader = FishNet.Serializing.ReaderPool.Retrieve(writer.GetArraySegment(), manager);
                try { return reader.Read<T>(); }
                finally { reader.Store(); }
            } finally { writer.Store(); }
        }

        private static void AssertExactState(MoveData expected, MoveData actual) {
            foreach (FieldInfo field in typeof(MoveData).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if (typeof(Object).IsAssignableFrom(field.FieldType)) continue;
                Assert.AreEqual(field.GetValue(expected), field.GetValue(actual), field.Name);
            }
        }
    }
}

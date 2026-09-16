using System;
using System.Linq;
using System.Reflection;
using FishNet.Serializing;
using Fragsurf.Movement;
using GAUNTLET.Rollback;
using NUnit.Framework;
using UnityEngine;

namespace Fragsurf.ReplayHarness {
    public class WorldRollbackHistoryTests {
        [Test]
        public void MovementWireManifestIncludesEveryValueField() {
            string[] values = typeof(MoveData).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                .Select(f => f.Name).ToArray();
            CollectionAssert.AreEquivalent(values, WorldMovementSerializer.Fields,
                "Update the versioned wire snapshot when adding movement state.");
        }

        [Test]
        public void MovementWireRoundTripPreservesEveryValueExactly() {
            var source = new MoveData();
            int index = 0;
            foreach (FieldInfo field in typeof(MoveData).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                index++;
                if (field.FieldType == typeof(float)) field.SetValue(source, index * 0.03125f);
                else if (field.FieldType == typeof(int)) field.SetValue(source, index * 7);
                else if (field.FieldType == typeof(bool)) field.SetValue(source, true);
                else if (field.FieldType == typeof(Vector3)) field.SetValue(source, new Vector3(index, -index, 0.0625f));
                else if (field.FieldType.IsEnum) field.SetValue(source, Enum.ToObject(field.FieldType, 1));
                else if (field.FieldType == typeof(InputFrame)) field.SetValue(source,
                    InputFrame.Create(17, 3, InputFrame.BTN_MELEE, -12, 34, InputFrame.BTN_BLOCK, 123f, -15f));
            }
            PooledWriter writer = WriterPool.Retrieve();
            try {
                writer.Write(source);
                PooledReader reader = ReaderPool.Retrieve(writer.GetArraySegment(), null);
                try {
                    MoveData restored = reader.Read<MoveData>();
                    foreach (FieldInfo field in typeof(MoveData).GetFields(BindingFlags.Public | BindingFlags.Instance))
                        Assert.AreEqual(field.GetValue(source), field.GetValue(restored), field.Name);
                    Assert.Zero(reader.Remaining, "Wire reader and writer must agree on the full payload.");
                } finally { reader.Store(); }
            } finally { writer.Store(); }
        }

        private static WorldSnapshot Initial() => new WorldSnapshot {
            Tick = 0, RandomState = 1234,
            Characters = new[] {
                new WorldCharacterState { Id = 3, Health = 3 },
                new WorldCharacterState { Id = 8, Health = 3 }
            },
            Pickups = new[] { new WorldPickupState { Id = 12, Available = true, NextSpawnTick = 5 } }
        };

        private static InputFrame[] Inputs(int tick) => new[] {
            InputFrame.Empty(tick, 3), InputFrame.Empty(tick, 8)
        };

        private static void Step(WorldSnapshot world, InputFrame[] inputs) {
            world.Tick++;
            foreach (WorldCharacterState character in world.Characters) {
                character.Movement.frame = world.Tick;
                character.Movement.origin.x += WorldRandom.Range(ref world.RandomState, 10);
            }
            if (world.Tick == 2) {
                world.Characters[0].Health--;
                world.Characters[1].Health--;
                world.Pickups[0].Available = false;
            }
            if (world.Tick == world.Pickups[0].NextSpawnTick) {
                world.Pickups[0].Available = true;
                world.Pickups[0].SpawnSequence++;
            }
        }

        [Test]
        public void RewindRestoresBothCharactersAndWorldRngWithoutAliasing() {
            var history = new WorldRollbackHistory(Initial(), Step);
            for (int tick = 1; tick <= 7; tick++) Assert.IsTrue(history.Advance(Inputs(tick)));
            WorldSnapshot expected = history.Capture();
            Assert.IsTrue(history.Correct(Initial()));
            WorldSnapshot actual = history.Capture();
            Assert.AreEqual(expected.RandomState, actual.RandomState);
            for (int i = 0; i < 2; i++) {
                Assert.AreEqual(expected.Characters[i].Health, actual.Characters[i].Health);
                Assert.AreEqual(expected.Characters[i].Movement.origin, actual.Characters[i].Movement.origin);
            }
            Assert.AreEqual(expected.Pickups[0], actual.Pickups[0]);
            actual.Characters[0].Health = 99;
            actual.Pickups[0].Available = false;
            Assert.AreEqual(2, history.Capture().Characters[0].Health);
            Assert.IsTrue(history.Capture().Pickups[0].Available);
        }

        [Test]
        public void MissingParticipantInMiddleOfReplayDoesNotChangeTimeline() {
            var history = new WorldRollbackHistory(Initial(), Step);
            for (int tick = 1; tick <= 7; tick++) history.Advance(Inputs(tick));
            WorldSnapshot before = history.Capture();
            Assert.IsFalse(history.Correct(Initial(), tick => tick == 4 ? new[] { Inputs(tick)[0] } : Inputs(tick)));
            Assert.AreEqual(before.RandomState, history.Capture().RandomState);
            Assert.AreEqual(before.Tick, history.Capture().Tick);
            Assert.IsTrue(history.TryGet(2, out WorldSnapshot second));
            Assert.AreEqual(2, second.Characters[1].Health);
        }

        [Test]
        public void SimulationFailureDoesNotCommitPartialWorldHistory() {
            bool fail = false;
            var history = new WorldRollbackHistory(Initial(), (world, inputs) => {
                Step(world, inputs);
                if (fail && world.Tick == 4) throw new InvalidOperationException("Synthetic failure");
            });
            for (int tick = 1; tick <= 7; tick++) history.Advance(Inputs(tick));
            WorldSnapshot before = history.Capture();
            fail = true;
            Assert.Throws<InvalidOperationException>(() => history.Correct(Initial()));
            Assert.AreEqual(before.Tick, history.Capture().Tick);
            Assert.AreEqual(before.RandomState, history.Capture().RandomState);
        }
    }
}

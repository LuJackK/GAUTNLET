using GAUNTLET.Rollback;
using NUnit.Framework;
using UnityEngine;

namespace Fragsurf.ReplayHarness {
    public class WorldCombatResolverTests {
        private static WorldSnapshot Duel() => new WorldSnapshot {
            Tick = 10,
            Characters = new[] {
                new WorldCharacterState { Id = 3, Health = 1 },
                new WorldCharacterState { Id = 8, Health = 1 }
            }
        };

        [TestCase(false)]
        [TestCase(true)]
        public void LethalTradesAreIndependentOfIntentOrder(bool reverse) {
            WorldSnapshot world = Duel();
            var a = new WorldHitIntent { Attacker = 3, Target = 8, Damage = 1 };
            var b = new WorldHitIntent { Attacker = 8, Target = 3, Damage = 1 };
            WorldEvent[] events = WorldCombatResolver.Resolve(world, reverse ? new[] { b, a } : new[] { a, b }, id => 30);
            Assert.Zero(world.Characters[0].Health);
            Assert.Zero(world.Characters[1].Health);
            Assert.AreEqual(40, world.Characters[0].RespawnTick);
            Assert.AreEqual(40, world.Characters[1].RespawnTick);
            Assert.AreEqual(4, events.Length);
            Assert.AreEqual(3, events[0].Source);
            Assert.AreEqual(8, events[1].Source);
        }

        [Test]
        public void ParryReflectsDamageOnceAndStopsAttackerMomentum() {
            WorldSnapshot world = Duel();
            world.Characters[1].Movement.isParrying = true;
            world.Characters[0].Movement.velocity = new Vector3(12f, 2f, 30f);
            var hit = new WorldHitIntent { Attacker = 3, Target = 8, Damage = 1 };
            WorldEvent[] events = WorldCombatResolver.Resolve(world, new[] { hit, hit }, id => 5);
            Assert.Zero(world.Characters[0].Health);
            Assert.AreEqual(1, world.Characters[1].Health);
            Assert.IsTrue(world.Characters[0].Movement.gotParriedThisFrame);
            Assert.IsTrue(world.Characters[1].Movement.parrySuccessThisFrame);
            Assert.AreEqual(new Vector3(0f, 2f, 0f), world.Characters[0].Movement.velocity);
            Assert.AreEqual(2, events.Length, "Duplicate overlap must not duplicate damage/events.");
        }

        [Test]
        public void RewindBeforeHitRecomputesOutcomeAgainstRestoredDefender() {
            WorldSnapshot baseline = Duel();
            var hit = new[] { new WorldHitIntent { Attacker = 3, Target = 8, Damage = 1 } };
            WorldSnapshot predicted = baseline.Clone();
            WorldCombatResolver.Resolve(predicted, hit, id => 5);
            Assert.Zero(predicted.Characters[1].Health);
            // A newly received input changes the historical defender's parry state.
            WorldSnapshot corrected = baseline.Clone();
            corrected.Characters[1].Movement.isParrying = true;
            WorldCombatResolver.Resolve(corrected, hit, id => 5);
            Assert.AreEqual(1, corrected.Characters[1].Health);
            Assert.Zero(corrected.Characters[0].Health);
            Assert.AreEqual(1, baseline.Characters[0].Health);
            Assert.AreEqual(1, baseline.Characters[1].Health);
        }
    }
}

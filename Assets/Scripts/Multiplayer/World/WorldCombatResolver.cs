using System;
using System.Collections.Generic;
using UnityEngine;

namespace GAUNTLET.Rollback {
    public struct WorldHitIntent {
        public int Attacker;
        public int Target;
        public int Damage;
    }

    public enum WorldEventKind : byte { Hit, Parry, Death, Respawn, Pickup }

    public struct WorldEvent {
        public int Tick;
        public WorldEventKind Kind;
        public int Source;
        public int Target;
        public uint TargetLife;
        public int Amount;
    }

    public static class WorldCombatResolver {
        // Queries collect every intent first. Resolution never cancels an intent
        // because its attacker was killed by another hit in this same batch.
        public static WorldEvent[] Resolve(WorldSnapshot world, WorldHitIntent[] intents,
            Func<int, int> respawnDelayTicks) {
            var characters = new Dictionary<int, WorldCharacterState>();
            var damage = new Dictionary<int, int>();
            foreach (WorldCharacterState character in world.Characters) {
                characters.Add(character.Id, character);
                damage.Add(character.Id, 0);
            }
            var ordered = (WorldHitIntent[])intents.Clone();
            Array.Sort(ordered, (a, b) => {
                int source = a.Attacker.CompareTo(b.Attacker);
                return source != 0 ? source : a.Target.CompareTo(b.Target);
            });
            var events = new List<WorldEvent>();
            int previousSource = -1, previousTarget = -1;
            foreach (WorldHitIntent hit in ordered) {
                if (hit.Attacker == previousSource && hit.Target == previousTarget) continue;
                previousSource = hit.Attacker;
                previousTarget = hit.Target;
                if (hit.Attacker == hit.Target ||
                    !characters.TryGetValue(hit.Attacker, out WorldCharacterState attacker) ||
                    !characters.TryGetValue(hit.Target, out WorldCharacterState target) ||
                    !attacker.Alive || !target.Alive) continue;

                attacker.Movement.hasHitTarget = true;
                attacker.Movement.meleeHitResolved = true;
                attacker.Movement.meleeHitResolveTick = world.Tick;
                attacker.Movement.meleeHitTargetObjectId = target.Id;
                attacker.Movement.meleeHitThisFrame = true;
                attacker.Movement.velocity.x = 0f;
                attacker.Movement.velocity.z = 0f;

                bool parried = target.Movement.isParrying;
                WorldCharacterState receiver = parried ? attacker : target;
                int amount = Mathf.Max(1, hit.Damage);
                damage[receiver.Id] = checked(damage[receiver.Id] + amount);
                if (parried) {
                    target.Movement.parrySuccessThisFrame = true;
                    attacker.Movement.gotParriedThisFrame = true;
                }
                events.Add(new WorldEvent {
                    Tick = world.Tick, Kind = parried ? WorldEventKind.Parry : WorldEventKind.Hit,
                    Source = attacker.Id, Target = receiver.Id, TargetLife = receiver.Life, Amount = amount
                });
            }
            // Stable ordering also keeps presentation event identity independent of
            // component registration or physics overlap order.
            var ids = new List<int>(characters.Keys);
            ids.Sort();
            foreach (int id in ids) {
                WorldCharacterState character = characters[id];
                if (!character.Alive || damage[id] == 0) continue;
                character.Health = Mathf.Max(0, character.Health - damage[id]);
                if (!character.Alive) {
                    character.RespawnTick = checked(world.Tick + Mathf.Max(0, respawnDelayTicks(id)));
                    events.Add(new WorldEvent {
                        Tick = world.Tick, Kind = WorldEventKind.Death, Target = id, TargetLife = character.Life
                    });
                }
            }
            return events.ToArray();
        }
    }
}

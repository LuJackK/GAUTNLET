using System;
using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Rollback {
    // Snapshots contain simulation values only. Scene objects and immutable rules
    // are resolved through stable IDs by the world driver, never stored here.
    public sealed class WorldSnapshot {
        public const int SchemaVersion = 1;
        public int Tick = -1;
        public uint RandomState = 1;
        public WorldCharacterState[] Characters = Array.Empty<WorldCharacterState>();
        public WorldPickupState[] Pickups = Array.Empty<WorldPickupState>();

        public WorldSnapshot Clone() {
            var copy = (WorldSnapshot)MemberwiseClone();
            copy.Characters = new WorldCharacterState[Characters.Length];
            for (int i = 0; i < Characters.Length; i++)
                copy.Characters[i] = Characters[i].Clone();
            copy.Pickups = (WorldPickupState[])Pickups.Clone();
            return copy;
        }
    }

    public sealed class WorldCharacterState {
        public int Id;
        public MoveData Movement = new MoveData();
        public int Health;
        public int RespawnTick = -1;
        public uint Life;
        public Vector3 SpawnPosition;
        public float SpawnYaw;
        public bool Alive => Health > 0;

        public WorldCharacterState Clone() {
            var copy = (WorldCharacterState)MemberwiseClone();
            copy.Movement = Movement.Clone();
            copy.Movement.playerTransform = null;
            copy.Movement.viewTransform = null;
            return copy;
        }
    }

    public struct WorldPickupState {
        public int Id;
        public int DefinitionIndex;
        public bool Available;
        public int NextSpawnTick;
        public uint SpawnSequence;
    }

    public static class WorldRandom {
        // A session-owned stream: visual effects and Unity.Random cannot advance it.
        public static uint Next(ref uint state) {
            if (state == 0) state = 0x6d2b79f5;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public static int Range(ref uint state, int count) {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            // Rejection sampling avoids modulo bias while preserving a restorable stream.
            uint bound = (uint)count;
            uint threshold = unchecked(0u - bound) % bound;
            uint value;
            do { value = Next(ref state); } while (value < threshold);
            return (int)(value % bound);
        }
    }
}

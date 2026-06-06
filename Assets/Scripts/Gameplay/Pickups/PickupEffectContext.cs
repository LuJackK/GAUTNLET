namespace GAUNTLET.Gameplay.Pickups
{
    public readonly struct PickupEffectContext
    {
        public PickupEffectContext(
            PickupDefinition definition,
            PickupWorldItem pickup,
            PickupSpawnLocation spawnLocation,
            PickupEffectTarget target,
            bool isServer)
        {
            Definition = definition;
            Pickup = pickup;
            SpawnLocation = spawnLocation;
            Target = target;
            IsServer = isServer;
        }

        public PickupDefinition Definition { get; }
        public PickupWorldItem Pickup { get; }
        public PickupSpawnLocation SpawnLocation { get; }
        public PickupEffectTarget Target { get; }
        public bool IsServer { get; }
    }
}

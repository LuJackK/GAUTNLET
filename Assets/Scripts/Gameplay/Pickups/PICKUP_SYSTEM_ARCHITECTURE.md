# Pickup Upgrade System

## Runtime Shape

- `PickupSpawnLocation` is a scene marker. Each marker owns its spawn interval and a pool of `PickupDefinition` assets that are valid at that location.
- `PickupSpawnDirector` owns timers. On the server, it chooses from each location pool, instantiates the chosen pickup prefab, and FishNet-spawns it when the prefab has a `NetworkObject`.
- `PickupWorldItem` is the visible pickup. It bobs and spins, listens for trigger contact, resolves the player target, applies the configured effect, then despawns or destroys itself.
- `PickupDefinition` is the asset designers pick in spawn pools. It names the upgrade type, points at the pickup prefab, and references a `PickupEffectDefinition`.
- `PickupEffectDefinition` is the extension point for concrete effects. Stamina upgrades, health pickups, timed buffs, and future powerups should each be separate ScriptableObject subclasses.

## Setup Flow

1. Add a `PickupSpawnDirector` to a scene or networked scene service object.
2. Add `PickupSpawnLocation` markers at preset spawn positions.
3. Create `PickupDefinition` assets through `Assets/Create/GAUNTLET/Pickups/Pickup Definition`.
4. Assign each location a pool of pickup definitions and tune its interval.
5. Give pickup prefabs a trigger collider and `PickupWorldItem`. For multiplayer pickup prefabs, also add/register a FishNet `NetworkObject`.

## Multiplayer Prefab Setup

Networked pickups should use a real prefab, not a raw imported `.obj` or `.fbx` model asset. The prefab root should contain:

- `PickupWorldItem`
- A trigger collider, usually `SphereCollider`
- FishNet `NetworkObject`
- A visible mesh or child visual

After creating the prefab, add its root `NetworkObject` to `Assets/DefaultPrefabObjects.asset`, then assign the prefab root to the `Pickup Definition` asset's `Pickup Prefab` field.

Current network-ready pickup prefabs live in `Assets/Prefabs/Pickups`.

## Next Effect Classes

- `StaminaRegenUpgradeEffectDefinition` increases `movementConfig.staminaRegenRate`.
- `MaxStaminaUpgradeEffectDefinition` increases `movementConfig.maxStamina`, defaulting to +2.
- `StaminaRefillEffectDefinition` replenishes `moveData.stamina`.
- `HealthPickupEffectDefinition` uses `context.Target.NetworkedCharacter.TryRestoreHealth(...)`.
- Timed buffs should store active state on a player-owned component, not on the pickup instance, because the pickup disappears after collection.

## Health Pickup Setup

1. Create a health effect asset through `Assets/Create/GAUNTLET/Pickups/Effects/Health Pickup`.
2. Set `Heal Amount` to the number of health pips restored.
3. Leave `Consume At Full Health` disabled if full-health players should not use the pickup.
4. Create a `Pickup Definition` asset and set `Upgrade Kind` to `Health Pickup`.
5. Assign the health effect asset to that pickup definition's `Effect` field.
6. Assign a pickup prefab or let the director use its default/fallback visual.
7. Add the health pickup definition to any `PickupSpawnLocation` pool that should be able to spawn it.

## Stamina Pickup Setup

Create one effect asset per behavior:

- Regen upgrade: `Assets/Create/GAUNTLET/Pickups/Effects/Stamina Regen Upgrade`.
- Max stamina upgrade: `Assets/Create/GAUNTLET/Pickups/Effects/Max Stamina Upgrade`.
- Refill: `Assets/Create/GAUNTLET/Pickups/Effects/Stamina Refill`.

Then create one `Pickup Definition` for each stamina pickup variant and assign the matching effect asset to its `Effect` field. Add whichever variants a location should offer to that `PickupSpawnLocation` pool.

using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GAUNTLET.Gameplay.Pickups
{
    public class PickupSpawnDirector : MonoBehaviour
    {
        public static bool AutoCreateEnabled { get; set; } = true;

        [SerializeField] private GameObject defaultPickupPrefab;
        [SerializeField] private bool autoDiscoverLocations = true;
        [SerializeField] private bool spawnImmediately;
        [SerializeField] private bool allowPrimitiveFallback = true;
        [SerializeField] private List<PickupSpawnLocation> spawnLocations = new List<PickupSpawnLocation>();

        private const float FailedSpawnRetrySeconds = 0.25f;

        private readonly List<SpawnSlot> _slots = new List<SpawnSlot>();
        private bool _initialized;
        private bool _serverWasStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadHook()
        {
            SceneManager.sceneLoaded -= SceneManager_SceneLoaded;
            SceneManager.sceneLoaded += SceneManager_SceneLoaded;
        }

        private static void SceneManager_SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureAutoCreated();
        }

        private static void EnsureAutoCreated()
        {
            if (!AutoCreateEnabled)
                return;

            if (FindFirstObjectByType<PickupSpawnDirector>() != null)
                return;

            if (FindFirstObjectByType<PickupSpawnLocation>() == null)
                return;

            GameObject go = new GameObject("PickupSpawnDirector");
            go.AddComponent<PickupSpawnDirector>();
        }

        private void Start()
        {
            InitializeSlots();
        }

        private void Update()
        {
            if (!ShouldRunSpawner())
                return;

            InitializeSlots();
            RescheduleInitialSpawnsOnServerStart();
            TickSlots();
        }

        public void RefreshLocations()
        {
            if (autoDiscoverLocations)
            {
                spawnLocations.Clear();
                spawnLocations.AddRange(FindObjectsByType<PickupSpawnLocation>(FindObjectsSortMode.None));
            }

            _slots.Clear();
            for (int i = 0; i < spawnLocations.Count; i++)
            {
                PickupSpawnLocation location = spawnLocations[i];
                if (location == null)
                    continue;

                float initialDelay = spawnImmediately ? 0f : location.InitialSpawnDelaySeconds;
                _slots.Add(new SpawnSlot(location, Time.time + initialDelay));
            }

            _initialized = true;
        }

        private void InitializeSlots()
        {
            if (_initialized)
                return;

            RefreshLocations();
        }

        private void TickSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                SpawnSlot slot = _slots[i];
                if (!slot.CanSpawn(Time.time))
                    continue;

                PickupWorldItem item = SpawnPickup(slot.Location, out bool retrySoon);
                if (item == null)
                {
                    if (retrySoon)
                        slot.ScheduleRetry(Time.time, FailedSpawnRetrySeconds);
                    else
                        slot.ScheduleNext(Time.time);

                    continue;
                }

                slot.ActivePickup = item;
                slot.MarkSpawned();
                slot.ScheduleNext(Time.time);
                item.Consumed += Pickup_Consumed;
            }
        }

        private PickupWorldItem SpawnPickup(PickupSpawnLocation location, out bool retrySoon)
        {
            retrySoon = false;

            if (location == null || !location.HasPickupPool)
                return null;

            PickupDefinition definition = location.ChoosePickupDefinition();
            if (definition == null)
                return null;

            GameObject prefab = definition.PickupPrefab != null ? definition.PickupPrefab : defaultPickupPrefab;
            GameObject instance = null;
            NetworkObject networkObject = null;
            bool prefabIsNetworked = prefab != null && prefab.TryGetComponent(out NetworkObject _);

            if (prefabIsNetworked)
            {
                if (!TryGetStartedServerManager(out ServerManager serverManager) || !CanSpawnNetworkedPickupNow(serverManager))
                {
                    retrySoon = true;
                    return null;
                }

                networkObject = serverManager.NetworkManager.GetPooledInstantiated(prefab, location.SpawnPosition, location.SpawnRotation, asServer: true);
                if (networkObject == null)
                {
                    Debug.LogWarning($"Pickup prefab '{prefab.name}' could not be instantiated by FishNet. Make sure it is registered in DefaultPrefabObjects and reserialize/refresh FishNet prefabs if Unity recently changed it.");
                    retrySoon = true;
                    return null;
                }

                instance = networkObject.gameObject;
            }
            else
            {
                instance = prefab != null
                    ? Instantiate(prefab, location.SpawnPosition, location.SpawnRotation)
                    : CreatePrimitivePickup(location.SpawnPosition, location.SpawnRotation);
            }

            if (instance == null)
                return null;

            PickupWorldItem pickup = instance.GetComponent<PickupWorldItem>();
            if (pickup == null)
                pickup = instance.AddComponent<PickupWorldItem>();

            pickup.Initialize(definition, location);

            if (networkObject == null)
                networkObject = instance.GetComponent<NetworkObject>();

            if (networkObject != null && TryGetStartedServerManager(out ServerManager spawnServerManager))
                spawnServerManager.Spawn(networkObject);

            return pickup;
        }

        private GameObject CreatePrimitivePickup(Vector3 position, Quaternion rotation)
        {
            if (!allowPrimitiveFallback)
                return null;

            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            primitive.name = "Runtime Pickup";
            primitive.transform.SetPositionAndRotation(position, rotation);
            primitive.transform.localScale = Vector3.one * 0.6f;
            return primitive;
        }

        private void Pickup_Consumed(PickupWorldItem pickup)
        {
            if (pickup != null)
                pickup.Consumed -= Pickup_Consumed;

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].ActivePickup == pickup)
                {
                    _slots[i].ActivePickup = null;
                    _slots[i].ScheduleNext(Time.time);
                    return;
                }
            }
        }

        private bool ShouldRunSpawner()
        {
            if (TryGetStartedServerManager(out _))
                return true;

            return !TryGetStartedClientManager(out _);
        }

        private static bool CanSpawnNetworkedPickupNow(ServerManager serverManager)
        {
            if (serverManager == null)
                return false;

            foreach (var serverConnection in serverManager.Clients.Values)
            {
                if (serverConnection != null && serverConnection.IsValid && !serverConnection.LoadedStartScenes(true))
                    return false;
            }

            ClientManager clientManager = InstanceFinder.ClientManager;
            if (clientManager == null || !clientManager.Started)
                return true;

            var clientConnection = clientManager.Connection;
            return clientConnection != null && clientConnection.IsValid && clientConnection.LoadedStartScenes(false);
        }

        private void RescheduleInitialSpawnsOnServerStart()
        {
            bool serverStarted = TryGetStartedServerManager(out _);
            if (_serverWasStarted == serverStarted)
                return;

            _serverWasStarted = serverStarted;
            if (!serverStarted)
                return;

            for (int i = 0; i < _slots.Count; i++)
            {
                SpawnSlot slot = _slots[i];
                if (!slot.NeedsInitialSpawnDelay)
                    continue;

                float initialDelay = spawnImmediately ? 0f : slot.Location.InitialSpawnDelaySeconds;
                slot.ScheduleInitial(Time.time, initialDelay);
            }
        }

        private static bool TryGetStartedServerManager(out ServerManager serverManager)
        {
            serverManager = InstanceFinder.ServerManager;
            return serverManager != null && serverManager.Started;
        }

        private static bool TryGetStartedClientManager(out ClientManager clientManager)
        {
            clientManager = InstanceFinder.ClientManager;
            return clientManager != null && clientManager.Started;
        }

        private sealed class SpawnSlot
        {
            public SpawnSlot(PickupSpawnLocation location, float nextSpawnTime)
            {
                Location = location;
                NextSpawnTime = nextSpawnTime;
            }

            public PickupSpawnLocation Location { get; }
            public PickupWorldItem ActivePickup { get; set; }
            public float NextSpawnTime { get; private set; }
            public bool HasSpawned { get; private set; }
            public bool NeedsInitialSpawnDelay => !HasSpawned && ActivePickup == null && Location != null;

            public bool CanSpawn(float now)
            {
                return ActivePickup == null && Location != null && now >= NextSpawnTime;
            }

            public void MarkSpawned()
            {
                HasSpawned = true;
            }

            public void ScheduleInitial(float now, float initialDelaySeconds)
            {
                NextSpawnTime = now + Mathf.Max(0f, initialDelaySeconds);
            }

            public void ScheduleRetry(float now, float retryDelaySeconds)
            {
                NextSpawnTime = now + Mathf.Max(0.05f, retryDelaySeconds);
            }

            public void ScheduleNext(float now)
            {
                float interval = Location != null ? Location.SpawnIntervalSeconds : 1f;
                NextSpawnTime = now + interval;
            }
        }
    }
}

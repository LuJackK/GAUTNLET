using System;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Object;
using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    public class PickupWorldItem : MonoBehaviour
    {
        [SerializeField] private PickupDefinition definition;
        [SerializeField] private PickupSpawnLocation spawnLocation;
        [SerializeField] private bool requireServerAuthority = true;
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private Vector3 triggerBoxSize = Vector3.one;
        [SerializeField] private Vector3 triggerBoxCenter = Vector3.zero;

        [Header("Floating")]
        [SerializeField] private float floatAmplitude = 0.25f;
        [SerializeField] private float floatFrequency = 1f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private Vector3 _basePosition;
        private bool _initializedBasePosition;
        private bool _consumed;
        private NetworkObject _networkObject;
        private BoxCollider _triggerCollider;

        public event Action<PickupWorldItem> Consumed;

        public PickupDefinition Definition => definition;
        public PickupSpawnLocation SpawnLocation => spawnLocation;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            EnsureTriggerCollider();
            CacheBasePosition();
        }

        private void OnEnable()
        {
            _consumed = false;
            CacheBasePosition();
        }

        private void OnValidate()
        {
            triggerBoxSize = GetSafeTriggerBoxSize();

            if (_triggerCollider == null)
                _triggerCollider = GetComponent<BoxCollider>();

            if (_triggerCollider != null)
                ApplyTriggerColliderSettings(_triggerCollider);
        }

        private void Update()
        {
            ApplyFloatingMotion();
        }

        public void Initialize(PickupDefinition pickupDefinition, PickupSpawnLocation sourceLocation)
        {
            Consumed = null;
            definition = pickupDefinition;
            spawnLocation = sourceLocation;
            _consumed = false;
            _networkObject = GetComponent<NetworkObject>();
            EnsureTriggerCollider();
            CacheBasePosition();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryConsume(other);
        }

        private void TryConsume(Collider other)
        {
            if (_consumed || definition == null || !CanApplyGameplaySideEffects())
                return;

            if (((1 << other.gameObject.layer) & targetLayers.value) == 0)
                return;

            if (!PickupEffectTarget.TryResolve(other, out PickupEffectTarget target))
                return;

            PickupEffectContext context = new PickupEffectContext(
                definition,
                this,
                spawnLocation,
                target,
                TryGetStartedServerManager(out _));

            bool shouldConsume = definition.TryApply(context);
            if (!shouldConsume)
                return;

            Consume();
        }

        private bool CanApplyGameplaySideEffects()
        {
            if (!requireServerAuthority)
                return true;

            if (TryGetStartedServerManager(out _))
                return true;

            return !TryGetStartedClientManager(out _);
        }

        private void Consume()
        {
            if (_consumed)
                return;

            _consumed = true;
            Consumed?.Invoke(this);

            if (_networkObject != null && TryGetStartedServerManager(out ServerManager serverManager))
                serverManager.Despawn(_networkObject, DespawnType.Destroy);
            else
                Destroy(gameObject);
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

        private void ApplyFloatingMotion()
        {
            if (!_initializedBasePosition)
                CacheBasePosition();

            float bob = Mathf.Sin(Time.time * Mathf.PI * 2f * Mathf.Max(0f, floatFrequency)) * Mathf.Max(0f, floatAmplitude);
            transform.position = _basePosition + Vector3.up * bob;

            if (!Mathf.Approximately(spinDegreesPerSecond, 0f))
                transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }

        private void CacheBasePosition()
        {
            _basePosition = transform.position;
            _initializedBasePosition = true;
        }

        private void EnsureTriggerCollider()
        {
            if (_triggerCollider == null)
                _triggerCollider = GetComponent<BoxCollider>();

            if (_triggerCollider == null)
                _triggerCollider = gameObject.AddComponent<BoxCollider>();

            ApplyTriggerColliderSettings(_triggerCollider);
        }

        private void ApplyTriggerColliderSettings(BoxCollider boxCollider)
        {
            boxCollider.isTrigger = true;
            boxCollider.center = triggerBoxCenter;
            boxCollider.size = GetSafeTriggerBoxSize();
        }

        private Vector3 GetSafeTriggerBoxSize()
        {
            return new Vector3(
                Mathf.Max(0.05f, triggerBoxSize.x),
                Mathf.Max(0.05f, triggerBoxSize.y),
                Mathf.Max(0.05f, triggerBoxSize.z));
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Networking
{
    /// <summary>
    /// Repositions spawned player objects onto validated map spawn points.
    /// This runs server-side after FishNet finishes spawning the player object.
    /// </summary>
    public class PlayerSpawnService : MonoBehaviour
    {
        public static bool AutoCreateEnabled { get; set; } = true;
        public static PlayerSpawnService Instance { get; private set; }

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private PlayerMaterialManager playerMaterialManager;
        [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(-9.6f, 5.84f, -14.8f);
        [SerializeField] private Vector3 fallbackSpawnEuler = Vector3.zero;
        [SerializeField] private bool randomizeSpawnSelection = true;
        [SerializeField] private bool logOwnershipDiagnostics = true;

        private readonly List<Transform> _spawnPoints = new List<Transform>();
        private int _nextSpawnIndex;
        private bool _callbacksRegistered;
        private bool _loggedMissingSpawnPoints;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!AutoCreateEnabled)
                return;

            if (FindFirstObjectByType<PlayerSpawnService>() != null)
                return;

            GameObject go = new GameObject("PlayerSpawnService");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerSpawnService>();
        }

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            StartCoroutine(BindNetworkManagerWhenReady());
        }

        private IEnumerator BindNetworkManagerWhenReady()
        {
            while (networkManager == null)
            {
                networkManager = InstanceFinder.NetworkManager;
                if (networkManager == null)
                    yield return null;
            }

            RegisterCallbacks();
            RefreshSpawnPoints();
        }

        private void RegisterCallbacks()
        {
            if (_callbacksRegistered || networkManager == null)
                return;

            networkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
            networkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
            networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

            _callbacksRegistered = true;
            if (logOwnershipDiagnostics)
                Debug.Log("PlayerSpawnService: Bound to NetworkManager and registered callbacks.");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (!_callbacksRegistered || networkManager == null)
                return;

            networkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
            networkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
            networkManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

            _callbacksRegistered = false;
        }

        private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
        {
            if (!args.QueueData.AsServer)
                return;

            RefreshSpawnPoints();
        }

        private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer)
                return;

            LogNetDebug(NetcodeDebugCategory.Spawn,
                        NetcodeDebugSeverity.Info,
                        null,
                        $"Client loaded start scenes on server. connId={conn.ClientId}. Beginning spawn/ownership checks.");
            LogConnectionSnapshot(conn, "OnClientLoadedStartScenes");
            LogAllNetworkedCharacterOwnership("OnClientLoadedStartScenes");

            StartCoroutine(RepositionWhenReady(conn));
        }

        private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                Debug.Log($"PlayerSpawnService: Client {conn.ClientId} disconnected.");
                ResolvePlayerMaterialManager(createIfMissing: false)?.UnregisterPlayer(conn.ClientId);
            }
        }

        private IEnumerator RepositionWhenReady(NetworkConnection conn)
        {
            const int maxFrames = 120;
            int waited = 0;

            while (conn != null && ResolveConnectionPlayerObject(conn) == null && waited < maxFrames)
            {
                waited++;
                yield return null;
            }

            if (conn == null)
            {
                Debug.LogWarning("PlayerSpawnService: Connection became null while waiting for player object.");
                yield break;
            }

            NetworkObject playerObject = ResolveConnectionPlayerObject(conn);
            if (playerObject == null)
            {
                Debug.LogWarning($"PlayerSpawnService: Could not resolve owned player object for client {conn.ClientId}. Owned objects={conn.Objects.Count}.");
                LogNetDebug(NetcodeDebugCategory.Spawn,
                            NetcodeDebugSeverity.Warning,
                            null,
                            $"Could not resolve player object for connId={conn.ClientId} after waiting {waited} frames.",
                            NetcodeDebugSuspectFlags.SpawnOwnershipRace);
                LogConnectionSnapshot(conn, "ResolveFailed");
                yield break;
            }

            // Safe ownership repair: only for remote player objects (not host's own object).
            // IMPORTANT: Skip repair if this is the host's connection and it already owns the object.
            // This prevents ownership churn on the host's client object.
            bool isHostOwnObject = conn.IsHost && playerObject.Owner == conn;
            if (playerObject == conn.FirstObject && playerObject.Owner != conn && !isHostOwnObject)
            {
                int priorOwner = playerObject.Owner != null && playerObject.Owner.IsValid ? playerObject.Owner.ClientId : -1;
                Debug.LogWarning($"PlayerSpawnService: FirstObject ownership mismatch for conn={conn.ClientId} (IsHost={conn.IsHost}). objectId={playerObject.ObjectId}, priorOwner={priorOwner}. Reassigning ownership.");
                LogNetDebug(NetcodeDebugCategory.Ownership,
                            NetcodeDebugSeverity.Warning,
                            playerObject.GetComponent<NetworkedCharacter>(),
                            $"Ownership mismatch repaired for connId={conn.ClientId}. priorOwner={priorOwner}.",
                            NetcodeDebugSuspectFlags.SpawnOwnershipRace);
                playerObject.GiveOwnership(conn);
            }

            if (logOwnershipDiagnostics)
            {
                int ownerId = playerObject.Owner != null && playerObject.Owner.IsValid ? playerObject.Owner.ClientId : -1;
                Debug.Log($"PlayerSpawnService: Resolved player object for conn={conn.ClientId}. objectId={playerObject.ObjectId}, ownerId={ownerId}, firstObjectMatch={(conn.FirstObject == playerObject)}");
            }

            Transform playerTransform = playerObject.transform;
            GetSpawnPose(out Vector3 position, out Quaternion rotation);
            NetworkedCharacter networkedCharacter = playerObject.GetComponent<NetworkedCharacter>();
            if (networkedCharacter != null)
            {
                AssignLobbyMaterial(conn, networkedCharacter);
                networkedCharacter.ApplyAuthoritativeSpawnPoseServer(position, rotation);
            }
            else
            {
                playerTransform.SetPositionAndRotation(position, rotation);
            }

            Debug.Log($"PlayerSpawnService: Spawned client {conn.ClientId} at {position}.");
            LogNetDebug(NetcodeDebugCategory.Spawn,
                        NetcodeDebugSeverity.Info,
                        networkedCharacter,
                        $"Spawn pose finalized for connId={conn.ClientId} at {position}. waitedFrames={waited}.");
            LogConnectionSnapshot(conn, "PostReposition");
            LogAllNetworkedCharacterOwnership($"PostReposition conn={conn.ClientId}");
        }

        private NetworkObject ResolveConnectionPlayerObject(NetworkConnection conn)
        {
            if (conn == null)
                return null;

            // FishNet assigns FirstObject as the player's primary object.
            // Trust this first when it points to a player object.
            if (conn.FirstObject != null &&
                conn.FirstObject.GetComponent<NetworkedCharacter>() != null)
            {
                return conn.FirstObject;
            }

            foreach (NetworkObject obj in conn.Objects)
            {
                if (obj == null)
                    continue;

                if (obj.Owner != conn)
                    continue;

                if (obj.GetComponent<NetworkedCharacter>() != null)
                    return obj;
            }

            return null;
        }

        private void RefreshSpawnPoints()
        {
            _spawnPoints.Clear();

            SpawnPoint[] points = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
            foreach (SpawnPoint point in points)
            {
                if (point != null && point.isActiveAndEnabled)
                    _spawnPoints.Add(point.transform);
            }

            if (_spawnPoints.Count == 0)
            {
                if (!_loggedMissingSpawnPoints)
                {
                    Debug.LogWarning($"PlayerSpawnService: Spawn points discovered: 0. Falling back to configured position {fallbackSpawnPosition} / euler {fallbackSpawnEuler}. Ensure active SpawnPoint components exist in loaded server scenes.");
                    _loggedMissingSpawnPoints = true;
                }
                else if (logOwnershipDiagnostics)
                {
                    Debug.Log($"PlayerSpawnService: Spawn points still missing (0). Using fallback spawn pose.");
                }
            }
            else
            {
                _loggedMissingSpawnPoints = false;
                if (logOwnershipDiagnostics)
                    Debug.Log($"PlayerSpawnService: Spawn points discovered: {_spawnPoints.Count}.");
            }
        }

        private void GetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (_spawnPoints.Count == 0)
            {
                position = fallbackSpawnPosition;
                rotation = Quaternion.Euler(fallbackSpawnEuler);
                return;
            }

            int index;
            if (randomizeSpawnSelection)
            {
                index = Random.Range(0, _spawnPoints.Count);
            }
            else
            {
                index = _nextSpawnIndex;
                _nextSpawnIndex = (_nextSpawnIndex + 1) % _spawnPoints.Count;
            }

            Transform spawn = _spawnPoints[index];
            position = spawn.position;
            rotation = spawn.rotation;
        }

        public static bool TryGetInstance(out PlayerSpawnService spawnService)
        {
            spawnService = Instance;
            if (spawnService != null)
                return true;

            spawnService = FindFirstObjectByType<PlayerSpawnService>();
            if (spawnService != null)
                Instance = spawnService;

            return spawnService != null;
        }

        public bool TryRespawnPlayer(NetworkedCharacter networkedCharacter)
        {
            if (networkedCharacter == null || !networkedCharacter.IsServerInitialized)
                return false;

            RefreshSpawnPoints();
            GetSpawnPose(out Vector3 position, out Quaternion rotation);
            networkedCharacter.ApplyAuthoritativeSpawnPoseServer(position, rotation);
            return true;
        }

        private void AssignLobbyMaterial(NetworkConnection conn, NetworkedCharacter networkedCharacter)
        {
            if (conn == null || networkedCharacter == null || !networkedCharacter.IsServerInitialized)
                return;

            PlayerMaterialManager manager = ResolvePlayerMaterialManager(createIfMissing: true);
            if (manager == null)
                return;

            int joinIndex = manager.AssignMaterialForConnection(networkedCharacter, conn.ClientId);
            if (logOwnershipDiagnostics)
                Debug.Log($"PlayerSpawnService: Assigned material join index {joinIndex} to client {conn.ClientId}.", networkedCharacter);
        }

        private PlayerMaterialManager ResolvePlayerMaterialManager(bool createIfMissing)
        {
            if (playerMaterialManager != null)
                return playerMaterialManager;

            playerMaterialManager = PlayerMaterialManager.Instance;
            if (playerMaterialManager != null)
                return playerMaterialManager;

            playerMaterialManager = FindFirstObjectByType<PlayerMaterialManager>();
            if (playerMaterialManager != null)
                return playerMaterialManager;

            if (!createIfMissing)
                return null;

            playerMaterialManager = PlayerMaterialManager.GetOrCreate();
            return playerMaterialManager;
        }

        private void LogConnectionSnapshot(NetworkConnection conn, string phase)
        {
            if (!logOwnershipDiagnostics || conn == null)
                return;

            int firstObjectId = conn.FirstObject != null ? conn.FirstObject.ObjectId : -1;
            int firstOwnerId = (conn.FirstObject != null && conn.FirstObject.Owner != null && conn.FirstObject.Owner.IsValid)
                ? conn.FirstObject.Owner.ClientId
                : -1;

            Debug.Log($"PlayerSpawnService: [{phase}] connId={conn.ClientId}, firstObjectId={firstObjectId}, firstOwnerId={firstOwnerId}, ownedCount={conn.Objects.Count}");

            int inspected = 0;
            foreach (NetworkObject obj in conn.Objects)
            {
                if (obj == null)
                    continue;

                int ownerId = obj.Owner != null && obj.Owner.IsValid ? obj.Owner.ClientId : -1;
                bool hasNetworkedCharacter = obj.GetComponent<NetworkedCharacter>() != null;
                Debug.Log($"PlayerSpawnService:   [{phase}] connId={conn.ClientId} ownsList objectId={obj.ObjectId}, ownerId={ownerId}, hasNetworkedCharacter={hasNetworkedCharacter}, name={obj.name}");

                inspected++;
                if (inspected >= 8)
                    break;
            }
        }

        private void LogAllNetworkedCharacterOwnership(string phase)
        {
            if (!logOwnershipDiagnostics)
                return;

            NetworkedCharacter[] chars = FindObjectsByType<NetworkedCharacter>(FindObjectsSortMode.None);
            Debug.Log($"PlayerSpawnService: [{phase}] NetworkedCharacter count={chars.Length}");

            for (int i = 0; i < chars.Length; i++)
            {
                NetworkedCharacter nc = chars[i];
                if (nc == null)
                    continue;

                NetworkObject nob = nc.GetComponent<NetworkObject>();
                LocalInputCollector collector = nc.GetComponent<LocalInputCollector>();
                PlayerAiming aiming = nc.GetComponentInChildren<PlayerAiming>(true);

                int objectId = nob != null ? nob.ObjectId : -1;
                int ownerId = (nob != null && nob.Owner != null && nob.Owner.IsValid) ? nob.Owner.ClientId : -1;
                int firstObjectOwnerId = -1;
                bool ownerMatchesFirstObject = false;

                if (nob != null && nob.Owner != null && nob.Owner.IsValid)
                {
                    NetworkConnection ownerConn = nob.Owner;
                    firstObjectOwnerId = (ownerConn.FirstObject != null && ownerConn.FirstObject.Owner != null && ownerConn.FirstObject.Owner.IsValid)
                        ? ownerConn.FirstObject.Owner.ClientId
                        : -1;
                    ownerMatchesFirstObject = ownerConn.FirstObject == nob;
                }

                Debug.Log(
                    $"PlayerSpawnService:   [{phase}] objectId={objectId}, ownerId={ownerId}, ownerMatchesFirstObject={ownerMatchesFirstObject}, firstObjectOwnerId={firstObjectOwnerId}, isServerInit={nc.IsServerInitialized}, isClientInit={nc.IsClientInitialized}, isOwnerLocal={nc.IsOwner}, inputCollectorEnabled={(collector != null && collector.enabled)}, aimingEnabled={(aiming != null && aiming.enabled)}, name={nc.name}");
            }
        }

        private static void LogNetDebug(NetcodeDebugCategory category,
                                        NetcodeDebugSeverity severity,
                                        NetworkedCharacter networkedCharacter,
                                        string message,
                                        NetcodeDebugSuspectFlags suspects = NetcodeDebugSuspectFlags.None)
        {
            int objectId = -1;
            int ownerId = -1;
            int localClientId = -1;
            bool isServerInitialized = false;
            bool isClientInitialized = false;
            bool isOwner = false;
            bool hasLocalAuthority = false;
            string moveType = "Unknown";

            if (networkedCharacter != null)
            {
                NetworkObject networkObject = networkedCharacter.GetComponent<NetworkObject>();
                objectId = networkObject != null ? networkObject.ObjectId : -1;
                ownerId = (networkObject != null && networkObject.Owner != null && networkObject.Owner.IsValid) ? networkObject.Owner.ClientId : -1;
                localClientId = (networkedCharacter.LocalConnection != null && networkedCharacter.LocalConnection.IsValid) ? networkedCharacter.LocalConnection.ClientId : -1;
                isServerInitialized = networkedCharacter.IsServerInitialized;
                isClientInitialized = networkedCharacter.IsClientInitialized;
                isOwner = networkedCharacter.IsOwner;
                hasLocalAuthority = networkedCharacter.DebugHasLocalAuthority;
                moveType = (networkedCharacter.GetComponent<SurfCharacter>() != null && networkedCharacter.GetComponent<SurfCharacter>().moveData != null)
                    ? networkedCharacter.GetComponent<SurfCharacter>().moveData.moveType.ToString()
                    : "Unknown";
            }

            NetcodeDebugEngine.Log(category,
                                   severity,
                                   new NetcodeDebugContext
                                   {
                                       ObjectId = objectId,
                                       OwnerId = ownerId,
                                       LocalClientId = localClientId,
                                       Tick = -1,
                                       Frame = -1,
                                       IsServerInitialized = isServerInitialized,
                                       IsClientInitialized = isClientInitialized,
                                       IsOwner = isOwner,
                                       HasLocalAuthority = hasLocalAuthority,
                                       MoveType = moveType
                                   },
                                   message,
                                   suspects);
        }
    }
}

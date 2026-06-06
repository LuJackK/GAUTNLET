using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fragsurf.Movement {

    [System.Serializable]
    public class PlayerMaterialSet {
        public string label;
        public Material helmet;
        public Material body;
        public Material gauntlet;
        [FormerlySerializedAs("rocket")]
        public Material rocketBody;
        public Material rocketSlot0;
        public Material rocketSlot1;
        public Material rocketSlot2;
        public Material rocketSlot3;
    }

    /// <summary>
    /// Tracks player lobby join order and resolves that order to player material sets.
    /// </summary>
    public class PlayerMaterialManager : MonoBehaviour {

        public static PlayerMaterialManager Instance { get; private set; }

        [SerializeField] private PlayerMaterialSet[] playerMaterialSets;
        [SerializeField] private bool dontDestroyOnLoad = true;
        [SerializeField] private bool releaseJoinSlotOnDisconnect;
        [SerializeField] private bool logAssignments = true;

        private readonly Dictionary<int, int> _joinIndexByClientId = new Dictionary<int, int>();
        private int _nextJoinIndex;

        public int MaterialSetCount => playerMaterialSets != null ? playerMaterialSets.Length : 0;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
        }

        public int RegisterPlayer(int clientId) {
            if (_joinIndexByClientId.TryGetValue(clientId, out int existingJoinIndex))
                return existingJoinIndex;

            int joinIndex = _nextJoinIndex++;
            _joinIndexByClientId.Add(clientId, joinIndex);

            if (logAssignments)
                Debug.Log($"[PlayerMaterialManager] Registered client {clientId} as lobby join index {joinIndex}.", this);

            return joinIndex;
        }

        public void UnregisterPlayer(int clientId) {
            if (!releaseJoinSlotOnDisconnect)
                return;

            _joinIndexByClientId.Remove(clientId);
        }

        public bool TryGetMaterialSetForJoinIndex(int joinIndex, out PlayerMaterialSet materialSet) {
            materialSet = null;
            if (joinIndex < 0 || playerMaterialSets == null || playerMaterialSets.Length == 0)
                return false;

            int materialIndex = joinIndex % playerMaterialSets.Length;
            materialSet = playerMaterialSets[materialIndex];
            return materialSet != null;
        }

        public int AssignMaterialForConnection(NetworkedCharacter character, int clientId) {
            if (character == null)
                return -1;

            int joinIndex = RegisterPlayer(clientId);
            character.SetPlayerMaterialJoinIndexServer(joinIndex);
            return joinIndex;
        }

        public static PlayerMaterialManager GetOrCreate() {
            if (Instance != null)
                return Instance;

            Instance = FindFirstObjectByType<PlayerMaterialManager>();
            if (Instance != null)
                return Instance;

            GameObject go = new GameObject("PlayerMaterialManager");
            Instance = go.AddComponent<PlayerMaterialManager>();
            DontDestroyOnLoad(go);
            return Instance;
        }
    }
}

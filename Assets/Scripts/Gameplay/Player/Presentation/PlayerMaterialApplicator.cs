using UnityEngine;

namespace Fragsurf.Movement {

    /// <summary>
    /// Applies the synced lobby join index to this player's visible model parts.
    /// </summary>
    public class PlayerMaterialApplicator : MonoBehaviour {

        [System.Serializable]
        private class RendererMaterialGroup {
            public Renderer[] renderers;
            public int materialSlot;
            public bool replaceAllSlots;

            public bool HasRenderers => renderers != null && renderers.Length > 0;
        }

        [Header("Renderer Groups")]
        [SerializeField] private RendererMaterialGroup helmet = new RendererMaterialGroup();
        [SerializeField] private RendererMaterialGroup body = new RendererMaterialGroup();
        [SerializeField] private RendererMaterialGroup gauntlet = new RendererMaterialGroup();
        [SerializeField] private RendererMaterialGroup rocketBody = new RendererMaterialGroup();
        [SerializeField] private RendererMaterialGroup rocketSlots = new RendererMaterialGroup();

        [Header("Fallback")]
        [SerializeField] private PlayerMaterialSet[] fallbackMaterialSets;

        [Header("Auto Binding")]
        [SerializeField] private bool autoFindNamedRenderers = true;
        [SerializeField] private bool includeInactiveRenderers = true;

        private int _joinIndex = -1;

        private void Awake() {
            EnsureRendererGroups();
        }

        private void OnEnable() {
            ApplyMaterialSet();
        }

        public void SetLobbyJoinIndex(int joinIndex) {
            _joinIndex = joinIndex;
            ApplyMaterialSet();
        }

        private void EnsureRendererGroups() {
            if (!autoFindNamedRenderers)
                return;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
            if (!helmet.HasRenderers)
                helmet.renderers = FindRenderersByName(renderers, "PilotHemet", "Helmet");

            if (!body.HasRenderers)
                body.renderers = FindRenderersByName(renderers, "Panda", "Body");

            if (!gauntlet.HasRenderers)
                gauntlet.renderers = FindRenderersByName(renderers, "N000_10", "BLUEglove", "Gauntlet");

            if (!rocketBody.HasRenderers)
                rocketBody.renderers = FindRenderersByExactName(renderers, "Rocket");

            if (!rocketSlots.HasRenderers)
                rocketSlots.renderers = FindRenderersByExactName(renderers, "0004_rocket");
        }

        private void ApplyMaterialSet() {
            if (_joinIndex < 0)
                return;

            EnsureRendererGroups();
            if (!TryResolveMaterialSet(_joinIndex, out PlayerMaterialSet materialSet))
                return;

            ApplyMaterial(helmet, materialSet.helmet);
            ApplyMaterial(body, materialSet.body);
            ApplyMaterial(gauntlet, materialSet.gauntlet);
            ApplyMaterial(rocketBody, materialSet.rocketBody);
            ApplyMaterialToSlot(rocketSlots, 0, materialSet.rocketSlot0);
            ApplyMaterialToSlot(rocketSlots, 1, materialSet.rocketSlot1);
            ApplyMaterialToSlot(rocketSlots, 2, materialSet.rocketSlot2);
            ApplyMaterialToSlot(rocketSlots, 3, materialSet.rocketSlot3);
        }

        private bool TryResolveMaterialSet(int joinIndex, out PlayerMaterialSet materialSet) {
            if (PlayerMaterialManager.Instance != null &&
                PlayerMaterialManager.Instance.TryGetMaterialSetForJoinIndex(joinIndex, out materialSet)) {
                return true;
            }

            materialSet = null;
            if (fallbackMaterialSets == null || fallbackMaterialSets.Length == 0)
                return false;

            int materialIndex = joinIndex % fallbackMaterialSets.Length;
            materialSet = fallbackMaterialSets[materialIndex];
            return materialSet != null;
        }

        private static void ApplyMaterial(RendererMaterialGroup group, Material material) {
            if (group == null || material == null || group.renderers == null)
                return;

            for (int i = 0; i < group.renderers.Length; i++) {
                Renderer targetRenderer = group.renderers[i];
                if (targetRenderer == null)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                if (group.replaceAllSlots) {
                    for (int slot = 0; slot < materials.Length; slot++)
                        materials[slot] = material;
                } else if (group.materialSlot >= 0 && group.materialSlot < materials.Length) {
                    materials[group.materialSlot] = material;
                }

                targetRenderer.sharedMaterials = materials;
            }
        }

        private static void ApplyMaterialToSlot(RendererMaterialGroup group, int materialSlot, Material material) {
            if (group == null || material == null || group.renderers == null)
                return;

            for (int i = 0; i < group.renderers.Length; i++) {
                Renderer targetRenderer = group.renderers[i];
                if (targetRenderer == null)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                if (materials == null || materialSlot < 0 || materialSlot >= materials.Length)
                    continue;

                materials[materialSlot] = material;
                targetRenderer.sharedMaterials = materials;
            }
        }

        private static Renderer[] FindRenderersByName(Renderer[] renderers, params string[] nameMatches) {
            if (renderers == null || nameMatches == null || nameMatches.Length == 0)
                return System.Array.Empty<Renderer>();

            System.Collections.Generic.List<Renderer> matches = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                string rendererName = renderer.name;
                for (int nameIndex = 0; nameIndex < nameMatches.Length; nameIndex++) {
                    string nameMatch = nameMatches[nameIndex];
                    if (!string.IsNullOrEmpty(nameMatch) && rendererName.Contains(nameMatch)) {
                        matches.Add(renderer);
                        break;
                    }
                }
            }

            return matches.ToArray();
        }

        private static Renderer[] FindRenderersByExactName(Renderer[] renderers, params string[] exactNames) {
            if (renderers == null || exactNames == null || exactNames.Length == 0)
                return System.Array.Empty<Renderer>();

            System.Collections.Generic.List<Renderer> matches = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                for (int nameIndex = 0; nameIndex < exactNames.Length; nameIndex++) {
                    if (renderer.name == exactNames[nameIndex]) {
                        matches.Add(renderer);
                        break;
                    }
                }
            }

            return matches.ToArray();
        }
    }
}

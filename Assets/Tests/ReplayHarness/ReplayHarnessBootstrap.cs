using GAUNTLET.Networking;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    internal static class ReplayHarnessBootstrap {

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DisablePlayerSpawnServiceAutoCreate() {
            PlayerSpawnService.AutoCreateEnabled = false;
        }
    }
}

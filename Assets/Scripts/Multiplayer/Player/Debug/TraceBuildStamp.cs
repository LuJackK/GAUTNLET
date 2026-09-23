using System;
using System.IO;
using UnityEngine;

namespace Fragsurf.Movement {
    [Serializable] public sealed class TraceBuildStamp {
        public string revision = "unknown", artifactHash = "unknown", contentHash = "unknown";
        public bool dirty = true;
        public static TraceBuildStamp Load() {
            if (Application.isEditor) return new TraceBuildStamp();
            try {
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "netcode-build.json");
                if (new FileInfo(path).Length > 65536) return new TraceBuildStamp();
                return JsonUtility.FromJson<TraceBuildStamp>(File.ReadAllText(path)) ?? new TraceBuildStamp();
            } catch (Exception) { return new TraceBuildStamp(); }
        }
    }
}

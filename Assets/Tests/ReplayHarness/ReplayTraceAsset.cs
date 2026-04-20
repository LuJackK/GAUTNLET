using System;
using System.Collections.Generic;
using Fragsurf.Movement;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    [Serializable]
    public struct ReplayTraceFrame {
        public int frame;
        public byte buttons;
        public sbyte stickX;
        public sbyte stickY;
        public byte justPressed;
        public short lookYaw100;
        public short lookPitch100;

        public InputFrame ToInputFrame(int characterObjectId) {
            return new InputFrame {
                frame = frame,
                characterObjectId = characterObjectId,
                buttons = buttons,
                stickX = stickX,
                stickY = stickY,
                justPressed = justPressed,
                lookYaw100 = lookYaw100,
                lookPitch100 = lookPitch100
            };
        }

        public static ReplayTraceFrame FromInputFrame(InputFrame input) {
            return new ReplayTraceFrame {
                frame = input.frame,
                buttons = input.buttons,
                stickX = input.stickX,
                stickY = input.stickY,
                justPressed = input.justPressed,
                lookYaw100 = input.lookYaw100,
                lookPitch100 = input.lookPitch100
            };
        }
    }

    [Serializable]
    public sealed class ReplayTraceDefinition {
        public string traceId = "unnamed-trace";
        public int characterObjectId = 1;
        public float tickDelta = 1f / 60f;
        public Vector3 initialPosition = new Vector3(0f, 2f, 0f);
        public float initialYaw;
        public float initialPitch;
        public bool excludeCrouch = true;
        public bool excludeLadders = true;
        public bool excludeUnderwater = true;
        public List<ReplayTraceFrame> frames = new List<ReplayTraceFrame>();
    }

    [CreateAssetMenu(fileName = "ReplayTrace", menuName = "Replay Harness/Trace Asset")]
    public sealed class ReplayTraceAsset : ScriptableObject {
        public ReplayTraceDefinition trace = new ReplayTraceDefinition();
    }
}

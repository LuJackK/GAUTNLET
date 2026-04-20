using System.Collections;
using Fragsurf.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Fragsurf.ReplayHarness {

    public class ReplayHarnessPlayModeTests {
        private const string RealPlayerPrefabPath = "Assets/Prefabs/Player 1.prefab";

        [UnityTest]
        public IEnumerator SameTrace_ReplaysIdentically() {
            ReplayTraceDefinition trace = BuildExerciseTrace();
            ReplayComparisonSettings settings = new ReplayComparisonSettings();

            ReplayHarnessEnvironment firstEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero);
            yield return WaitUntilReady(firstEnvironment);
            ReplayRunResult reference = ReplayHarnessRunner.RunLocalTrace(firstEnvironment, trace, ReplayRunKind.LocalReference);
            firstEnvironment.Dispose();
            yield return null;

            ReplayHarnessEnvironment secondEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero);
            yield return WaitUntilReady(secondEnvironment);
            ReplayRunResult repeat = ReplayHarnessRunner.RunLocalTrace(secondEnvironment, trace, ReplayRunKind.LocalRepeat);
            secondEnvironment.Dispose();
            yield return null;

            ReplayComparisonReport report = ReplayComparisonReport.Compare(reference, repeat, settings, "Local replay repeatability");
            Assert.IsTrue(report.passed, report.summary);
        }

        [UnityTest]
        public IEnumerator SameTrace_ReplaysIdentically_WithRealPlayerPrefabBootstrap() {
            ReplayTraceDefinition trace = BuildExerciseTrace();
            ReplayComparisonSettings settings = new ReplayComparisonSettings();
            ReplayHarnessEnvironmentOptions options = new ReplayHarnessEnvironmentOptions {
                bootstrapMode = ReplayHarnessBootstrapMode.RealPlayerPrefab,
                playerPrefab = LoadRealPlayerPrefab()
            };

            ReplayHarnessEnvironment firstEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero, options);
            yield return WaitUntilReady(firstEnvironment);
            ReplayRunResult reference = ReplayHarnessRunner.RunLocalTrace(firstEnvironment, trace, ReplayRunKind.LocalReference);
            firstEnvironment.Dispose();
            yield return null;

            ReplayHarnessEnvironment secondEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero, options);
            yield return WaitUntilReady(secondEnvironment);
            ReplayRunResult repeat = ReplayHarnessRunner.RunLocalTrace(secondEnvironment, trace, ReplayRunKind.LocalRepeat);
            secondEnvironment.Dispose();
            yield return null;

            ReplayComparisonReport report = ReplayComparisonReport.Compare(reference,
                                                                           repeat,
                                                                           settings,
                                                                           "Local replay repeatability (real player prefab)");
            Assert.IsTrue(report.passed, report.summary);
        }

        [UnityTest]
        public IEnumerator PredictedRun_ConvergesWithDelayedAuthoritativeCorrections() {
            ReplayTraceDefinition trace = BuildExerciseTrace();
            ReplayComparisonSettings settings = new ReplayComparisonSettings {
                authoritativeDelayFrames = 6
            };

            ReplayHarnessEnvironment authoritativeEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero);
            yield return WaitUntilReady(authoritativeEnvironment);
            ReplayRunResult authoritative = ReplayHarnessRunner.RunLocalTrace(authoritativeEnvironment, trace, ReplayRunKind.AuthoritativeReference);
            authoritativeEnvironment.Dispose();
            yield return null;

            ReplayHarnessEnvironment predictedEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero);
            yield return WaitUntilReady(predictedEnvironment);
            ReplayRunResult predicted = ReplayHarnessRunner.RunPredictedWithCorrections(predictedEnvironment, trace, authoritative, settings);
            predictedEnvironment.Dispose();
            yield return null;

            ReplayComparisonReport report = ReplayComparisonReport.Compare(authoritative, predicted, settings, "Predicted vs authoritative");
            Assert.IsTrue(report.passed, report.summary);
            Assert.Zero(predicted.predictedFillCount, "Predicted replay should not need to synthesize missing state history.");
        }

        [UnityTest]
        public IEnumerator PredictedRun_HasNoTransientChecksumDivergence() {
            ReplayTraceDefinition trace = BuildExerciseTrace();
            ReplayComparisonSettings settings = new ReplayComparisonSettings {
                authoritativeDelayFrames = 6
            };

            ReplayHarnessEnvironment authoritativeEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero);
            yield return WaitUntilReady(authoritativeEnvironment);
            ReplayRunResult authoritative = ReplayHarnessRunner.RunLocalTrace(authoritativeEnvironment, trace, ReplayRunKind.AuthoritativeReference);
            authoritativeEnvironment.Dispose();
            yield return null;

            ReplayHarnessEnvironment predictedEnvironment = ReplayHarnessEnvironment.Create(trace, Vector3.zero);
            yield return WaitUntilReady(predictedEnvironment);
            ReplayRunResult predicted = ReplayHarnessRunner.RunPredictedWithCorrections(predictedEnvironment, trace, authoritative, settings);
            predictedEnvironment.Dispose();
            yield return null;

            Assert.Zero(predicted.checksumMismatchCount,
                        $"Predicted timeline diverged before authoritative reconciliation. firstMismatchFrame={predicted.firstChecksumMismatchFrame}, lastMismatchFrame={predicted.lastChecksumMismatchFrame}, mismatchCount={predicted.checksumMismatchCount}.");
        }

        private static IEnumerator WaitUntilReady(ReplayHarnessEnvironment environment) {
            int safetyFrames = 10;
            while (environment != null && !environment.IsReady && safetyFrames-- > 0)
                yield return null;

            Assert.IsNotNull(environment);
            Assert.IsTrue(environment.IsReady, "Replay harness environment did not finish initializing.");
        }

        private static ReplayTraceDefinition BuildExerciseTrace() {
            ReplayTraceDefinition trace = new ReplayTraceDefinition {
                traceId = "dash-doublejump-heavymelee-smoke",
                tickDelta = 1f / 60f,
                initialPosition = new Vector3(0f, 2f, 0f)
            };

            byte previousButtons = 0;
            int frame = 0;

            void AddFrame(byte buttons, sbyte stickX, sbyte stickY, float yaw = 0f, float pitch = 0f) {
                byte justPressed = (byte)(buttons & ~previousButtons);
                previousButtons = buttons;

                InputFrame input = InputFrame.Create(frame,
                                                     trace.characterObjectId,
                                                     buttons,
                                                     stickX,
                                                     stickY,
                                                     justPressed,
                                                     yaw,
                                                     pitch);
                trace.frames.Add(ReplayTraceFrame.FromInputFrame(input));
                frame++;
            }

            for (int i = 0; i < 15; i++)
                AddFrame(0, 0, 127);

            AddFrame(InputFrame.BTN_JUMP, 0, 127);

            for (int i = 0; i < 12; i++)
                AddFrame(0, 0, 127);

            AddFrame(InputFrame.BTN_DASH, 0, 127);

            for (int i = 0; i < 14; i++)
                AddFrame(0, 0, 127);

            AddFrame(InputFrame.BTN_JUMP, 0, 127);

            for (int i = 0; i < 12; i++)
                AddFrame(0, 0, 127);

            for (int i = 0; i < 18; i++)
                AddFrame(InputFrame.BTN_MELEE, 0, 127);

            for (int i = 0; i < 20; i++)
                AddFrame(0, 0, 127);

            return trace;
        }

        private static GameObject LoadRealPlayerPrefab() {
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RealPlayerPrefabPath);
            Assert.IsNotNull(prefab, $"Replay harness could not load real player prefab at '{RealPlayerPrefabPath}'.");
            return prefab;
#else
            Assert.Fail("Real player prefab bootstrap is only supported in the Unity editor test runner.");
            return null;
#endif
        }
    }
}

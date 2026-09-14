using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FacilityViewer.Tests
{
    public sealed class MaterialOwnershipProfilingEvidenceTests
    {
        private const string ManifestPath = "Packages/manifest.json";
        private const string LockPath = "Packages/packages-lock.json";
        private const string EvidencePath = "Assets/Tests/MaterialOwnershipProfilingEvidence.json";
        private const string HarnessTypeName =
            "FacilityViewer.Editor.MaterialOwnershipProfilingCapture, Assembly-CSharp-Editor";

        [Test]
        public void MemoryProfilerPackageIsPinnedAndResolved()
        {
            string manifest = File.ReadAllText(ManifestPath);
            string lockFile = File.ReadAllText(LockPath);

            Assert.That(manifest, Does.Contain("\"com.unity.memoryprofiler\": \"1.1.9\""));
            AssertLockEntry(
                lockFile,
                "com.unity.memoryprofiler",
                "1.1.9",
                0,
                new Dictionary<string, string>
                {
                    { "com.unity.burst", "1.8.0" },
                    { "com.unity.collections", "1.2.3" },
                    { "com.unity.mathematics", "1.2.1" },
                    { "com.unity.profiling.core", "1.0.0" },
                    { "com.unity.editorcoroutines", "1.0.0" }
                });
            AssertLockEntry(lockFile, "com.unity.editorcoroutines", "1.1.0", 1,
                new Dictionary<string, string>());
            AssertLockEntry(lockFile, "com.unity.profiling.core", "1.0.3", 1,
                new Dictionary<string, string>());

            PackageInfo packageInfo = PackageInfo.FindForPackageName("com.unity.memoryprofiler");
            Assert.That(packageInfo, Is.Not.Null);
            Assert.That(packageInfo.version, Is.EqualTo("1.1.9"));
            Assert.That(packageInfo.source.ToString(), Is.EqualTo("Registry"));
        }

        [Test]
        public void SourceEvidenceUsesTheCaptureSchemaAndExpectedOwnershipCounts()
        {
            SourceEvidence evidence = JsonUtility.FromJson<SourceEvidence>(File.ReadAllText(EvidencePath));

            Assert.That(evidence.schemaVersion, Is.EqualTo(1));
            Assert.That(evidence.scenario, Is.EqualTo("lobby-material-ownership-comparison"));
            Assert.That(evidence.unityVersion, Is.EqualTo("6000.5.9f1"));
            Assert.That(evidence.rendererCount, Is.EqualTo(3));
            Assert.That(evidence.savedSharedMaterialReferenceCount, Is.EqualTo(2));
            Assert.That(evidence.propertyBlockOverrideRendererCount, Is.EqualTo(1));
            Assert.That(evidence.ownedRuntimeMaterialReferenceCount, Is.EqualTo(1));
            Assert.That(evidence.distinctComparisonMaterialObjectCount, Is.EqualTo(2));
            Assert.That(evidence.frameDebuggerStatus, Is.EqualTo("manual-native-window-gate"));
            Assert.That(evidence.runtimeCountersStatus, Is.EqualTo("local-capture-only"));
            Assert.That(evidence.memorySnapshotStatus, Is.EqualTo("local-temp-snapshot-only"));
        }

        [Test]
        public void EditorOnlyCaptureContractMatchesTheSourceEvidence()
        {
            Type harnessType = Type.GetType(HarnessTypeName);
            Assert.That(harnessType, Is.Not.Null);
            Assert.That(harnessType.Assembly.GetName().Name, Is.EqualTo("Assembly-CSharp-Editor"));

            string generatedJson = (string)harnessType.GetMethod(
                    "CreateSourceEvidenceJson",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
            SourceEvidence generated = JsonUtility.FromJson<SourceEvidence>(generatedJson);
            SourceEvidence stored = JsonUtility.FromJson<SourceEvidence>(File.ReadAllText(EvidencePath));

            Assert.That(generated.schemaVersion, Is.EqualTo(stored.schemaVersion));
            Assert.That(generated.scenario, Is.EqualTo(stored.scenario));
            Assert.That(generated.rendererCount, Is.EqualTo(stored.rendererCount));
            Assert.That(generated.savedSharedMaterialReferenceCount,
                Is.EqualTo(stored.savedSharedMaterialReferenceCount));
            Assert.That(generated.propertyBlockOverrideRendererCount,
                Is.EqualTo(stored.propertyBlockOverrideRendererCount));
            Assert.That(generated.ownedRuntimeMaterialReferenceCount,
                Is.EqualTo(stored.ownedRuntimeMaterialReferenceCount));
            Assert.That(generated.distinctComparisonMaterialObjectCount,
                Is.EqualTo(stored.distinctComparisonMaterialObjectCount));
            Assert.That(generated.frameDebuggerStatus, Is.EqualTo(stored.frameDebuggerStatus));
            Assert.That(generated.runtimeCountersStatus, Is.EqualTo(stored.runtimeCountersStatus));
            Assert.That(generated.memorySnapshotStatus, Is.EqualTo(stored.memorySnapshotStatus));
        }

        [Test]
        public void SceneSetupSerializationPreservesLoadedAndActiveScenes()
        {
            Type harnessType = Type.GetType(HarnessTypeName);
            MethodInfo serialize = harnessType.GetMethod(
                "SerializeSceneSetupForTesting",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo deserialize = harnessType.GetMethod(
                "DeserializeSceneSetupForTesting",
                BindingFlags.Public | BindingFlags.Static);
            SceneSetup[] original =
            {
                new SceneSetup
                {
                    path = "Assets/Scenes/Bootstrap.unity",
                    isLoaded = true,
                    isActive = true
                },
                new SceneSetup
                {
                    path = "Assets/Scenes/Lobby.unity",
                    isLoaded = true,
                    isActive = false
                },
                new SceneSetup
                {
                    path = "Assets/Scenes/PlantRoom.unity",
                    isLoaded = false,
                    isActive = false
                }
            };

            string serialized = (string)serialize.Invoke(null, new object[] { original });
            SceneSetup[] restored = (SceneSetup[])deserialize.Invoke(null, new object[] { serialized });

            Assert.That(restored, Has.Length.EqualTo(original.Length));
            for (int index = 0; index < original.Length; index++)
            {
                Assert.That(restored[index].path, Is.EqualTo(original[index].path));
                Assert.That(restored[index].isLoaded, Is.EqualTo(original[index].isLoaded));
                Assert.That(restored[index].isActive, Is.EqualTo(original[index].isActive));
            }
        }

        [Test]
        public void SceneSetupRestorationRejectsUntitledScenesWithoutOpeningThem()
        {
            Type harnessType = Type.GetType(HarnessTypeName);
            MethodInfo deserialize = harnessType.GetMethod(
                "DeserializeSceneSetupForTesting",
                BindingFlags.Public | BindingFlags.Static);
            const string untitledSceneSetup =
                "{\"scenes\":[{\"path\":\"\",\"isLoaded\":true,\"isActive\":true}]}";

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => deserialize.Invoke(null, new object[] { untitledSceneSetup }));

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        private static void AssertLockEntry(
            string lockFile,
            string packageName,
            string expectedVersion,
            int expectedDepth,
            IReadOnlyDictionary<string, string> expectedDependencies)
        {
            string entryJson = GetTopLevelLockEntry(lockFile, packageName);
            LockEntry entry = JsonUtility.FromJson<LockEntry>(entryJson);

            Assert.That(entry.version, Is.EqualTo(expectedVersion), packageName);
            Assert.That(entry.depth, Is.EqualTo(expectedDepth), packageName);
            Assert.That(entry.source, Is.EqualTo("registry"), packageName);

            string dependenciesJson = GetObjectProperty(entryJson, "dependencies");
            Dictionary<string, string> dependencies = ParseStringProperties(dependenciesJson);
            Assert.That(dependencies.Count, Is.EqualTo(expectedDependencies.Count), packageName);
            foreach (KeyValuePair<string, string> expectedDependency in expectedDependencies)
            {
                Assert.That(dependencies.TryGetValue(expectedDependency.Key, out string resolvedVersion), Is.True,
                    packageName);
                Assert.That(resolvedVersion, Is.EqualTo(expectedDependency.Value), packageName);
            }
        }

        private static string GetTopLevelLockEntry(string lockFile, string packageName)
        {
            Match match = Regex.Match(
                lockFile,
                $"(?m)^    \\\"{Regex.Escape(packageName)}\\\": \\{{");
            Assert.That(match.Success, Is.True, packageName);
            int objectStart = lockFile.IndexOf('{', match.Index);
            Assert.That(objectStart, Is.GreaterThanOrEqualTo(match.Index), packageName);
            return lockFile.Substring(objectStart, FindMatchingBrace(lockFile, objectStart) - objectStart + 1);
        }

        private static string GetObjectProperty(string json, string propertyName)
        {
            string marker = $"\"{propertyName}\":";
            int propertyStart = json.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(propertyStart, Is.GreaterThanOrEqualTo(0), propertyName);
            int objectStart = json.IndexOf('{', propertyStart + marker.Length);
            Assert.That(objectStart, Is.GreaterThan(propertyStart), propertyName);
            return json.Substring(objectStart, FindMatchingBrace(json, objectStart) - objectStart + 1);
        }

        private static Dictionary<string, string> ParseStringProperties(string json)
        {
            var properties = new Dictionary<string, string>();
            MatchCollection matches = Regex.Matches(
                json,
                "\\\"(?<key>[^\\\"]+)\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"");
            for (int index = 0; index < matches.Count; index++)
            {
                properties.Add(matches[index].Groups["key"].Value, matches[index].Groups["value"].Value);
            }

            return properties;
        }

        private static int FindMatchingBrace(string text, int objectStart)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = objectStart; index < text.Length; index++)
            {
                char current = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '\"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '\"')
                {
                    inString = true;
                }
                else if (current == '{')
                {
                    depth++;
                }
                else if (current == '}' && --depth == 0)
                {
                    return index;
                }
            }

            Assert.Fail("The lock entry has an unmatched object brace.");
            return -1;
        }

        [Serializable]
        private sealed class LockEntry
        {
            public string version;
            public int depth;
            public string source;
        }

        [Serializable]
        private sealed class SourceEvidence
        {
            public int schemaVersion;
            public string scenario;
            public string unityVersion;
            public int rendererCount;
            public int savedSharedMaterialReferenceCount;
            public int propertyBlockOverrideRendererCount;
            public int ownedRuntimeMaterialReferenceCount;
            public int distinctComparisonMaterialObjectCount;
            public string frameDebuggerStatus;
            public string runtimeCountersStatus;
            public string memorySnapshotStatus;
        }
    }
}

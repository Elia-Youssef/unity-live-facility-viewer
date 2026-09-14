using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FacilityViewer.Tests
{
    public sealed class ThemeTargetAssetTests
    {
        private const string ThemeTargetScriptPath = "Assets/Scripts/World/ThemeTarget.cs";
        private const string StandardStructureMaterialPath =
            "Assets/Art/Materials/Standard/FacilityStructure.mat";
        private const string StandardEquipmentMaterialPath =
            "Assets/Art/Materials/Standard/FacilityEquipment.mat";
        private const string MaintenanceThemePath = "Assets/Data/ThemeDefinitions/Maintenance.asset";
        private const string MaintenanceStructureMaterialPath =
            "Assets/Art/Materials/Maintenance/FacilityStructure.mat";

        private static readonly SceneExpectation[] SceneExpectations =
        {
            new("Assets/Scenes/Lobby.unity", 7, 1),
            new("Assets/Scenes/OperationsFloor.unity", 5, 6),
            new("Assets/Scenes/PlantRoom.unity", 5, 4)
        };

        [Test]
        public void FacilityScenesHaveCompleteSameSceneThemeTargetMappingsWithStandardSharedMaterials()
        {
            AssertRuntimeTypesAvailable();
            Material standardStructure = AssetDatabase.LoadAssetAtPath<Material>(StandardStructureMaterialPath);
            Material standardEquipment = AssetDatabase.LoadAssetAtPath<Material>(StandardEquipmentMaterialPath);
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                foreach (SceneExpectation expectation in SceneExpectations)
                {
                    Scene scene = EditorSceneManager.OpenScene(expectation.Path, OpenSceneMode.Single);
                    GameObject root = scene.GetRootGameObjects().Single();
                    Transform environment = root.transform.Find("Environment");
                    Component[] targets = environment.GetComponents(ThemeTargetType);

                    Assert.That(environment, Is.Not.Null, expectation.Path);
                    Assert.That(targets, Has.Length.EqualTo(1), expectation.Path);
                    Assert.That(root.GetComponentsInChildren(ThemeTargetType, true), Is.EquivalentTo(targets));

                    Component target = targets[0];
                    Assert.That(TryValidate(target, out string error), Is.True, error);
                    IReadOnlyList<object> mappings = GetMappings(target);
                    Assert.That(mappings.Count, Is.EqualTo(expectation.TotalSlots), expectation.Path);

                    int structureCount = 0;
                    int equipmentCount = 0;
                    HashSet<RendererSlot> mappedSlots = new();
                    Renderer[] environmentRenderers = environment.GetComponentsInChildren<Renderer>(true);

                    foreach (Renderer renderer in environmentRenderers)
                    {
                        Assert.That(renderer.GetComponentInParent(TeleportPadType), Is.Null, renderer.name);
                        List<Material> sharedMaterials = GetSharedMaterials(renderer);

                        for (int materialSlotIndex = 0;
                             materialSlotIndex < sharedMaterials.Count;
                             materialSlotIndex++)
                        {
                            object mapping = mappings.SingleOrDefault(candidate =>
                                GetMappingRenderer(candidate) == renderer
                                && GetMappingSlotIndex(candidate) == materialSlotIndex);

                            Assert.That(mapping, Is.Not.Null, $"{expectation.Path}: {renderer.name} slot {materialSlotIndex}");
                            Assert.That(GetMappingRenderer(mapping).gameObject.scene, Is.EqualTo(scene));
                            Assert.That(GetMappingRenderer(mapping).transform.IsChildOf(environment), Is.True);
                            Assert.That(IsKnownGroupId(GetMappingGroupId(mapping)), Is.True, GetMappingGroupId(mapping));
                            Assert.That(mappedSlots.Add(new RendererSlot(renderer, materialSlotIndex)), Is.True);

                            Material expectedMaterial = IsStructure(renderer.transform)
                                ? standardStructure
                                : standardEquipment;
                            string expectedGroupId = IsStructure(renderer.transform)
                                ? "facility-structure"
                                : "facility-equipment";

                            Assert.That(GetMappingGroupId(mapping), Is.EqualTo(expectedGroupId));
                            Assert.That(sharedMaterials[materialSlotIndex], Is.EqualTo(expectedMaterial));

                            if (GetMappingGroupId(mapping) == "facility-structure")
                            {
                                structureCount++;
                            }
                            else
                            {
                                equipmentCount++;
                            }
                        }
                    }

                    Assert.That(mappedSlots.Count, Is.EqualTo(mappings.Count));
                    Assert.That(structureCount, Is.EqualTo(expectation.StructureSlots), expectation.Path);
                    Assert.That(equipmentCount, Is.EqualTo(expectation.EquipmentSlots), expectation.Path);
                }
            }
            finally
            {
                if (previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void ThemeTargetValidationRejectsNullOutOfRangeUnknownAndDuplicateRendererSlotMappings()
        {
            AssertRuntimeTypesAvailable();
            GameObject targetObject = new("Theme Target Validation");
            Renderer renderer = targetObject.AddComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(StandardStructureMaterialPath);

            try
            {
                SetSharedMaterials(renderer, material);
                Component target = targetObject.AddComponent(ThemeTargetType);

                ConfigureMappings(target, new Mapping(null, 0, "facility-structure"));
                Assert.That(TryValidate(target, out string nullError), Is.False);
                Assert.That(nullError, Does.Contain("no renderer"));

                ConfigureMappings(target, new Mapping(renderer, 1, "facility-structure"));
                Assert.That(TryValidate(target, out string rangeError), Is.False);
                Assert.That(rangeError, Does.Contain("references material slot 1"));

                ConfigureMappings(target, new Mapping(renderer, 0, "unknown-group"));
                Assert.That(TryValidate(target, out string groupError), Is.False);
                Assert.That(groupError, Does.Contain("unknown group ID 'unknown-group'"));

                ConfigureMappings(
                    target,
                    new Mapping(renderer, 0, "facility-structure"),
                    new Mapping(renderer, 0, "facility-equipment"));
                Assert.That(TryValidate(target, out string duplicateError), Is.False);
                Assert.That(duplicateError, Does.Contain("more than once"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void ThemeApplicationDoesNotChangeAnyRendererWhenOneMappingIsInvalid()
        {
            AssertRuntimeTypesAvailable();
            Material standardStructure = AssetDatabase.LoadAssetAtPath<Material>(StandardStructureMaterialPath);
            Material maintenanceStructure = AssetDatabase.LoadAssetAtPath<Material>(MaintenanceStructureMaterialPath);
            ScriptableObject maintenance = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MaintenanceThemePath);
            GameObject targetObject = new("Theme Target Atomic Application");
            Renderer validRenderer = targetObject.AddComponent<MeshRenderer>();
            GameObject invalidObject = new("Invalid Theme Target Renderer");
            Renderer invalidRenderer = invalidObject.AddComponent<MeshRenderer>();

            try
            {
                SetSharedMaterials(validRenderer, standardStructure);
                SetSharedMaterials(invalidRenderer, standardStructure);
                Component target = targetObject.AddComponent(ThemeTargetType);
                ConfigureMappings(target, new Mapping(validRenderer, 0, "facility-structure"));

                Assert.That(TryApplyTheme(target, maintenance, out string successError), Is.True, successError);
                Assert.That(GetSharedMaterials(validRenderer).Single(), Is.EqualTo(maintenanceStructure));

                SetSharedMaterials(validRenderer, standardStructure);
                ConfigureMappings(
                    target,
                    new Mapping(validRenderer, 0, "facility-structure"),
                    new Mapping(invalidRenderer, 1, "facility-equipment"));

                Assert.That(TryApplyTheme(target, maintenance, out string error), Is.False);
                Assert.That(error, Does.Contain("references material slot 1"));
                Assert.That(GetSharedMaterials(validRenderer).Single(), Is.EqualTo(standardStructure));
                Assert.That(GetSharedMaterials(invalidRenderer).Single(), Is.EqualTo(standardStructure));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalidObject);
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void ThemeTargetUsesSharedMaterialsAndResetsStaticRegistrationEvents()
        {
            AssertRuntimeTypesAvailable();
            string source = File.ReadAllText(ThemeTargetScriptPath);
            MethodInfo resetMethod = ThemeTargetType.GetMethod(
                "ResetLifecycleEvents",
                BindingFlags.NonPublic | BindingFlags.Static);
            RuntimeInitializeOnLoadMethodAttribute resetAttribute = resetMethod?
                .GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();

            Assert.That(Regex.IsMatch(source, @"\.\s*material\b"), Is.False);
            Assert.That(source, Does.Contain("GetSharedMaterials"));
            Assert.That(source, Does.Contain("SetSharedMaterials"));
            Assert.That(ThemeTargetType.GetEvent("Enabled"), Is.Not.Null);
            Assert.That(ThemeTargetType.GetEvent("Disabled"), Is.Not.Null);
            Assert.That(resetAttribute, Is.Not.Null);
            Assert.That(resetAttribute.loadType, Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
        }

        private static Type ThemeTargetType => Type.GetType(
            "FacilityViewer.World.ThemeTarget, Assembly-CSharp");

        private static Type TeleportPadType => Type.GetType(
            "FacilityViewer.World.TeleportPad, Assembly-CSharp");

        private static Type MaterialThemeGroupIdsType => Type.GetType(
            "FacilityViewer.Core.MaterialThemeGroupIds, Assembly-CSharp");

        private static void AssertRuntimeTypesAvailable()
        {
            Assert.That(ThemeTargetType, Is.Not.Null);
            Assert.That(TeleportPadType, Is.Not.Null);
            Assert.That(MaterialThemeGroupIdsType, Is.Not.Null);
        }

        private static bool TryValidate(Component target, out string error)
        {
            MethodInfo method = ThemeTargetType.GetMethod("TryValidate");
            object[] arguments = { null };
            bool result = (bool)method.Invoke(target, arguments);
            error = (string)arguments[0];
            return result;
        }

        private static bool TryApplyTheme(Component target, ScriptableObject theme, out string error)
        {
            MethodInfo method = ThemeTargetType.GetMethod("TryApplyTheme");
            object[] arguments = { theme, null };
            bool result = (bool)method.Invoke(target, arguments);
            error = (string)arguments[1];
            return result;
        }

        private static IReadOnlyList<object> GetMappings(Component target)
        {
            IEnumerable mappings = (IEnumerable)ThemeTargetType.GetProperty("MaterialSlots").GetValue(target);
            return mappings.Cast<object>().ToArray();
        }

        private static Renderer GetMappingRenderer(object mapping)
        {
            return (Renderer)mapping.GetType().GetProperty("Renderer").GetValue(mapping);
        }

        private static int GetMappingSlotIndex(object mapping)
        {
            return (int)mapping.GetType().GetProperty("MaterialSlotIndex").GetValue(mapping);
        }

        private static string GetMappingGroupId(object mapping)
        {
            return (string)mapping.GetType().GetProperty("GroupId").GetValue(mapping);
        }

        private static bool IsKnownGroupId(string groupId)
        {
            MethodInfo method = MaterialThemeGroupIdsType.GetMethod("IsKnown");
            return (bool)method.Invoke(null, new object[] { groupId });
        }

        private static bool IsStructure(Transform rendererTransform)
        {
            string name = rendererTransform.name;
            return name == "Floor"
                || name.EndsWith(" Wall", StringComparison.Ordinal)
                || name.EndsWith(" Column", StringComparison.Ordinal);
        }

        private static List<Material> GetSharedMaterials(Renderer renderer)
        {
            List<Material> materials = new();
            renderer.GetSharedMaterials(materials);
            return materials;
        }

        private static void SetSharedMaterials(Renderer renderer, Material material)
        {
            renderer.SetSharedMaterials(new List<Material> { material });
        }

        private static void ConfigureMappings(Component target, params Mapping[] mappings)
        {
            SerializedObject serializedTarget = new(target);
            SerializedProperty materialSlots = serializedTarget.FindProperty("materialSlots");
            materialSlots.arraySize = mappings.Length;

            for (int index = 0; index < mappings.Length; index++)
            {
                SerializedProperty mapping = materialSlots.GetArrayElementAtIndex(index);
                mapping.FindPropertyRelative("renderer").objectReferenceValue = mappings[index].Renderer;
                mapping.FindPropertyRelative("materialSlotIndex").intValue = mappings[index].MaterialSlotIndex;
                mapping.FindPropertyRelative("groupId").stringValue = mappings[index].GroupId;
            }

            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private readonly struct SceneExpectation
        {
            public SceneExpectation(string path, int structureSlots, int equipmentSlots)
            {
                Path = path;
                StructureSlots = structureSlots;
                EquipmentSlots = equipmentSlots;
            }

            public string Path { get; }
            public int StructureSlots { get; }
            public int EquipmentSlots { get; }
            public int TotalSlots => StructureSlots + EquipmentSlots;
        }

        private readonly struct Mapping
        {
            public Mapping(Renderer renderer, int materialSlotIndex, string groupId)
            {
                Renderer = renderer;
                MaterialSlotIndex = materialSlotIndex;
                GroupId = groupId;
            }

            public Renderer Renderer { get; }
            public int MaterialSlotIndex { get; }
            public string GroupId { get; }
        }

        private readonly struct RendererSlot : IEquatable<RendererSlot>
        {
            private readonly Renderer renderer;
            private readonly int materialSlotIndex;

            public RendererSlot(Renderer renderer, int materialSlotIndex)
            {
                this.renderer = renderer;
                this.materialSlotIndex = materialSlotIndex;
            }

            public bool Equals(RendererSlot other)
            {
                return renderer == other.renderer && materialSlotIndex == other.materialSlotIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RendererSlot other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((renderer != null ? renderer.GetHashCode() : 0) * 397) ^ materialSlotIndex;
                }
            }
        }
    }
}

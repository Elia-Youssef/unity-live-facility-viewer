using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FacilityViewer.Tests
{
    public sealed class MaterialOwnershipComparisonTests
    {
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string SourceMaterialPath =
            "Assets/Art/Materials/Standard/FacilityStructure.mat";
        private const string GroupName = "Material Ownership Comparison";

        private static Type ShaderParameterControllerType => Type.GetType(
            "FacilityViewer.World.ShaderParameterController, Assembly-CSharp");
        private static Type OwnedRuntimeMaterialInstanceType => Type.GetType(
            "FacilityViewer.World.OwnedRuntimeMaterialInstance, Assembly-CSharp");
        private static Type ThemeTargetType => Type.GetType(
            "FacilityViewer.World.ThemeTarget, Assembly-CSharp");

        [Test]
        public void LobbyComparisonIsOutsideEnvironmentAndUsesTheSavedStandardSource()
        {
            AssertRuntimeTypesAvailable();
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                var scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
                GameObject root = scene.GetRootGameObjects().Single();
                Transform group = root.transform.Find(GroupName);
                Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);

                Assert.That(group, Is.Not.Null);
                Assert.That(group.parent, Is.EqualTo(root.transform));
                Assert.That(root.transform.Find("Environment"), Is.Not.Null);
                Assert.That(group.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(component => ThemeTargetType.IsInstanceOfType(component)), Is.False);
                Assert.That(group.childCount, Is.EqualTo(3));

                AssertComparisonObject(group, "Shared Saved Material", new Vector3(-4f, 0.8f, 6f),
                    sourceMaterial, null);
                AssertComparisonObject(group, "Property Block Override", new Vector3(0f, 0.8f, 6f),
                    sourceMaterial, ShaderParameterControllerType);
                AssertComparisonObject(group, "Owned Runtime Instance", new Vector3(4f, 0.8f, 6f),
                    sourceMaterial, OwnedRuntimeMaterialInstanceType);
            }
            finally
            {
                if (canRestore)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void ShaderParameterControllerCachesIdsUsesItsPropertyBlockAndRetainsTheSharedMaterial()
        {
            AssertRuntimeTypesAvailable();
            Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
            GameObject comparisonObject = new("Property Block Controller Test");
            comparisonObject.SetActive(false);
            MeshRenderer renderer = comparisonObject.AddComponent<MeshRenderer>();
            renderer.SetSharedMaterials(new List<Material> { sourceMaterial });
            Component controller = comparisonObject.AddComponent(ShaderParameterControllerType);
            Configure(controller, renderer, sourceMaterial);

            comparisonObject.SetActive(true);
            Assert.That(TryInvoke(controller, "TryApply", out string error), Is.True, error);
            Assert.That(GetProperty<bool>(controller, "IsApplied"), Is.True);
            Assert.That(GetStaticProperty<int>(ShaderParameterControllerType, "MaintenanceTintPropertyId"),
                Is.EqualTo(Shader.PropertyToID("_MaintenanceTint")));
            Assert.That(GetStaticProperty<int>(ShaderParameterControllerType, "MaintenanceBlendPropertyId"),
                Is.EqualTo(Shader.PropertyToID("_MaintenanceBlend")));
            Assert.That(GetSharedMaterials(renderer).Single(), Is.SameAs(sourceMaterial));

            MaterialPropertyBlock propertyBlock = new();
            renderer.GetPropertyBlock(propertyBlock, 0);
            Assert.That(propertyBlock.GetColor(GetStaticProperty<int>(
                    ShaderParameterControllerType,
                    "MaintenanceTintPropertyId")),
                Is.EqualTo(new Color(1f, 0.45f, 0f, 1f)));
            Assert.That(propertyBlock.GetFloat(GetStaticProperty<int>(
                    ShaderParameterControllerType,
                    "MaintenanceBlendPropertyId")),
                Is.EqualTo(0.7f).Within(0.0001f));

            controller.GetType().GetMethod("ClearOverride").Invoke(controller, null);
            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock, 0);
            Assert.That(propertyBlock.GetFloat(GetStaticProperty<int>(
                    ShaderParameterControllerType,
                    "MaintenanceBlendPropertyId")),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(GetProperty<bool>(controller, "IsApplied"), Is.False);
            Assert.That(GetSharedMaterials(renderer).Single(), Is.SameAs(sourceMaterial));

            UnityEngine.Object.DestroyImmediate(comparisonObject);
        }

        [Test]
        public void RuntimeControllersDoNotUseRendererMaterialAccessors()
        {
            string shaderParameterSource = File.ReadAllText(
                "Assets/Scripts/World/ShaderParameterController.cs");
            string ownedInstanceSource = File.ReadAllText(
                "Assets/Scripts/World/OwnedRuntimeMaterialInstance.cs");

            Assert.That(shaderParameterSource, Does.Not.Match(@"targetRenderer\.materials?\b"));
            Assert.That(ownedInstanceSource, Does.Not.Match(@"targetRenderer\.materials?\b"));
            Assert.That(ownedInstanceSource, Does.Contain("SetSharedMaterials"));
            Assert.That(ownedInstanceSource, Does.Contain("Destroy(ownedMaterial)"));
        }

        private static void AssertComparisonObject(
            Transform group,
            string name,
            Vector3 expectedPosition,
            Material sourceMaterial,
            Type expectedControllerType)
        {
            Transform comparison = group.Find(name);
            Assert.That(comparison, Is.Not.Null, name);
            Assert.That(comparison.localPosition, Is.EqualTo(expectedPosition));
            Assert.That(comparison.localScale, Is.EqualTo(new Vector3(1.5f, 1.5f, 1.5f)));
            Assert.That(comparison.GetComponent<Collider>(), Is.Null);

            Renderer renderer = comparison.GetComponent<Renderer>();
            Assert.That(GetSharedMaterials(renderer).Single(), Is.SameAs(sourceMaterial));

            if (expectedControllerType == null)
            {
                Assert.That(comparison.GetComponents<MonoBehaviour>(), Is.Empty);
                return;
            }

            Component controller = comparison.GetComponent(expectedControllerType);
            Assert.That(controller, Is.Not.Null, name);
            SerializedObject serialized = new(controller);
            Assert.That(serialized.FindProperty("targetRenderer").objectReferenceValue, Is.EqualTo(renderer));
            Assert.That(serialized.FindProperty("materialSlotIndex").intValue, Is.EqualTo(0));
            Assert.That(serialized.FindProperty("sourceMaterial").objectReferenceValue, Is.EqualTo(sourceMaterial));
        }

        private static void Configure(Component controller, Renderer renderer, Material sourceMaterial)
        {
            SerializedObject serialized = new(controller);
            serialized.FindProperty("targetRenderer").objectReferenceValue = renderer;
            serialized.FindProperty("materialSlotIndex").intValue = 0;
            serialized.FindProperty("sourceMaterial").objectReferenceValue = sourceMaterial;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool TryInvoke(Component component, string methodName, out string error)
        {
            object[] arguments = { null };
            bool result = (bool)component.GetType().GetMethod(methodName).Invoke(component, arguments);
            error = (string)arguments[0];
            return result;
        }

        private static List<Material> GetSharedMaterials(Renderer renderer)
        {
            List<Material> materials = new();
            renderer.GetSharedMaterials(materials);
            return materials;
        }

        private static T GetProperty<T>(Component component, string propertyName)
        {
            return (T)component.GetType()
                .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(component);
        }

        private static T GetStaticProperty<T>(Type type, string propertyName)
        {
            return (T)type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
        }

        private static void AssertRuntimeTypesAvailable()
        {
            Assert.That(ShaderParameterControllerType, Is.Not.Null);
            Assert.That(OwnedRuntimeMaterialInstanceType, Is.Not.Null);
            Assert.That(ThemeTargetType, Is.Not.Null);
        }
    }
}

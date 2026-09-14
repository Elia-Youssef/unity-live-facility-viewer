using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class MaterialOwnershipComparisonPlayModeTests
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string GroupName = "Material Ownership Comparison";
        private const float TimeoutSeconds = 10f;

        [UnityTest]
        public IEnumerator ComparisonPreservesSharedOwnershipAndDestroysOnlyItsOwnedInstance()
        {
            Type appStateType = GetRuntimeType("FacilityViewer.Core.AppState");
            Type themeServiceType = GetRuntimeType("FacilityViewer.Services.MaterialThemeService");
            Type parameterControllerType = GetRuntimeType("FacilityViewer.World.ShaderParameterController");
            Type instanceOwnerType = GetRuntimeType("FacilityViewer.World.OwnedRuntimeMaterialInstance");

            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);

            Component appState = FindRuntimeComponent(appStateType);
            Component themeService = FindRuntimeComponent(themeServiceType);
            yield return WaitForLevel(appState, "lobby");

            Transform group = SceneManager.GetSceneByPath(LobbyPath).GetRootGameObjects().Single()
                .transform.Find(GroupName);
            Assert.That(group, Is.Not.Null);

            Renderer sharedRenderer = group.Find("Shared Saved Material").GetComponent<Renderer>();
            Renderer propertyBlockRenderer = group.Find("Property Block Override").GetComponent<Renderer>();
            Renderer ownedRenderer = group.Find("Owned Runtime Instance").GetComponent<Renderer>();
            Component parameterController = propertyBlockRenderer.GetComponent(parameterControllerType);
            Component instanceOwner = ownedRenderer.GetComponent(instanceOwnerType);
            Material sourceMaterial = GetSharedMaterials(sharedRenderer).Single();
            int baseColorId = Shader.PropertyToID("_BaseColor");
            Color sourceBaseColor = sourceMaterial.GetColor(baseColorId);

            Assert.That(GetSharedMaterials(propertyBlockRenderer).Single(), Is.SameAs(sourceMaterial));
            Assert.That(GetProperty<bool>(parameterController, "IsApplied"), Is.True);
            MaterialPropertyBlock propertyBlock = new();
            propertyBlockRenderer.GetPropertyBlock(propertyBlock, 0);
            Assert.That(propertyBlock.GetFloat(Shader.PropertyToID("_MaintenanceBlend")),
                Is.EqualTo(0.7f).Within(0.0001f));

            Behaviour parameterBehaviour = (Behaviour)parameterController;
            parameterBehaviour.enabled = false;
            yield return null;
            propertyBlock.Clear();
            propertyBlockRenderer.GetPropertyBlock(propertyBlock, 0);
            Assert.That(GetProperty<bool>(parameterController, "IsApplied"), Is.False);
            Assert.That(propertyBlock.GetFloat(Shader.PropertyToID("_MaintenanceBlend")),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(GetSharedMaterials(propertyBlockRenderer).Single(), Is.SameAs(sourceMaterial));
            parameterBehaviour.enabled = true;
            yield return null;
            Assert.That(GetProperty<bool>(parameterController, "IsApplied"), Is.True);

            Material ownedMaterial = GetProperty<Material>(instanceOwner, "RuntimeMaterial");
            Assert.That(ownedMaterial, Is.Not.Null);
            Assert.That(ownedMaterial, Is.Not.SameAs(sourceMaterial));
            Assert.That(GetSharedMaterials(ownedRenderer).Single(), Is.SameAs(ownedMaterial));
            Assert.That(GetProperty<int>(instanceOwner, "CreatedInstanceCount"), Is.EqualTo(1));
            Assert.That(CountOwnedComparisonMaterials(ownedMaterial.name), Is.EqualTo(1));
            Assert.That(sourceMaterial.GetColor(baseColorId), Is.EqualTo(sourceBaseColor));

            Behaviour ownerBehaviour = (Behaviour)instanceOwner;
            ownerBehaviour.enabled = false;
            yield return null;
            ownerBehaviour.enabled = true;
            yield return null;
            Assert.That(GetProperty<Material>(instanceOwner, "RuntimeMaterial"), Is.SameAs(ownedMaterial));
            Assert.That(GetProperty<int>(instanceOwner, "CreatedInstanceCount"), Is.EqualTo(1));

            for (int repeat = 0; repeat < 3; repeat++)
            {
                Assert.That(RequestTheme(themeServiceType, themeService, "maintenance"), Is.True);
                Assert.That(RequestTheme(themeServiceType, themeService, "emergency"), Is.True);
                Assert.That(RequestTheme(themeServiceType, themeService, "standard"), Is.True);
            }

            Assert.That(GetSharedMaterials(sharedRenderer).Single(), Is.SameAs(sourceMaterial));
            Assert.That(GetSharedMaterials(propertyBlockRenderer).Single(), Is.SameAs(sourceMaterial));
            Assert.That(GetSharedMaterials(ownedRenderer).Single(), Is.SameAs(ownedMaterial));
            Assert.That(GetProperty<Material>(instanceOwner, "RuntimeMaterial"), Is.SameAs(ownedMaterial));
            Assert.That(GetProperty<int>(instanceOwner, "CreatedInstanceCount"), Is.EqualTo(1));
            Assert.That(CountOwnedComparisonMaterials(ownedMaterial.name), Is.EqualTo(1));
            Assert.That(sourceMaterial.GetColor(baseColorId), Is.EqualTo(sourceBaseColor));

            GameObject temporaryRendererObject = new("Owned Material Restoration Renderer");
            MeshRenderer temporaryRenderer = temporaryRendererObject.AddComponent<MeshRenderer>();
            temporaryRenderer.SetSharedMaterials(new List<Material> { sourceMaterial });
            GameObject temporaryOwnerObject = new("Owned Material Restoration Owner");
            temporaryOwnerObject.SetActive(false);
            Component temporaryOwner = temporaryOwnerObject.AddComponent(instanceOwnerType);
            SetPrivateField(temporaryOwner, "targetRenderer", temporaryRenderer);
            SetPrivateField(temporaryOwner, "materialSlotIndex", 0);
            SetPrivateField(temporaryOwner, "sourceMaterial", sourceMaterial);

            temporaryOwnerObject.SetActive(true);
            yield return null;
            Material temporaryOwnedMaterial = GetProperty<Material>(temporaryOwner, "RuntimeMaterial");
            Assert.That(temporaryOwnedMaterial, Is.Not.Null);
            Assert.That(GetSharedMaterials(temporaryRenderer).Single(), Is.SameAs(temporaryOwnedMaterial));
            Assert.That(GetProperty<int>(temporaryOwner, "CreatedInstanceCount"), Is.EqualTo(1));
            Assert.That(CountOwnedComparisonMaterials(ownedMaterial.name), Is.EqualTo(2));
            Assert.That(sourceMaterial.GetColor(baseColorId), Is.EqualTo(sourceBaseColor));

            UnityEngine.Object.Destroy(temporaryOwnerObject);
            yield return null;
            Assert.That(GetSharedMaterials(temporaryRenderer).Single(), Is.SameAs(sourceMaterial));
            Assert.That(temporaryOwnedMaterial == null, Is.True);
            Assert.That(CountOwnedComparisonMaterials(ownedMaterial.name), Is.EqualTo(1));
            UnityEngine.Object.Destroy(temporaryRendererObject);

            GameObject invalidOwnerObject = new("Invalid Owned Material Owner");
            invalidOwnerObject.SetActive(false);
            Component invalidOwner = invalidOwnerObject.AddComponent(instanceOwnerType);
            SetPrivateField(invalidOwner, "targetRenderer", sharedRenderer);
            SetPrivateField(invalidOwner, "materialSlotIndex", 0);
            invalidOwnerObject.SetActive(true);
            yield return null;
            Assert.That(TryInvoke(invalidOwner, "TryCreateAndAssign", out _), Is.False);
            Assert.That(GetProperty<Material>(invalidOwner, "RuntimeMaterial"), Is.Null);
            Assert.That(GetProperty<int>(invalidOwner, "CreatedInstanceCount"), Is.EqualTo(0));
            Assert.That(GetSharedMaterials(sharedRenderer).Single(), Is.SameAs(sourceMaterial));
            UnityEngine.Object.Destroy(invalidOwnerObject);

            yield return CleanUpLoadedScenes();
        }

        private static Type GetRuntimeType(string name)
        {
            Type type = Type.GetType($"{name}, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        private static Component FindRuntimeComponent(Type type)
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Single(component => type.IsInstanceOfType(component) && component.gameObject.scene.IsValid());
        }

        private static bool RequestTheme(Type serviceType, Component service, string themeId)
        {
            return (bool)serviceType.GetMethod("RequestTheme", new[] { typeof(string) })
                .Invoke(service, new object[] { themeId });
        }

        private static bool TryInvoke(Component component, string methodName, out string error)
        {
            object[] arguments = { null };
            bool result = (bool)component.GetType().GetMethod(methodName).Invoke(component, arguments);
            error = (string)arguments[0];
            return result;
        }

        private static void SetPrivateField(Component component, string fieldName, object value)
        {
            component.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(component, value);
        }

        private static List<Material> GetSharedMaterials(Renderer renderer)
        {
            List<Material> materials = new();
            renderer.GetSharedMaterials(materials);
            return materials;
        }

        private static int CountOwnedComparisonMaterials(string materialName)
        {
            return Resources.FindObjectsOfTypeAll<Material>()
                .Count(material => material != null && material.name == materialName);
        }

        private static T GetProperty<T>(Component component, string propertyName)
        {
            return (T)component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(component);
        }

        private static IEnumerator WaitForLevel(Component appState, string levelId)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetProperty<string>(appState, "CurrentLevelId") == levelId
                    && !GetProperty<bool>(appState, "IsTransitioning"))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for facility level '{levelId}'.");
        }

        private static IEnumerator CleanUpLoadedScenes()
        {
            Scene cleanupScene = SceneManager.CreateScene("MaterialOwnershipComparisonCleanup");
            SceneManager.SetActiveScene(cleanupScene);
            Scene[] loadedScenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.handle != cleanupScene.handle)
                .ToArray();

            for (int index = 0; index < loadedScenes.Length; index++)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(loadedScenes[index]);
                if (unload != null)
                {
                    yield return unload;
                }
            }
        }
    }
}

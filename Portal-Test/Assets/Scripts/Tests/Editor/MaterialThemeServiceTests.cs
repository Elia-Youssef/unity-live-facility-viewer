using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace FacilityViewer.Tests
{
    public sealed class MaterialThemeServiceTests
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string StandardDefinitionPath = "Assets/Data/ThemeDefinitions/Standard.asset";
        private const string MaintenanceDefinitionPath = "Assets/Data/ThemeDefinitions/Maintenance.asset";
        private const string EmergencyDefinitionPath = "Assets/Data/ThemeDefinitions/Emergency.asset";
        private const string StandardStructureMaterialPath =
            "Assets/Art/Materials/Standard/FacilityStructure.mat";

        private readonly List<GameObject> temporaryObjects = new();

        private static Type AppStateType => Type.GetType("FacilityViewer.Core.AppState, Assembly-CSharp");
        private static Type ServiceType => Type.GetType(
            "FacilityViewer.Services.MaterialThemeService, Assembly-CSharp");
        private static Type TargetType => Type.GetType("FacilityViewer.World.ThemeTarget, Assembly-CSharp");
        private static Type DefinitionType => Type.GetType(
            "FacilityViewer.Core.MaterialThemeDefinition, Assembly-CSharp");

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < temporaryObjects.Count; index++)
            {
                if (temporaryObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryObjects[index]);
                }
            }

            temporaryObjects.Clear();
        }

        [Test]
        public void BootstrapOwnsThePersistentThemeServiceWithTheCompleteCatalog()
        {
            AssertRuntimeTypesAvailable();
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestorePreviousSetup = previousSetup.Any(scene => scene.isLoaded);

            try
            {
                var scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
                GameObject bootstrap = scene.GetRootGameObjects().Single();
                Component bootstrapper = bootstrap.GetComponent("AppBootstrapper");
                Component appState = bootstrap.GetComponent(AppStateType);
                Component service = bootstrap.GetComponent(ServiceType);

                Assert.That(bootstrapper, Is.Not.Null);
                Assert.That(appState, Is.Not.Null);
                Assert.That(service, Is.Not.Null);
                Assert.That(bootstrap.GetComponentsInChildren(ServiceType, true), Has.Length.EqualTo(1));

                SerializedObject serializedBootstrapper = new(bootstrapper);
                SerializedObject serializedService = new(service);
                SerializedProperty catalog = serializedService.FindProperty("themeCatalog");

                Assert.That(
                    serializedBootstrapper.FindProperty("materialThemeService").objectReferenceValue,
                    Is.EqualTo(service));
                Assert.That(serializedService.FindProperty("appState").objectReferenceValue, Is.EqualTo(appState));
                Assert.That(catalog.arraySize, Is.EqualTo(3));
                Assert.That(catalog.GetArrayElementAtIndex(0).objectReferenceValue,
                    Is.EqualTo(LoadDefinition(StandardDefinitionPath)));
                Assert.That(catalog.GetArrayElementAtIndex(1).objectReferenceValue,
                    Is.EqualTo(LoadDefinition(MaintenanceDefinitionPath)));
                Assert.That(catalog.GetArrayElementAtIndex(2).objectReferenceValue,
                    Is.EqualTo(LoadDefinition(EmergencyDefinitionPath)));
            }
            finally
            {
                if (canRestorePreviousSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [Test]
        public void RequestThemePrevalidatesEveryRegisteredTargetAndRetainsSharedMaterialIdentity()
        {
            AssertRuntimeTypesAvailable();
            Material standardMaterial = AssetDatabase.LoadAssetAtPath<Material>(StandardStructureMaterialPath);
            UnityEngine.Object standard = LoadDefinition(StandardDefinitionPath);
            UnityEngine.Object maintenance = LoadDefinition(MaintenanceDefinitionPath);
            UnityEngine.Object emergency = LoadDefinition(EmergencyDefinitionPath);
            Component service = CreateInitializedService(standard, maintenance, emergency);
            Component firstTarget = CreateTarget("First Theme Target", standardMaterial);
            Component secondTarget = CreateTarget("Second Theme Target", standardMaterial);

            Invoke(service, "RefreshRegisteredTargets");

            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(2));
            Assert.That(RequestTheme(service, "maintenance"), Is.True);
            Material maintenanceMaterial = GetThemeMaterial(maintenance, "facility-structure");
            Assert.That(GetSharedMaterials(GetSlotRenderer(firstTarget)).Single(), Is.EqualTo(maintenanceMaterial));
            Assert.That(GetSharedMaterials(GetSlotRenderer(secondTarget)).Single(), Is.EqualTo(maintenanceMaterial));

            int stableCount = GetProperty<int>(service, "RegisteredTargetCount");
            Assert.That(RequestTheme(service, "maintenance"), Is.True);
            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(stableCount));
            Assert.That(GetSharedMaterials(GetSlotRenderer(firstTarget)).Single(), Is.SameAs(maintenanceMaterial));
            Assert.That(GetSharedMaterials(GetSlotRenderer(secondTarget)).Single(), Is.SameAs(maintenanceMaterial));

            SetMaterialSlot(secondTarget, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[Material Theme\] Theme request rejected:.*material slot 1"));
            Assert.That(RequestTheme(service, "emergency"), Is.False);
            Assert.That(GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"), Is.EqualTo(maintenance));
            Assert.That(GetProperty<string>(GetProperty<Component>(service, "State"), "SelectedThemeId"),
                Is.EqualTo("maintenance"));
            Assert.That(GetSharedMaterials(GetSlotRenderer(firstTarget)).Single(), Is.SameAs(maintenanceMaterial));
            Assert.That(GetSharedMaterials(GetSlotRenderer(secondTarget)).Single(), Is.SameAs(maintenanceMaterial));

            Assert.That(RequestTheme(service, "unknown-theme"), Is.False);
            Assert.That(GetProperty<string>(GetProperty<Component>(service, "State"), "SelectedThemeId"),
                Is.EqualTo("maintenance"));
        }

        [Test]
        public void IncompleteCatalogNeverMakesTheThemeServiceReady()
        {
            AssertRuntimeTypesAvailable();
            GameObject serviceRoot = new("Incomplete Material Theme Service Test");
            serviceRoot.SetActive(false);
            Component appState = serviceRoot.AddComponent(AppStateType);
            Component service = serviceRoot.AddComponent(ServiceType);
            SerializedObject serializedService = new(service);
            serializedService.FindProperty("appState").objectReferenceValue = appState;
            SerializedProperty catalog = serializedService.FindProperty("themeCatalog");
            catalog.arraySize = 1;
            catalog.GetArrayElementAtIndex(0).objectReferenceValue = LoadDefinition(StandardDefinitionPath);
            serializedService.ApplyModifiedPropertiesWithoutUndo();
            appState.GetType().GetMethod("Initialize")
                .Invoke(appState, new object[] { "Incomplete catalog test" });
            serviceRoot.SetActive(true);
            temporaryObjects.Add(serviceRoot);

            LogAssert.Expect(LogType.Error, new Regex(@"MaterialThemeService theme catalog is invalid:.*Missing a material theme definition"));
            Assert.That((bool)Invoke(service, "Initialize"), Is.False);
            Assert.That(GetProperty<bool>(service, "IsReady"), Is.False);
            Assert.That(GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"), Is.Null);
        }

        [Test]
        public void RuntimeValidationPolicyRelaxesOnlyNullGraphicsPropertyInspection()
        {
            Type policyType = Type.GetType(
                "FacilityViewer.Services.MaterialThemeRuntimeValidationPolicy, Assembly-CSharp");
            MethodInfo policy = policyType?.GetMethod(
                "ShouldValidateRequiredShaderProperties",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(policyType, Is.Not.Null);
            Assert.That(policy, Is.Not.Null);
            Assert.That(
                (bool)policy.Invoke(null, new object[] { GraphicsDeviceType.Null }),
                Is.False);
            Assert.That(
                (bool)policy.Invoke(null, new object[] { GraphicsDeviceType.Direct3D11 }),
                Is.True);
        }

        [Test]
        public void RendererSlotCollisionsRejectThemeRequestsAndDeduplicateLateRegistrationDiagnostics()
        {
            AssertRuntimeTypesAvailable();
            Material standardMaterial = AssetDatabase.LoadAssetAtPath<Material>(StandardStructureMaterialPath);
            UnityEngine.Object standard = LoadDefinition(StandardDefinitionPath);
            UnityEngine.Object maintenance = LoadDefinition(MaintenanceDefinitionPath);
            UnityEngine.Object emergency = LoadDefinition(EmergencyDefinitionPath);
            Component service = CreateInitializedService(standard, maintenance, emergency);
            Component firstTarget = CreateTarget(
                "Primary Theme Target",
                standardMaterial,
                "facility-structure");
            Component secondTarget = CreateTarget(
                "Secondary Theme Target",
                standardMaterial,
                "facility-equipment");

            Invoke(service, "RefreshRegisteredTargets");
            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(2));

            Renderer sharedRenderer = GetSlotRenderer(firstTarget);
            Renderer originalSecondRenderer = GetSlotRenderer(secondTarget);
            Material originalSecondMaterial = GetSharedMaterials(originalSecondRenderer).Single();
            SetMapping(secondTarget, sharedRenderer, 0, "facility-equipment");

            Component appState = GetProperty<Component>(service, "State");
            string initialStatus = GetProperty<string>(appState, "StatusMessage");
            LogAssert.Expect(LogType.Error, new Regex(@"\[Material Theme\] Theme request rejected:.*already claimed"));
            Assert.That(RequestTheme(service, "maintenance"), Is.False);
            Assert.That(GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"), Is.EqualTo(standard));
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo(initialStatus));
            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(2));
            Assert.That(GetSharedMaterials(sharedRenderer).Single(), Is.SameAs(standardMaterial));
            Assert.That(GetSharedMaterials(originalSecondRenderer).Single(), Is.SameAs(originalSecondMaterial));

            SetMapping(secondTarget, originalSecondRenderer, 0, "facility-equipment");
            UnityEngine.Object.DestroyImmediate(secondTarget.gameObject);
            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(1));

            Component lateTarget = CreateTarget(
                "Late Collision Theme Target",
                standardMaterial,
                "facility-equipment",
                sharedRenderer,
                false);
            LogAssert.Expect(LogType.Error, new Regex(@"\[Material Theme\] Target registration rejected:.*already claimed"));
            lateTarget.gameObject.SetActive(true);
            InvokeLifecycle(service, "OnTargetEnabled", lateTarget);
            Invoke(service, "RefreshRegisteredTargets");

            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(1));
            Assert.That(GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"), Is.EqualTo(standard));
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo(initialStatus));
            Assert.That(GetSharedMaterials(sharedRenderer).Single(), Is.SameAs(standardMaterial));

            Invoke(service, "RefreshRegisteredTargets");
            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(1));

            lateTarget.gameObject.SetActive(false);
            InvokeLifecycle(service, "OnTargetDisabled", lateTarget);
            LogAssert.Expect(LogType.Error, new Regex(@"\[Material Theme\] Target registration rejected:.*already claimed"));
            lateTarget.gameObject.SetActive(true);
            InvokeLifecycle(service, "OnTargetEnabled", lateTarget);

            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(1));
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo(initialStatus));

            Renderer repairedRenderer = lateTarget.gameObject.AddComponent<MeshRenderer>();
            repairedRenderer.SetSharedMaterials(new List<Material> { standardMaterial });
            SetMapping(lateTarget, repairedRenderer, 0, "facility-equipment");
            Invoke(service, "RefreshRegisteredTargets");

            Assert.That(GetProperty<int>(service, "RegisteredTargetCount"), Is.EqualTo(2));
            Assert.That(GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"), Is.EqualTo(standard));
            Assert.That(GetProperty<string>(appState, "SelectedThemeId"), Is.EqualTo("standard"));
            Assert.That(GetProperty<string>(appState, "StatusMessage"), Is.EqualTo(initialStatus));
        }

        private Component CreateInitializedService(params UnityEngine.Object[] definitions)
        {
            GameObject serviceRoot = new("Material Theme Service Test");
            serviceRoot.SetActive(false);
            Component appState = serviceRoot.AddComponent(AppStateType);
            Component service = serviceRoot.AddComponent(ServiceType);
            SerializedObject serializedService = new(service);
            serializedService.FindProperty("appState").objectReferenceValue = appState;
            SerializedProperty catalog = serializedService.FindProperty("themeCatalog");
            catalog.arraySize = definitions.Length;

            for (int index = 0; index < definitions.Length; index++)
            {
                catalog.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            }

            serializedService.ApplyModifiedPropertiesWithoutUndo();
            appState.GetType().GetMethod("Initialize")
                .Invoke(appState, new object[] { "Material theme service test ready" });
            serviceRoot.SetActive(true);
            temporaryObjects.Add(serviceRoot);

            Assert.That((bool)Invoke(service, "Initialize"), Is.True);
            Assert.That(GetProperty<bool>(service, "IsReady"), Is.True);
            Assert.That(GetProperty<UnityEngine.Object>(service, "CurrentThemeDefinition"), Is.EqualTo(definitions[0]));
            return service;
        }

        private Component CreateTarget(
            string name,
            Material material,
            string groupId = "facility-structure",
            Renderer mappedRenderer = null,
            bool activate = true)
        {
            GameObject targetObject = new(name);
            targetObject.SetActive(false);
            Renderer renderer = mappedRenderer ?? targetObject.AddComponent<MeshRenderer>();
            if (mappedRenderer == null)
            {
                renderer.SetSharedMaterials(new List<Material> { material });
            }

            Component target = targetObject.AddComponent(TargetType);
            SetMapping(target, renderer, 0, groupId);
            temporaryObjects.Add(targetObject);

            if (activate)
            {
                targetObject.SetActive(true);
            }

            return target;
        }

        private static void AssertRuntimeTypesAvailable()
        {
            Assert.That(AppStateType, Is.Not.Null);
            Assert.That(ServiceType, Is.Not.Null);
            Assert.That(TargetType, Is.Not.Null);
            Assert.That(DefinitionType, Is.Not.Null);
        }

        private static UnityEngine.Object LoadDefinition(string path)
        {
            UnityEngine.Object definition = AssetDatabase.LoadAssetAtPath(path, DefinitionType);
            Assert.That(definition, Is.Not.Null, path);
            return definition;
        }

        private static bool RequestTheme(Component service, string themeId)
        {
            return (bool)ServiceType.GetMethod("RequestTheme", new[] { typeof(string) })
                .Invoke(service, new object[] { themeId });
        }

        private static Material GetThemeMaterial(UnityEngine.Object definition, string groupId)
        {
            object[] arguments = { groupId, null };
            bool found = (bool)DefinitionType.GetMethod("TryGetMaterial").Invoke(definition, arguments);
            Assert.That(found, Is.True, groupId);
            return (Material)arguments[1];
        }

        private static Renderer GetSlotRenderer(Component target)
        {
            IEnumerable mappings = (IEnumerable)TargetType.GetProperty("MaterialSlots").GetValue(target);
            object mapping = mappings.Cast<object>().Single();
            return (Renderer)mapping.GetType().GetProperty("Renderer").GetValue(mapping);
        }

        private static List<Material> GetSharedMaterials(Renderer renderer)
        {
            List<Material> materials = new();
            renderer.GetSharedMaterials(materials);
            return materials;
        }

        private static void SetMapping(
            Component target,
            Renderer renderer,
            int materialSlotIndex,
            string groupId = "facility-structure")
        {
            SerializedObject serializedTarget = new(target);
            SerializedProperty mappings = serializedTarget.FindProperty("materialSlots");
            mappings.arraySize = 1;
            SerializedProperty mapping = mappings.GetArrayElementAtIndex(0);
            mapping.FindPropertyRelative("renderer").objectReferenceValue = renderer;
            mapping.FindPropertyRelative("materialSlotIndex").intValue = materialSlotIndex;
            mapping.FindPropertyRelative("groupId").stringValue = groupId;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetMaterialSlot(Component target, int materialSlotIndex)
        {
            SerializedObject serializedTarget = new(target);
            serializedTarget.FindProperty("materialSlots").GetArrayElementAtIndex(0)
                .FindPropertyRelative("materialSlotIndex").intValue = materialSlotIndex;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private static object Invoke(Component target, string methodName)
        {
            return target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                .Invoke(target, null);
        }

        private static void InvokeLifecycle(Component service, string methodName, Component target)
        {
            service.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(service, new object[] { target });
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            return (T)target.GetType().GetProperty(propertyName).GetValue(target);
        }
    }
}

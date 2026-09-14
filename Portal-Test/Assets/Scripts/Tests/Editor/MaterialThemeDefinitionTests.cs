using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FacilityViewer.Tests
{
    public sealed class MaterialThemeDefinitionTests
    {
        private const string DefinitionFolderPath = "Assets/Data/ThemeDefinitions";
        private const string FacilitySurfaceShaderPath = "Assets/Art/Shaders/FacilitySurface.shadergraph";

        [Test]
        public void ProjectMaterialThemeDefinitionsFormTheRequiredValidCatalog()
        {
            AssertRuntimeTypesAvailable();
            UnityEngine.Object[] definitions = LoadProjectDefinitions();
            IReadOnlyList<string> requiredThemeIds = GetStaticStringList(MaterialThemeIdsType, "RequiredIds");

            Assert.That(definitions.Select(GetThemeId), Is.EquivalentTo(requiredThemeIds));
            Assert.That(TryValidateCatalog(definitions, out IReadOnlyList<string> errors), Is.True, string.Join("\n", errors));

            foreach (UnityEngine.Object definition in definitions)
            {
                Assert.That(GetDefinitionProperty<string>(definition, "DisplayName"), Is.Not.Empty, GetThemeId(definition));
                IEnumerable mappings = (IEnumerable)GetDefinitionProperty<object>(definition, "GroupMaterials");
                Assert.That(mappings.Cast<object>().Count(), Is.EqualTo(GetStaticStringList(MaterialThemeGroupIdsType, "RequiredIds").Count));

                foreach (string groupId in GetStaticStringList(MaterialThemeGroupIdsType, "RequiredIds"))
                {
                    Assert.That(TryGetMaterial(definition, groupId, out Material material), Is.True, GetThemeId(definition));
                    Assert.That(material, Is.Not.Null, GetThemeId(definition));
                    Assert.That(
                        AssetDatabase.GetAssetPath(material),
                        Is.EqualTo(GetExpectedMaterialPath(GetThemeId(definition), groupId)));
                }
            }
        }

        [Test]
        public void FacilitySurfaceContractMatchesTheAuthoredShaderAndEveryThemeMaterial()
        {
            AssertRuntimeTypesAvailable();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(FacilitySurfaceShaderPath);
            UnityEngine.Object[] definitions = LoadProjectDefinitions();
            string shaderName = (string)FacilitySurfaceShaderType.GetField("ShaderName").GetValue(null);
            IReadOnlyList<string> requiredProperties = GetStaticStringList(
                FacilitySurfaceShaderType,
                "RequiredPropertyNames");

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo(shaderName));
            Assert.That(requiredProperties, Has.Count.EqualTo(11));
            Assert.That(requiredProperties.Distinct().Count(), Is.EqualTo(11));

            foreach (UnityEngine.Object definition in definitions)
            {
                IEnumerable mappings = (IEnumerable)GetDefinitionProperty<object>(definition, "GroupMaterials");
                foreach (object mapping in mappings)
                {
                    Material material = GetDefinitionProperty<Material>(mapping, "Material");
                    Assert.That(material.shader, Is.EqualTo(shader), GetThemeId(definition));

                    foreach (string propertyName in requiredProperties)
                    {
                        Assert.That(material.HasProperty(propertyName), Is.True, propertyName);
                    }
                }
            }
        }

        [Test]
        public void CatalogValidationRejectsMissingAndDuplicateThemeDefinitions()
        {
            AssertRuntimeTypesAvailable();
            UnityEngine.Object standard = CreateDefinition("standard", "Standard");
            UnityEngine.Object duplicateStandard = CreateDefinition("standard", "Standard Copy");
            UnityEngine.Object maintenance = CreateDefinition("maintenance", "Maintenance");

            try
            {
                Assert.That(
                    TryValidateCatalog(
                        new[] { standard, duplicateStandard, maintenance },
                        out IReadOnlyList<string> errors),
                    Is.False);
                Assert.That(errors, Has.Some.Contains("is duplicated"));
                Assert.That(errors, Has.Some.Contains("Missing a material theme definition for theme ID 'emergency'"));
            }
            finally
            {
                DestroyDefinitions(standard, duplicateStandard, maintenance);
            }
        }

        [Test]
        public void DefinitionValidationRejectsMissingDuplicateAndUnknownGroupsAndNullMaterials()
        {
            AssertRuntimeTypesAvailable();
            UnityEngine.Object definition = CreateDefinition("standard", "Standard");

            try
            {
                SerializedObject serializedDefinition = new(definition);
                SerializedProperty groupMaterials = serializedDefinition.FindProperty("groupMaterials");
                groupMaterials.arraySize = 3;
                SetMapping(groupMaterials.GetArrayElementAtIndex(0), "facility-equipment", null);
                SetMapping(groupMaterials.GetArrayElementAtIndex(1), "facility-equipment", LoadFacilityMaterial());
                SetMapping(groupMaterials.GetArrayElementAtIndex(2), "unknown-group", LoadFacilityMaterial());
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(TryValidateDefinition(definition, out IReadOnlyList<string> errors), Is.False);
                Assert.That(errors, Has.Some.Contains("missing group ID 'facility-structure'"));
                Assert.That(errors, Has.Some.Contains("more than once"));
                Assert.That(errors, Has.Some.Contains("unknown group ID 'unknown-group'"));
                Assert.That(errors, Has.Some.Contains("has no material"));
            }
            finally
            {
                DestroyDefinitions(definition);
            }
        }

        [Test]
        public void DefinitionValidationRejectsWrongShadersAndMissingFacilitySurfaceProperties()
        {
            AssertRuntimeTypesAvailable();
            UnityEngine.Object definition = CreateDefinition("standard", "Standard");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Material wrongShaderMaterial = new(litShader);

            try
            {
                Assert.That(litShader, Is.Not.Null);
                SerializedObject serializedDefinition = new(definition);
                serializedDefinition.FindProperty("groupMaterials").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("material").objectReferenceValue = wrongShaderMaterial;
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(TryValidateDefinition(definition, out IReadOnlyList<string> errors), Is.False);
                Assert.That(
                    errors,
                    Has.Some.Contains("expected '" +
                        (string)FacilitySurfaceShaderType.GetField("ShaderName").GetValue(null) + "'"));
                Assert.That(errors, Has.Some.Contains("missing required FacilitySurface shader property"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrongShaderMaterial);
                DestroyDefinitions(definition);
            }
        }

        [Test]
        public void RelaxedPropertyInspectionKeepsThemeAndMaterialContractValidation()
        {
            AssertRuntimeTypesAvailable();
            UnityEngine.Object standard = CreateDefinition("standard", "Standard");
            UnityEngine.Object maintenance = CreateDefinition("maintenance", "Maintenance");
            UnityEngine.Object emergency = CreateDefinition("emergency", "Emergency");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Material wrongShaderMaterial = new(litShader);

            try
            {
                Assert.That(litShader, Is.Not.Null);
                UnityEngine.Object[] definitions = { standard, maintenance, emergency };
                Assert.That(
                    TryValidateCatalogWithoutPropertyInspection(definitions, out IReadOnlyList<string> validErrors),
                    Is.True,
                    string.Join("\n", validErrors));

                SerializedObject serializedStandard = new(standard);
                SerializedProperty groupMaterials = serializedStandard.FindProperty("groupMaterials");
                groupMaterials.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("material").objectReferenceValue = wrongShaderMaterial;
                serializedStandard.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    TryValidateCatalogWithoutPropertyInspection(definitions, out IReadOnlyList<string> wrongShaderErrors),
                    Is.False);
                Assert.That(wrongShaderErrors, Has.Some.Contains("expected '" +
                    (string)FacilitySurfaceShaderType.GetField("ShaderName").GetValue(null) + "'"));
                Assert.That(wrongShaderErrors, Has.None.Contains("missing required FacilitySurface shader property"));

                groupMaterials.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("material").objectReferenceValue = null;
                serializedStandard.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    TryValidateCatalogWithoutPropertyInspection(definitions, out IReadOnlyList<string> nullMaterialErrors),
                    Is.False);
                Assert.That(nullMaterialErrors, Has.Some.Contains("has no material"));

                groupMaterials.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("material").objectReferenceValue = LoadFacilityMaterial();
                groupMaterials.arraySize = 1;
                serializedStandard.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    TryValidateCatalogWithoutPropertyInspection(definitions, out IReadOnlyList<string> incompleteGroupErrors),
                    Is.False);
                Assert.That(incompleteGroupErrors,
                    Has.Some.Contains("missing group ID 'facility-equipment'"));

                groupMaterials.arraySize = 2;
                SetMapping(groupMaterials.GetArrayElementAtIndex(0), "facility-structure", LoadFacilityMaterial());
                SetMapping(groupMaterials.GetArrayElementAtIndex(1), "facility-equipment", LoadFacilityMaterial());
                serializedStandard.FindProperty("themeId").stringValue = "unknown-theme";
                serializedStandard.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    TryValidateCatalogWithoutPropertyInspection(definitions, out IReadOnlyList<string> unknownThemeErrors),
                    Is.False);
                Assert.That(unknownThemeErrors, Has.Some.Contains("uses unknown theme ID 'unknown-theme'"));
                Assert.That(unknownThemeErrors,
                    Has.Some.Contains("Missing a material theme definition for theme ID 'standard'"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrongShaderMaterial);
                DestroyDefinitions(standard, maintenance, emergency);
            }
        }

        [Test]
        public void EditorProjectValidationCommandReportsTheValidProjectCatalog()
        {
            Type validatorType = Type.GetType(
                "FacilityViewer.Editor.MaterialThemeDefinitionProjectValidator, Assembly-CSharp-Editor");
            MethodInfo validateProject = validatorType?.GetMethod("TryValidateProject", BindingFlags.Public | BindingFlags.Static);
            object[] arguments = { null };

            Assert.That(validatorType, Is.Not.Null);
            Assert.That(validateProject, Is.Not.Null);
            Assert.That((bool)validateProject.Invoke(null, arguments), Is.True);
            Assert.That((IReadOnlyList<string>)arguments[0], Is.Empty);
        }

        private static Type MaterialThemeDefinitionType => Type.GetType(
            "FacilityViewer.Core.MaterialThemeDefinition, Assembly-CSharp");

        private static Type MaterialThemeDefinitionValidatorType => Type.GetType(
            "FacilityViewer.Core.MaterialThemeDefinitionValidator, Assembly-CSharp");

        private static Type MaterialThemeIdsType => Type.GetType(
            "FacilityViewer.Core.MaterialThemeIds, Assembly-CSharp");

        private static Type MaterialThemeGroupIdsType => Type.GetType(
            "FacilityViewer.Core.MaterialThemeGroupIds, Assembly-CSharp");

        private static Type FacilitySurfaceShaderType => Type.GetType(
            "FacilityViewer.Core.FacilitySurfaceShader, Assembly-CSharp");

        private static void AssertRuntimeTypesAvailable()
        {
            Assert.That(MaterialThemeDefinitionType, Is.Not.Null);
            Assert.That(MaterialThemeDefinitionValidatorType, Is.Not.Null);
            Assert.That(MaterialThemeIdsType, Is.Not.Null);
            Assert.That(MaterialThemeGroupIdsType, Is.Not.Null);
            Assert.That(FacilitySurfaceShaderType, Is.Not.Null);
        }

        private static UnityEngine.Object[] LoadProjectDefinitions()
        {
            string[] guids = AssetDatabase.FindAssets("t:MaterialThemeDefinition", new[] { DefinitionFolderPath });
            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath(path, MaterialThemeDefinitionType))
                .ToArray();
        }

        private static UnityEngine.Object CreateDefinition(string themeId, string displayName)
        {
            ScriptableObject definition = ScriptableObject.CreateInstance(MaterialThemeDefinitionType);
            SerializedObject serializedDefinition = new(definition);
            serializedDefinition.FindProperty("themeId").stringValue = themeId;
            serializedDefinition.FindProperty("displayName").stringValue = displayName;

            SerializedProperty groupMaterials = serializedDefinition.FindProperty("groupMaterials");
            groupMaterials.arraySize = 2;
            SetMapping(groupMaterials.GetArrayElementAtIndex(0), "facility-structure", LoadFacilityMaterial());
            SetMapping(groupMaterials.GetArrayElementAtIndex(1), "facility-equipment", LoadFacilityMaterial());
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static bool TryValidateCatalog(
            IReadOnlyList<UnityEngine.Object> definitions,
            out IReadOnlyList<string> errors)
        {
            Array typedDefinitions = Array.CreateInstance(MaterialThemeDefinitionType, definitions.Count);
            for (int index = 0; index < definitions.Count; index++)
            {
                typedDefinitions.SetValue(definitions[index], index);
            }

            MethodInfo validateCatalog = MaterialThemeDefinitionValidatorType.GetMethod(
                "TryValidateCatalog",
                BindingFlags.Public | BindingFlags.Static);
            object[] arguments = { typedDefinitions, null };
            bool isValid = (bool)validateCatalog.Invoke(null, arguments);
            errors = (IReadOnlyList<string>)arguments[1];
            return isValid;
        }

        private static bool TryValidateCatalogWithoutPropertyInspection(
            IReadOnlyList<UnityEngine.Object> definitions,
            out IReadOnlyList<string> errors)
        {
            Array typedDefinitions = Array.CreateInstance(MaterialThemeDefinitionType, definitions.Count);
            for (int index = 0; index < definitions.Count; index++)
            {
                typedDefinitions.SetValue(definitions[index], index);
            }

            MethodInfo validateCatalog = MaterialThemeDefinitionValidatorType
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name == "TryValidateCatalog"
                    && method.GetParameters().Length == 3);
            object[] arguments = { typedDefinitions, false, null };
            bool isValid = (bool)validateCatalog.Invoke(null, arguments);
            errors = (IReadOnlyList<string>)arguments[2];
            return isValid;
        }

        private static bool TryValidateDefinition(
            UnityEngine.Object definition,
            out IReadOnlyList<string> errors)
        {
            MethodInfo validateDefinition = MaterialThemeDefinitionValidatorType.GetMethod(
                "TryValidateDefinition",
                BindingFlags.Public | BindingFlags.Static);
            object[] arguments = { definition, null };
            bool isValid = (bool)validateDefinition.Invoke(null, arguments);
            errors = (IReadOnlyList<string>)arguments[1];
            return isValid;
        }

        private static bool TryGetMaterial(UnityEngine.Object definition, string groupId, out Material material)
        {
            MethodInfo tryGetMaterial = MaterialThemeDefinitionType.GetMethod("TryGetMaterial");
            object[] arguments = { groupId, null };
            bool found = (bool)tryGetMaterial.Invoke(definition, arguments);
            material = (Material)arguments[1];
            return found;
        }

        private static T GetDefinitionProperty<T>(object instance, string propertyName)
        {
            return (T)instance.GetType().GetProperty(propertyName).GetValue(instance);
        }

        private static IReadOnlyList<string> GetStaticStringList(Type type, string propertyName)
        {
            return (IReadOnlyList<string>)type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
        }

        private static string GetThemeId(UnityEngine.Object definition)
        {
            return GetDefinitionProperty<string>(definition, "ThemeId");
        }

        private static Material LoadFacilityMaterial()
        {
            return AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Materials/Standard/FacilityStructure.mat");
        }

        private static string GetExpectedMaterialPath(string themeId, string groupId)
        {
            string themeFolder = themeId switch
            {
                "standard" => "Standard",
                "maintenance" => "Maintenance",
                "emergency" => "Emergency",
                _ => throw new ArgumentOutOfRangeException(nameof(themeId), themeId, "Unknown theme ID.")
            };
            string materialName = groupId switch
            {
                "facility-structure" => "FacilityStructure",
                "facility-equipment" => "FacilityEquipment",
                _ => throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "Unknown group ID.")
            };

            return $"Assets/Art/Materials/{themeFolder}/{materialName}.mat";
        }

        private static void SetMapping(SerializedProperty mapping, string groupId, Material material)
        {
            mapping.FindPropertyRelative("groupId").stringValue = groupId;
            mapping.FindPropertyRelative("material").objectReferenceValue = material;
        }

        private static void DestroyDefinitions(params UnityEngine.Object[] definitions)
        {
            foreach (UnityEngine.Object definition in definitions)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }
    }
}

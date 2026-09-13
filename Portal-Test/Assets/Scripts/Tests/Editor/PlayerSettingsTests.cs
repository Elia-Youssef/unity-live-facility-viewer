using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FacilityViewer.Tests
{
    public sealed class PlayerSettingsTests
    {
        private const string SettingsAssetPath =
            "Assets/Data/PlayerSettings/DefaultPlayerSettings.asset";

        [Test]
        public void DefaultPlayerSettingsExposeExpectedConfiguration()
        {
            ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsAssetPath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.GetType().Name, Is.EqualTo("PlayerSettings"));
            Assert.That(settings.GetType().GetCustomAttribute<CreateAssetMenuAttribute>(), Is.Null);

            SerializedObject serializedSettings = new(settings);

            Assert.That(serializedSettings.FindProperty("walkSpeed").floatValue, Is.EqualTo(4f));
            Assert.That(serializedSettings.FindProperty("sprintSpeed").floatValue, Is.EqualTo(6.5f));
            Assert.That(serializedSettings.FindProperty("acceleration").floatValue, Is.EqualTo(18f));
            Assert.That(serializedSettings.FindProperty("deceleration").floatValue, Is.EqualTo(22f));
            Assert.That(serializedSettings.FindProperty("gravity").floatValue, Is.EqualTo(-20f));
            Assert.That(serializedSettings.FindProperty("groundedVerticalSpeed").floatValue, Is.EqualTo(-2f));
            Assert.That(serializedSettings.FindProperty("lookSensitivity").floatValue, Is.EqualTo(0.1f));
            Assert.That(serializedSettings.FindProperty("minimumPitch").floatValue, Is.EqualTo(-85f));
            Assert.That(serializedSettings.FindProperty("maximumPitch").floatValue, Is.EqualTo(85f));
            Assert.That(serializedSettings.FindProperty("slopeLimit").floatValue, Is.EqualTo(45f));
            Assert.That(serializedSettings.FindProperty("stepOffset").floatValue, Is.EqualTo(0.3f));

            AssertReadOnlyFloatProperty(settings, "WalkSpeed");
            AssertReadOnlyFloatProperty(settings, "SprintSpeed");
            AssertReadOnlyFloatProperty(settings, "Acceleration");
            AssertReadOnlyFloatProperty(settings, "Deceleration");
            AssertReadOnlyFloatProperty(settings, "Gravity");
            AssertReadOnlyFloatProperty(settings, "GroundedVerticalSpeed");
            AssertReadOnlyFloatProperty(settings, "LookSensitivity");
            AssertReadOnlyFloatProperty(settings, "MinimumPitch");
            AssertReadOnlyFloatProperty(settings, "MaximumPitch");
            AssertReadOnlyFloatProperty(settings, "SlopeLimit");
            AssertReadOnlyFloatProperty(settings, "StepOffset");
        }

        [Test]
        public void PlayerSettingsRepairInvalidInspectorValues()
        {
            ScriptableObject configuredSettings =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsAssetPath);
            ScriptableObject temporarySettings = ScriptableObject.CreateInstance(configuredSettings.GetType());

            try
            {
                SerializedObject serializedSettings = new(temporarySettings);

                serializedSettings.FindProperty("walkSpeed").floatValue = 8f;
                serializedSettings.FindProperty("sprintSpeed").floatValue = 6f;
                serializedSettings.FindProperty("gravity").floatValue = 5f;
                serializedSettings.FindProperty("lookSensitivity").floatValue = 0f;
                serializedSettings.FindProperty("minimumPitch").floatValue = -120f;
                serializedSettings.FindProperty("maximumPitch").floatValue = 120f;
                serializedSettings.FindProperty("slopeLimit").floatValue = -10f;
                serializedSettings.FindProperty("stepOffset").floatValue = 1f;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                temporarySettings.GetType()
                    .GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(temporarySettings, null);

                serializedSettings.Update();

                Assert.That(serializedSettings.FindProperty("sprintSpeed").floatValue, Is.EqualTo(8f));
                Assert.That(serializedSettings.FindProperty("gravity").floatValue, Is.EqualTo(-0.01f));
                Assert.That(serializedSettings.FindProperty("lookSensitivity").floatValue, Is.EqualTo(0.01f));
                Assert.That(serializedSettings.FindProperty("minimumPitch").floatValue, Is.EqualTo(-89f));
                Assert.That(serializedSettings.FindProperty("maximumPitch").floatValue, Is.EqualTo(89f));
                Assert.That(serializedSettings.FindProperty("slopeLimit").floatValue, Is.EqualTo(0f));
                Assert.That(serializedSettings.FindProperty("stepOffset").floatValue, Is.EqualTo(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(temporarySettings);
            }
        }

        private static void AssertReadOnlyFloatProperty(Object settings, string propertyName)
        {
            PropertyInfo property = settings.GetType().GetProperty(propertyName);

            Assert.That(property, Is.Not.Null, $"Missing property '{propertyName}'.");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(float)));
            Assert.That(property.CanRead, Is.True);
            Assert.That(property.CanWrite, Is.False);
        }
    }
}

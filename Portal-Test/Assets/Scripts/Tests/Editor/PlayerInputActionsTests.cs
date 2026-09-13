using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace FacilityViewer.Tests
{
    public sealed class PlayerInputActionsTests
    {
        private const string InputActionsPath = "Assets/Settings/Input/PlayerControls.inputactions";

        [Test]
        public void GameplayMapDefinesExpectedDesktopBindings()
        {
            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            Assert.That(inputActions, Is.Not.Null);
            Assert.That(inputActions.actionMaps.Select(map => map.name), Is.EqualTo(new[] { "Gameplay" }));

            InputActionMap gameplay = inputActions.FindActionMap("Gameplay", true);

            AssertAction(gameplay, "Move", InputActionType.Value, "Vector2");
            AssertAction(gameplay, "Look", InputActionType.Value, "Vector2");
            AssertAction(gameplay, "Sprint", InputActionType.Button, string.Empty);
            AssertAction(gameplay, "Interact", InputActionType.Button, string.Empty);
            AssertAction(gameplay, "TogglePanel", InputActionType.Button, string.Empty);
            Assert.That(gameplay.actions, Has.Count.EqualTo(5));
            Assert.That(gameplay.bindings, Has.Count.EqualTo(14));

            InputAction move = gameplay.FindAction("Move", true);

            Assert.That(move.bindings.Count(binding => binding.isComposite), Is.EqualTo(2));
            Assert.That(move.bindings.Count(binding => binding.isPartOfComposite), Is.EqualTo(8));
            Assert.That(
                move.bindings.Where(binding => binding.isPartOfComposite).Select(binding => binding.path),
                Is.EquivalentTo(new[]
                {
                    "<Keyboard>/w",
                    "<Keyboard>/s",
                    "<Keyboard>/a",
                    "<Keyboard>/d",
                    "<Keyboard>/upArrow",
                    "<Keyboard>/downArrow",
                    "<Keyboard>/leftArrow",
                    "<Keyboard>/rightArrow"
                }));

            AssertSingleBinding(gameplay, "Look", "<Mouse>/delta");
            AssertSingleBinding(gameplay, "Sprint", "<Keyboard>/leftShift");
            AssertSingleBinding(gameplay, "Interact", "<Keyboard>/e");
            AssertSingleBinding(gameplay, "TogglePanel", "<Keyboard>/tab");

            Assert.That(inputActions.controlSchemes, Has.Count.EqualTo(1));
            InputControlScheme keyboardAndMouse = inputActions.controlSchemes[0];

            Assert.That(keyboardAndMouse.name, Is.EqualTo("Keyboard&Mouse"));
            Assert.That(keyboardAndMouse.bindingGroup, Is.EqualTo("Keyboard&Mouse"));
            Assert.That(
                keyboardAndMouse.deviceRequirements.Select(requirement => requirement.controlPath),
                Is.EquivalentTo(new[] { "<Keyboard>", "<Mouse>" }));
        }

        private static void AssertAction(
            InputActionMap actionMap,
            string actionName,
            InputActionType actionType,
            string expectedControlType)
        {
            InputAction action = actionMap.FindAction(actionName, true);

            Assert.That(action.type, Is.EqualTo(actionType), actionName);
            Assert.That(action.expectedControlType, Is.EqualTo(expectedControlType), actionName);
        }

        private static void AssertSingleBinding(InputActionMap actionMap, string actionName, string path)
        {
            InputAction action = actionMap.FindAction(actionName, true);

            Assert.That(action.bindings, Has.Count.EqualTo(1), actionName);
            Assert.That(action.bindings[0].path, Is.EqualTo(path), actionName);
            Assert.That(action.bindings[0].groups, Is.EqualTo("Keyboard&Mouse"), actionName);
        }
    }
}

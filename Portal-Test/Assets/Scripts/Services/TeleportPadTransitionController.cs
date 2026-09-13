using System.Collections.Generic;
using FacilityViewer.Core;
using FacilityViewer.Player;
using FacilityViewer.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FacilityViewer.Services
{
    [DisallowMultipleComponent]
    public sealed class TeleportPadTransitionController : MonoBehaviour
    {
        [SerializeField] private AppState appState;
        [SerializeField] private LevelTeleportService levelTeleportService;
        [SerializeField] private PlayerTeleportPadInteractor playerInteractor;

        private readonly HashSet<TeleportPad> registeredPads = new();

        public int RegisteredPadCount => registeredPads.Count;

        private void Reset()
        {
            appState = GetComponent<AppState>();
            levelTeleportService = GetComponent<LevelTeleportService>();
            playerInteractor = GetComponentInChildren<PlayerTeleportPadInteractor>(true);
        }

        private void OnEnable()
        {
            appState ??= GetComponent<AppState>();
            levelTeleportService ??= GetComponent<LevelTeleportService>();
            playerInteractor ??= GetComponentInChildren<PlayerTeleportPadInteractor>(true);

            if (appState == null || levelTeleportService == null || playerInteractor == null)
            {
                Debug.LogError("[Teleport Pad] Transition controller references are incomplete.", this);
                enabled = false;
                return;
            }

            playerInteractor.TeleportRequested += OnTeleportRequested;
            appState.Changed += OnAppStateChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            TeleportPad.Enabled += OnPadEnabled;
            TeleportPad.Disabled += OnPadDisabled;

            RefreshRegisteredPads();
        }

        private void OnDisable()
        {
            if (playerInteractor != null)
            {
                playerInteractor.TeleportRequested -= OnTeleportRequested;
            }

            if (appState != null)
            {
                appState.Changed -= OnAppStateChanged;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            TeleportPad.Enabled -= OnPadEnabled;
            TeleportPad.Disabled -= OnPadDisabled;

            foreach (TeleportPad pad in registeredPads)
            {
                if (pad != null)
                {
                    pad.SetTransitionLocked(false);
                }
            }

            registeredPads.Clear();
        }

        public void RefreshRegisteredPads()
        {
            registeredPads.RemoveWhere(pad => pad == null);

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                RegisterScene(SceneManager.GetSceneAt(index));
            }

            ApplyTransitionLock();
        }

        public bool RequestTeleport(TeleportPad pad)
        {
            if (pad == null)
            {
                Debug.LogWarning("[Teleport Pad] Request rejected because no pad was supplied.", this);
                return false;
            }

            if (!pad.CanInteract)
            {
                string reason = pad.TryValidate(out string validationError)
                    ? "the pad is unavailable"
                    : validationError;
                Debug.LogWarning($"[Teleport Pad] Request rejected: {reason}", pad);
                return false;
            }

            return levelTeleportService.RequestTransition(
                pad.DestinationLevel,
                pad.DestinationSpawnId);
        }

        private void OnTeleportRequested(TeleportPad pad)
        {
            RequestTeleport(pad);
        }

        private void OnAppStateChanged(AppState _)
        {
            ApplyTransitionLock();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            RegisterScene(scene);
            ApplyTransitionLock();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            registeredPads.RemoveWhere(pad =>
                pad == null || pad.gameObject.scene.handle == scene.handle);
        }

        private void RegisterScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (TeleportPad pad in root.GetComponentsInChildren<TeleportPad>(true))
                {
                    RegisterPad(pad);
                }
            }
        }

        private void OnPadEnabled(TeleportPad pad)
        {
            RegisterPad(pad);
        }

        private void OnPadDisabled(TeleportPad pad)
        {
            if (pad != null)
            {
                registeredPads.Remove(pad);
                pad.SetTransitionLocked(false);
            }
        }

        private void RegisterPad(TeleportPad pad)
        {
            if (pad == null)
            {
                return;
            }

            registeredPads.Add(pad);
            pad.SetTransitionLocked(appState != null && appState.IsTransitioning);
        }

        private void ApplyTransitionLock()
        {
            bool isLocked = appState != null && appState.IsTransitioning;
            registeredPads.RemoveWhere(pad => pad == null);

            foreach (TeleportPad pad in registeredPads)
            {
                pad.SetTransitionLocked(isLocked);
            }
        }
    }
}

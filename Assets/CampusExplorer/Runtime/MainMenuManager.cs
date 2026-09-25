using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace CampusExplorer
{
    /// <summary>Small, stationary world-space launch menu. The villa owns its own XR rig.</summary>
    public sealed class MainMenuManager : MonoBehaviour
    {
        const string TourSceneName = "ModernVillaTour";

        [SerializeField] GameObject canvasRoot;
        [SerializeField] GameObject mainButtonsRoot;
        [SerializeField] GameObject controlsPanel;
        [SerializeField] GameObject controlsButtonsPanel;
        [SerializeField] GameObject comfortPanel;
        [SerializeField] GameObject creditsPanel;
        [SerializeField] Text comfortStatusText;
        bool awaitingPhysicalHeadset;

        public GameObject CanvasRoot => canvasRoot;
        public GameObject MainButtonsRoot => mainButtonsRoot;
        public GameObject ControlsPanel => controlsPanel;
        public GameObject ControlsButtonsPanel => controlsButtonsPanel;
        public GameObject ComfortPanel => comfortPanel;
        public GameObject CreditsPanel => creditsPanel;
        public Text ComfortStatusText => comfortStatusText;
        public bool IsLaunching { get; private set; }
        public float SelectedSnapAngle { get; private set; } = 45f;
        public bool UseContinuousMovement { get; private set; } = true;

        public void Configure(GameObject canvas, GameObject buttons, GameObject controls,
            GameObject controlsButtons,
            GameObject comfort, GameObject credits, Text comfortStatus)
        {
            canvasRoot = canvas;
            mainButtonsRoot = buttons;
            controlsPanel = controls;
            controlsButtonsPanel = controlsButtons;
            comfortPanel = comfort;
            creditsPanel = credits;
            comfortStatusText = comfortStatus;
        }

        void Awake()
        {
            awaitingPhysicalHeadset = !Application.isEditor;
            Back();
            UpdateComfortStatus();
        }

        IEnumerator Start()
        {
            // Give the XR runtime a frame to supply a tracked head pose before placing UI.
            yield return null;
            yield return null;
            Recenter();
        }

        void LateUpdate()
        {
            if (!awaitingPhysicalHeadset)
                return;
            var headsets = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeadMounted | InputDeviceCharacteristics.TrackedDevice, headsets);
            foreach (var headset in headsets)
            {
                if (!headset.isValid || !headset.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var tracked) || !tracked)
                    continue;
                // The first physical pose can arrive after the scene's first frames.
                Recenter();
                awaitingPhysicalHeadset = false;
                break;
            }
        }

        public void Recenter()
        {
            var camera = Camera.main;
            if (canvasRoot == null || camera == null)
                return;

            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = camera.transform.parent != null ? camera.transform.parent.forward : Vector3.forward;
            forward.y = 0f;
            forward.Normalize();
            canvasRoot.transform.SetPositionAndRotation(
                camera.transform.position + forward * 1.75f + Vector3.down * 0.12f,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        public void OpenControls() => ShowPanel(controlsPanel);
        public void OpenControlsButtons() => ShowPanel(controlsButtonsPanel);
        public void OpenComfort() => ShowPanel(comfortPanel);
        public void OpenCredits() => ShowPanel(creditsPanel);
        public void Back() => ShowPanel(null);

        void ShowPanel(GameObject selected)
        {
            if (mainButtonsRoot != null) mainButtonsRoot.SetActive(selected == null);
            if (controlsPanel != null) controlsPanel.SetActive(selected == controlsPanel);
            if (controlsButtonsPanel != null) controlsButtonsPanel.SetActive(selected == controlsButtonsPanel);
            if (comfortPanel != null) comfortPanel.SetActive(selected == comfortPanel);
            if (creditsPanel != null) creditsPanel.SetActive(selected == creditsPanel);
        }

        public void SetSnap30()
        {
            SelectedSnapAngle = 30f;
            UpdateComfortStatus();
        }

        public void SetSnap45()
        {
            SelectedSnapAngle = 45f;
            UpdateComfortStatus();
        }

        public void SetTeleportMovement()
        {
            UseContinuousMovement = false;
            UpdateComfortStatus();
        }

        public void SetContinuousMovement()
        {
            UseContinuousMovement = true;
            UpdateComfortStatus();
        }

        void UpdateComfortStatus()
        {
            if (comfortStatusText != null)
                comfortStatusText.text = $"Turn: {SelectedSnapAngle:0}°   |   Move: " +
                    (UseContinuousMovement ? "Continuous" : "Teleportation");
        }

        public void StartFreeTour() => Launch(MainMenuLaunchMode.FreeTour);
        public void StartGuidedTour() => Launch(MainMenuLaunchMode.GuidedTour);

        void Launch(MainMenuLaunchMode mode)
        {
            if (IsLaunching)
                return;
            if (!Application.CanStreamedLevelBeLoaded(TourSceneName))
            {
                Debug.LogError("[Main Menu] ModernVillaTour is missing from Build Settings.", this);
                return;
            }

            IsLaunching = true;
            MainMenuLaunchState.Queue(mode, SelectedSnapAngle, UseContinuousMovement);
            var operation = SceneManager.LoadSceneAsync(TourSceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                MainMenuLaunchState.Clear();
                IsLaunching = false;
                Debug.LogError("[Main Menu] Could not start ModernVillaTour scene load.", this);
            }
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

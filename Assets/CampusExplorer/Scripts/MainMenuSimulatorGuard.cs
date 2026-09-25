using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CampusExplorer;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>Keeps the Editor simulator out of the Quest input stream.</summary>
[DefaultExecutionOrder(-32000)]
public sealed class MainMenuSimulatorGuard : MonoBehaviour
{
    [SerializeField] GameObject simulator;
    [SerializeField] XROrigin origin;
    [SerializeField] MainMenuManager menu;

#if UNITY_EDITOR
    const float StableHeadsetSeconds = 0.5f;
    const float LostHeadsetSeconds = 0.75f;
    float detectedSince = -1f;
    float lostSince = -1f;
    bool realHeadsetActive;
    bool transitioning;
    bool desktopSimulatorMode;
    static object TrackingFrameBoundary => Application.isBatchMode ? null : new WaitForEndOfFrame();
#endif

    public void Configure(GameObject simulatorObject, XROrigin xrOrigin, MainMenuManager mainMenu)
    {
        simulator = simulatorObject;
        origin = xrOrigin;
        menu = mainMenu;
    }

    void Awake()
    {
        if (simulator == null)
            return;
#if UNITY_EDITOR
        // Never let a synthetic HMD compete with a Quest Link session.
        // Desktop simulation is opt-in through the project's XR startup setting.
        var xrSettings = XRGeneralSettings.Instance;
        desktopSimulatorMode = xrSettings != null && !xrSettings.InitManagerOnStart;
        simulator.SetActive(desktopSimulatorMode);
#else
        simulator.SetActive(false);
#endif
    }

#if UNITY_EDITOR
    IEnumerator Start()
    {
        var deadline = Time.realtimeSinceStartup + 3f;
        while (!StableRealHeadset() && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (StableRealHeadset())
            yield return SwitchToHeadset();
    }

    void Update()
    {
        if (transitioning)
            return;

        if (!realHeadsetActive)
        {
            if (StableRealHeadset())
                StartCoroutine(SwitchToHeadset());
            return;
        }

        if (TrackedRealHeadset())
        {
            lostSince = -1f;
            return;
        }

        if (lostSince < 0f)
            lostSince = Time.realtimeSinceStartup;
        if (desktopSimulatorMode && Time.realtimeSinceStartup - lostSince >= LostHeadsetSeconds)
            StartCoroutine(SwitchToSimulator());
    }

    bool StableRealHeadset()
    {
        if (!TrackedRealHeadset())
        {
            detectedSince = -1f;
            return false;
        }
        if (detectedSince < 0f)
            detectedSince = Time.realtimeSinceStartup;
        return Time.realtimeSinceStartup - detectedSince >= StableHeadsetSeconds;
    }

    static bool TrackedRealHeadset()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        if (!displays.Any(display => display != null && display.running))
            return false;

        var headsets = new List<UnityEngine.XR.InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeadMounted | InputDeviceCharacteristics.TrackedDevice, headsets);
        return headsets.Any(device => device.isValid &&
            device.name.IndexOf("simulat", StringComparison.OrdinalIgnoreCase) < 0 &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var tracked) && tracked);
    }

    IEnumerator SwitchToHeadset()
    {
        transitioning = true;
        realHeadsetActive = true;
        lostSince = -1f;
        if (simulator != null)
            simulator.SetActive(false);
        yield return null;
        yield return TrackingFrameBoundary;
        RefreshControllerModality();
        yield return TrackingFrameBoundary;
        menu?.Recenter();
        transitioning = false;
        Debug.Log("[Main Menu] Real XR headset active; Editor simulator disabled.", this);
    }

    IEnumerator SwitchToSimulator()
    {
        transitioning = true;
        realHeadsetActive = false;
        detectedSince = -1f;
        lostSince = -1f;
        if (simulator != null)
            simulator.SetActive(true);
        yield return null;
        RefreshControllerModality();
        menu?.Recenter();
        transitioning = false;
        Debug.LogWarning("[Main Menu] XR headset lost; Editor simulator restored.", this);
    }

    void RefreshControllerModality()
    {
        var modality = origin != null ? origin.GetComponent<XRInputModalityManager>() : null;
        if (modality == null || !modality.isActiveAndEnabled)
            return;
        modality.enabled = false;
        modality.enabled = true;
        Debug.Log($"[Main Menu] Quest controller modality refreshed. " +
                  $"LeftActive={modality.leftController != null && modality.leftController.activeInHierarchy}, " +
                  $"RightActive={modality.rightController != null && modality.rightController.activeInHierarchy}.", this);
    }
#endif
}

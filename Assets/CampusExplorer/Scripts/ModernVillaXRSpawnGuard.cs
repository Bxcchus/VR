using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace CampusExplorer
{
    /// <summary>
    /// Keeps the XR Origin floor-relative without writing to the tracked camera transform.
    /// Quest uses Floor tracking. The Editor simulator uses Device tracking with a
    /// calculated offset because it does not supply a real standing headset height.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class ModernVillaXRSpawnGuard : MonoBehaviour
    {
        const string TourSceneName = "ModernVillaTour";
        const float VisibleFloorY = 64.87821f;
        const float SimulatorEyeHeight = 1.65f;
        // Small comfort correction for Quest Link's zero-height fallback.
        // This does not affect normal Floor tracking or standalone Quest builds.
        const float QuestLinkStandingEyeHeight = 1.75f;
        const float MinimumPlausibleStandingEyeHeight = 1.50f;
        const float MinimumCapsuleHeight = 1.0f;
        const float MaximumCapsuleHeight = 2.1f;
        const float NavigationSlopeLimit = 45f;
        const float NavigationStepOffset = 0.30f;
        const float NavigationSkinWidth = 0.025f;
#if UNITY_EDITOR
        const float RealHeadsetStableDuration = 0.50f;
        const float RealHeadsetLossDuration = 0.75f;
#endif

        static readonly Vector3 DefaultFloorPosition = new(-3.45f, VisibleFloorY, -23.05f);
        static readonly Vector3 KitchenFloorPosition = new(-2.50f, VisibleFloorY - 0.06f, -24.00f);
        static readonly Vector3 KitchenFloorSize = new(12.00f, 0.12f, 12.00f);
        static readonly Vector3 DefaultForward = Quaternion.Euler(0f, 128.8543f, 0f) * Vector3.forward;

        [SerializeField] Vector3 desiredFloorPosition = new(-3.45f, VisibleFloorY, -23.05f);
        [SerializeField] Vector3 desiredForward = new(0.778971f, 0f, -0.627060f);

        XROrigin origin;
        CharacterController characterController;
        GravityProvider gravityProvider;
        XRBodyTransformer bodyTransformer;
        BoxCollider floorCollider;
        bool spawnReady;
        public bool IsSpawnReady => spawnReady;
        bool useRealHeadsetTracking;
        bool useDeviceHeightFallback;
        bool headsetTransitionInProgress;
        bool? exposureEnabledForCurrentMode;
        float nextRecoveryTime;
#if UNITY_EDITOR
        [SerializeField] bool showDebugOverlay;
        float realHeadsetDetectedSince = -1f;
        float realHeadsetLostSince = -1f;
#endif

        // Batch Play Mode has no rendered end-of-frame event. A regular frame is
        // sufficient there because no physical HMD compositor is being sampled.
        static object TrackingFrameBoundary => Application.isBatchMode ? null : new WaitForEndOfFrame();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForExistingTourScene()
        {
            if (SceneManager.GetActiveScene().name != TourSceneName)
                return;

            EnsureKitchenSafetyFloor();
            var xrOrigin = Object.FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.GetComponent<ModernVillaXRSpawnGuard>() == null)
                xrOrigin.gameObject.AddComponent<ModernVillaXRSpawnGuard>();
        }

        public void Configure(Vector3 floorPosition, Vector3 forward)
        {
            desiredFloorPosition = floorPosition;
            desiredForward = forward.sqrMagnitude > 0.001f ? forward.normalized : DefaultForward;
        }

        IEnumerator Start()
        {
            origin = GetComponent<XROrigin>();
            if (origin == null)
                origin = Object.FindAnyObjectByType<XROrigin>();

            if (origin == null || origin.Camera == null)
            {
                Debug.LogError("[Modern Villa] XR floor setup failed: no XROrigin camera was found.", this);
                yield break;
            }

            characterController = origin.Origin.GetComponent<CharacterController>();
            ConfigureCharacterControllerNavigation();
            gravityProvider = origin.GetComponentInChildren<GravityProvider>(true);
            bodyTransformer = origin.GetComponentInChildren<XRBodyTransformer>(true);
            floorCollider = GameObject.Find("Teleportation Area - Kitchen")?.GetComponent<BoxCollider>();

#if UNITY_EDITOR
            // A running OpenXR display alone is not enough: Meta Link can create a
            // display and immediately abort the session before any tracked HMD is
            // available. Require a real (non-simulated) tracked device to remain
            // stable before disabling the desktop simulator.
            var headsetDeadline = Time.realtimeSinceStartup + 3f;
            while (!HasStableTrackedRealHeadset() && Time.realtimeSinceStartup < headsetDeadline)
                yield return null;
            useRealHeadsetTracking = HasStableTrackedRealHeadset();
#else
            useRealHeadsetTracking = HasRunningXrDisplay();
#endif

#if UNITY_EDITOR
            // The XRI simulator registers its own synthetic HMD and controllers.
            // Leaving those devices enabled during Quest Link makes the tracked
            // pose nondeterministic: the camera can bind to the simulated HMD at
            // floor level and its rotation can fight the physical headset.
            if (useRealHeadsetTracking)
            {
                DisableEditorSimulator();
                yield return null;
                yield return TrackingFrameBoundary;
                RefreshControllerModalityAfterSimulatorHandoff();
            }
#endif

            ConfigureTrackingSpace();

            // The simulator applies its own offsets during OnEnable. Restore the
            // floor-relative hierarchy after its first tracked pose has arrived.
            yield return null;
            yield return TrackingFrameBoundary;
            ConfigureTrackingSpace();

#if UNITY_EDITOR
            // Some Quest Link sessions report Floor as the active tracking mode
            // while returning a zero-height head pose. Wait briefly for a valid
            // physical pose, then use Device tracking with an explicit eye-height
            // offset only for that broken Link session.
            if (useRealHeadsetTracking)
            {
                var floorPoseDeadline = Time.realtimeSinceStartup + 1.5f;
                while (origin.CameraInOriginSpacePos.y < 0.5f && Time.realtimeSinceStartup < floorPoseDeadline)
                    yield return null;

                if (origin.CameraInOriginSpacePos.y < MinimumPlausibleStandingEyeHeight)
                {
                    useDeviceHeightFallback = true;
                    ConfigureTrackingSpace();
                    yield return null;
                    yield return TrackingFrameBoundary;
                    CorrectQuestLinkStandingHeight();
                    yield return TrackingFrameBoundary;
                    Debug.LogWarning(
                        "[Modern Villa] Quest Link returned a low Floor pose; " +
                        $"using Device tracking with a {QuestLinkStandingEyeHeight:F2} m standing eye-height calibration for this session.", this);
                }
            }
#endif

            yield return TrackingFrameBoundary;
            AlignTrackedHeadToSpawn();
            yield return TrackingFrameBoundary;
            UpdateCapsuleFromTrackedHead();

            spawnReady = true;
            LogFloorDiagnostics("ready");
            LogVisibilityDiagnostics();
        }

        void ConfigureTrackingSpace()
        {
            EnsureKitchenSafetyFloor();
            ConfigureExposureForCurrentRenderMode();

            var originTransform = origin.Origin.transform;
            var forward = desiredForward.sqrMagnitude > 0.001f ? desiredForward.normalized : DefaultForward;
            originTransform.SetPositionAndRotation(desiredFloorPosition, Quaternion.LookRotation(forward, Vector3.up));

            if ((useRealHeadsetTracking && !useDeviceHeightFallback) || !Application.isEditor)
            {
                // Quest standalone and Quest Link both supply the real headset
                // height relative to the guardian floor.
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                origin.CameraYOffset = 0f;
                SetCameraFloorOffsetHeight(0f);
            }
            else
            {
                // A desktop simulator has no physical standing height. Keep that
                // artificial height on the Camera Offset object, never on Main Camera.
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                var trackedLocalY = Mathf.Max(0f, origin.Camera.transform.localPosition.y);
                var targetEyeHeight = useRealHeadsetTracking && useDeviceHeightFallback
                    ? QuestLinkStandingEyeHeight
                    : SimulatorEyeHeight;
                var simulatorOffset = Mathf.Max(0f, targetEyeHeight - trackedLocalY);
                origin.CameraYOffset = simulatorOffset;
                SetCameraFloorOffsetHeight(simulatorOffset);
            }

            if (!Application.isEditor)
            {
                // Reassert the standalone Quest contract even if the display
                // subsystem is still starting during the first frame.
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                origin.CameraYOffset = 0f;
                SetCameraFloorOffsetHeight(0f);
            }

            if (bodyTransformer != null)
                bodyTransformer.useCharacterControllerIfExists = true;

            gravityProvider?.ResetFallForce();
            Physics.SyncTransforms();
        }

        void ConfigureExposureForCurrentRenderMode()
        {
            if (origin == null || origin.Camera == null)
                return;

            var exposure = origin.Camera.GetComponent<ModernVillaExposure>();
            if (exposure == null)
                return;

            // The legacy OnRenderImage exposure shader samples a regular 2D
            // texture. OpenXR Single Pass Instanced supplies a stereo texture
            // array, which turns the headset output into a uniform white frame.
            // Keep the authored effect for desktop review and let the XR camera
            // render the original scene directly on Quest/Quest Link.
            var shouldEnable = Application.isEditor && !useRealHeadsetTracking;
            exposure.enabled = shouldEnable;

            if (exposureEnabledForCurrentMode == shouldEnable)
                return;

            exposureEnabledForCurrentMode = shouldEnable;
            Debug.Log(
                shouldEnable
                    ? "[Modern Villa] Desktop exposure pass enabled."
                    : "[Modern Villa] XR-incompatible exposure pass disabled; rendering the villa directly.",
                this);
        }

        static bool HasRunningXrDisplay()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.Any(display => display != null && display.running);
        }

#if UNITY_EDITOR
        static bool HasTrackedRealHeadset()
        {
            if (!HasRunningXrDisplay())
                return false;

            var headsets = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeadMounted | InputDeviceCharacteristics.TrackedDevice,
                headsets);

            return headsets.Any(device =>
            {
                if (!device.isValid || device.name.IndexOf("simulat", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var isTracked) && isTracked;
            });
        }

        bool HasStableTrackedRealHeadset()
        {
            if (!HasTrackedRealHeadset())
            {
                realHeadsetDetectedSince = -1f;
                return false;
            }

            if (realHeadsetDetectedSince < 0f)
                realHeadsetDetectedSince = Time.realtimeSinceStartup;

            return Time.realtimeSinceStartup - realHeadsetDetectedSince >= RealHeadsetStableDuration;
        }

        static GameObject FindEditorSimulator()
        {
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "XR Interaction Simulator (Editor)")?.gameObject;
        }

        static void DisableEditorSimulator()
        {
            var simulator = FindEditorSimulator();
            if (simulator == null || !simulator.activeSelf)
                return;

            simulator.SetActive(false);
            Debug.Log("[Modern Villa] XR Interaction Simulator disabled because a real OpenXR headset is running.");
        }

        static void EnableEditorSimulator()
        {
            var simulator = FindEditorSimulator();
            if (simulator == null || simulator.activeSelf)
                return;

            simulator.SetActive(true);
            Debug.LogWarning("[Modern Villa] OpenXR headset session was lost; the desktop XR simulator was restored.");
        }

        void RefreshControllerModalityAfterSimulatorHandoff()
        {
            // XRI sets the entire Controller GameObject inactive when a simulated
            // controller is removed. If the real Quest controller was registered
            // before that removal, XRI's disconnect callback does not rescan it.
            // Re-enabling this manager makes it inspect the already tracked real
            // devices and restore the original model and interactors for each hand.
            var manager = origin.GetComponent<XRInputModalityManager>();
            if (manager == null || !manager.isActiveAndEnabled)
                return;

            manager.enabled = false;
            manager.enabled = true;
            Debug.Log($"[Modern Villa] XR controller modality refreshed after simulator handoff. " +
                      $"LeftActive={manager.leftController != null && manager.leftController.activeInHierarchy}, " +
                      $"RightActive={manager.rightController != null && manager.rightController.activeInHierarchy}.", this);
        }
#endif

        void AlignTrackedHeadToSpawn()
        {
            var cameraTransform = origin.Camera.transform;
            var targetForward = Vector3.ProjectOnPlane(desiredForward, Vector3.up).normalized;
            var currentForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;

            // Rotate the origin around the tracked camera. The HMD remains the
            // authoritative pose while the virtual room receives a stable heading.
            if (targetForward.sqrMagnitude > 0.001f && currentForward.sqrMagnitude > 0.001f)
                origin.MatchOriginUpCameraForward(Vector3.up, targetForward);

            // Room-scale tracking can report several metres of physical X/Z offset.
            // Move the origin so the tracked head, rather than the origin pivot,
            // starts at the intended interior point.
            var cameraWorld = cameraTransform.position;
            origin.MoveCameraToWorldLocation(new Vector3(
                desiredFloorPosition.x,
                cameraWorld.y,
                desiredFloorPosition.z));

            var originPosition = origin.Origin.transform.position;
            originPosition.y = desiredFloorPosition.y;
            origin.Origin.transform.position = originPosition;
            Physics.SyncTransforms();
        }

        void SetCameraFloorOffsetHeight(float height)
        {
            if (origin.CameraFloorOffsetObject == null)
                return;

            var offsetTransform = origin.CameraFloorOffsetObject.transform;
            offsetTransform.localPosition = new Vector3(0f, height, 0f);
        }

        void CorrectQuestLinkStandingHeight()
        {
#if UNITY_EDITOR
            if (!useRealHeadsetTracking || !useDeviceHeightFallback)
                return;

            // OpenXR may update the physical head pose only after Device mode has
            // taken effect. Correct from the resulting camera height instead of
            // subtracting a stale pre-transition local pose.
            var currentEyeHeight = origin.CameraInOriginSpacePos.y;
            var correctedOffset = Mathf.Clamp(
                origin.CameraYOffset + (QuestLinkStandingEyeHeight - currentEyeHeight),
                0f,
                MaximumCapsuleHeight);

            origin.CameraYOffset = correctedOffset;
            SetCameraFloorOffsetHeight(correctedOffset);
            Physics.SyncTransforms();

            Debug.Log(
                $"[Modern Villa] Quest Link standing height corrected. " +
                $"Measured={currentEyeHeight:F3}m, Target={QuestLinkStandingEyeHeight:F3}m, " +
                $"CameraYOffset={correctedOffset:F3}m.", this);
#endif
        }

        void UpdateCapsuleFromTrackedHead()
        {
            if (characterController == null)
                return;

            var cameraLocal = origin.CameraInOriginSpacePos;
            var height = Mathf.Clamp(cameraLocal.y, MinimumCapsuleHeight, MaximumCapsuleHeight);
            characterController.height = height;
            characterController.center = new Vector3(cameraLocal.x, height * 0.5f, cameraLocal.z);
        }

        void ConfigureCharacterControllerNavigation()
        {
            if (characterController == null)
                return;

            characterController.slopeLimit = NavigationSlopeLimit;
            characterController.stepOffset = NavigationStepOffset;
            characterController.skinWidth = NavigationSkinWidth;
            characterController.minMoveDistance = 0f;
        }

        static void EnsureKitchenSafetyFloor()
        {
            var area = GameObject.Find("Teleportation Area - Kitchen");
            if (area == null)
            {
                area = new GameObject("Teleportation Area - Kitchen");
                Debug.LogWarning("[Modern Villa] The teleportation area was missing; a physics safety floor was created.");
            }

            area.transform.SetPositionAndRotation(KitchenFloorPosition, Quaternion.identity);
            area.transform.localScale = Vector3.one;
            var collider = area.GetComponent<BoxCollider>();
            if (collider == null)
                collider = area.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = KitchenFloorSize;
            collider.isTrigger = false;
            collider.enabled = true;
            Physics.SyncTransforms();
        }

        void LateUpdate()
        {
#if UNITY_EDITOR
            if (Keyboard.current?.f8Key.wasPressedThisFrame == true)
                showDebugOverlay = !showDebugOverlay;
#endif
            if (!spawnReady || origin == null || origin.Camera == null)
                return;

#if UNITY_EDITOR
            // Quest Link can become ready after Play has already started. Switch
            // away from the simulator as soon as the physical display appears.
            var hasTrackedRealHeadset = HasTrackedRealHeadset();
            if (!useRealHeadsetTracking && !headsetTransitionInProgress && HasStableTrackedRealHeadset())
            {
                StartCoroutine(TransitionToLateQuestLinkSession());
                return;
            }

            if (useRealHeadsetTracking)
            {
                if (hasTrackedRealHeadset)
                {
                    realHeadsetLostSince = -1f;
                }
                else
                {
                    if (realHeadsetLostSince < 0f)
                        realHeadsetLostSince = Time.realtimeSinceStartup;

                    if (!headsetTransitionInProgress &&
                        Time.realtimeSinceStartup - realHeadsetLostSince >= RealHeadsetLossDuration)
                    {
                        RestoreEditorSimulatorAfterHeadsetLoss();
                        return;
                    }
                }
            }
#endif

            if (origin.Camera.transform.position.y < VisibleFloorY - 0.5f && Time.unscaledTime >= nextRecoveryTime)
            {
                nextRecoveryTime = Time.unscaledTime + 1f;
                ConfigureTrackingSpace();
                AlignTrackedHeadToSpawn();
                UpdateCapsuleFromTrackedHead();
                Debug.LogWarning("[Modern Villa] XR Origin recovered after moving below the floor.", this);
                LogFloorDiagnostics("recovered");
            }
        }

#if UNITY_EDITOR
        void RestoreEditorSimulatorAfterHeadsetLoss()
        {
            useRealHeadsetTracking = false;
            useDeviceHeightFallback = false;
            realHeadsetDetectedSince = -1f;
            realHeadsetLostSince = -1f;
            EnableEditorSimulator();
            ConfigureTrackingSpace();
            AlignTrackedHeadToSpawn();
            UpdateCapsuleFromTrackedHead();
            LogFloorDiagnostics("desktop simulator restored");
        }

        IEnumerator TransitionToLateQuestLinkSession()
        {
            headsetTransitionInProgress = true;
            useRealHeadsetTracking = true;
            useDeviceHeightFallback = false;
            DisableEditorSimulator();
            yield return null;
            yield return TrackingFrameBoundary;
            RefreshControllerModalityAfterSimulatorHandoff();

            ConfigureTrackingSpace();
            var floorPoseDeadline = Time.realtimeSinceStartup + 1.5f;
            while (origin.CameraInOriginSpacePos.y < 0.5f && Time.realtimeSinceStartup < floorPoseDeadline)
                yield return null;

            if (origin.CameraInOriginSpacePos.y < MinimumPlausibleStandingEyeHeight)
            {
                useDeviceHeightFallback = true;
                ConfigureTrackingSpace();
                yield return null;
                yield return TrackingFrameBoundary;
                CorrectQuestLinkStandingHeight();
                yield return TrackingFrameBoundary;
                Debug.LogWarning(
                    "[Modern Villa] Late Quest Link connection returned a low Floor pose; " +
                    $"using Device tracking with a {QuestLinkStandingEyeHeight:F2} m standing eye-height calibration.", this);
            }

            AlignTrackedHeadToSpawn();
            UpdateCapsuleFromTrackedHead();
            LogFloorDiagnostics("Quest Link connected");
            headsetTransitionInProgress = false;
        }
#endif

        void LogFloorDiagnostics(string phase)
        {
            var originY = origin.Origin.transform.position.y;
            var cameraY = origin.Camera.transform.position.y;
            var colliderY = floorCollider != null ? floorCollider.bounds.max.y : float.NaN;
            var capsuleBottomY = characterController != null
                ? origin.Origin.transform.TransformPoint(characterController.center + Vector3.down * characterController.height * 0.5f).y
                : float.NaN;

            Debug.Log($"[Modern Villa] XR floor {phase}. VisibleFloorY={VisibleFloorY:F3}, ColliderTopY={colliderY:F3}, " +
                      $"Origin={origin.Origin.transform.position:F3}, Camera={origin.Camera.transform.position:F3}, " +
                      $"OriginY={originY:F3}, CameraY={cameraY:F3}, CapsuleBottomY={capsuleBottomY:F3}, " +
                      $"CapsuleCenter={characterController?.center.ToString("F3") ?? "none"}, " +
                      $"CapsuleHeight={(characterController != null ? characterController.height : -1f):F3}, " +
                      $"RequestedMode={origin.RequestedTrackingOriginMode}, CurrentMode={origin.CurrentTrackingOriginMode}, " +
                      $"CameraYOffset={origin.CameraYOffset:F3}, " +
                      $"RealHeadset={useRealHeadsetTracking}, DeviceHeightFallback={useDeviceHeightFallback}", this);
        }

        void LogVisibilityDiagnostics()
        {
            var camera = origin.Camera;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)
                .Where(item => !item.transform.IsChildOf(origin.transform))
                .ToArray();
            var visible = renderers.Count(item =>
                item.enabled && !item.forceRenderingOff &&
                (camera.cullingMask & (1 << item.gameObject.layer)) != 0 &&
                GeometryUtility.TestPlanesAABB(planes, item.bounds));

            Debug.Log($"[Modern Villa] XR visibility. Forward={camera.transform.forward:F2}, " +
                      $"Renderers={renderers.Length}, FrustumVisible={visible}", this);
        }

#if UNITY_EDITOR
        void OnGUI()
        {
            if (!showDebugOverlay || !spawnReady || origin == null || origin.Camera == null)
                return;

            var colliderY = floorCollider != null ? floorCollider.bounds.max.y : float.NaN;
            var capsuleBottomY = characterController != null
                ? origin.Origin.transform.TransformPoint(characterController.center + Vector3.down * characterController.height * 0.5f).y
                : float.NaN;
            var capsuleCenter = characterController != null ? characterController.center.ToString("F3") : "none";
            var capsuleHeight = characterController != null ? characterController.height : -1f;

            var text =
                "XR FLOOR DEBUG\n" +
                $"Visible floor Y     {VisibleFloorY:F3}\n" +
                $"Collider top Y      {colliderY:F3}\n" +
                $"XR Origin           {origin.Origin.transform.position:F3}\n" +
                $"Camera world        {origin.Camera.transform.position:F3}\n" +
                $"Capsule bottom Y    {capsuleBottomY:F3}\n" +
                $"Capsule center      {capsuleCenter}\n" +
                $"Capsule height      {capsuleHeight:F3}\n" +
                $"Tracking mode       {origin.RequestedTrackingOriginMode}/{origin.CurrentTrackingOriginMode} ({(useRealHeadsetTracking ? "HMD" : "Simulator")})\n" +
                $"Height fallback     {useDeviceHeightFallback}\n" +
                $"Camera Y offset     {origin.CameraYOffset:F3}";

            GUI.Box(new Rect(18f, 18f, 390f, 230f), text);
        }
#endif

        void Reset()
        {
            desiredFloorPosition = DefaultFloorPosition;
            desiredForward = DefaultForward;
        }
    }
}

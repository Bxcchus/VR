using System.Collections;
using CampusExplorer;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>Applies a menu choice to the existing villa rig after its spawn calibration.</summary>
public sealed class MainMenuLaunchReceiver : MonoBehaviour
{
    MainMenuLaunchRequest request;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLaunchState()
    {
        MainMenuLaunchState.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Subscribe()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "ModernVillaTour" || !MainMenuLaunchState.TryConsume(out var launch))
            return;

        var host = new GameObject("Main Menu Launch Handoff");
        SceneManager.MoveGameObjectToScene(host, scene);
        var receiver = host.AddComponent<MainMenuLaunchReceiver>();
        receiver.request = launch;
        receiver.StartCoroutine(receiver.ApplyAfterSpawn());
    }

    IEnumerator ApplyAfterSpawn()
    {
        // The tour rig has its own calibration coroutine. Never move the HMD or XR Origin here.
        yield return null;
        var guard = FindAnyObjectByType<CampusExplorer.ModernVillaXRSpawnGuard>();
        var deadline = Time.realtimeSinceStartup + 15f;
        while (guard != null && !guard.IsSpawnReady && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (guard != null && !guard.IsSpawnReady)
        {
            Debug.LogError("[Main Menu] Villa spawn calibration did not complete; launch handoff stopped.");
            Destroy(gameObject);
            yield break;
        }

        var origin = FindAnyObjectByType<XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[Main Menu] Villa XR Origin was not found after scene load.");
            Destroy(gameObject);
            yield break;
        }

        foreach (var turn in origin.GetComponentsInChildren<SnapTurnProvider>(true))
            turn.turnAmount = request.SnapAngle;

        var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move != null)
            move.enabled = request.ContinuousMovement;

        foreach (var controller in origin.GetComponentsInChildren<ControllerInputActionManager>(true))
        {
            var isLeft = controller.transform.name.Contains("Left");
            controller.smoothMotionEnabled = request.ContinuousMovement && isLeft;
        }

        if (request.Mode == MainMenuLaunchMode.GuidedTour)
        {
            var tour = FindAnyObjectByType<XRGuidedTour>();
            if (tour != null)
                tour.StartTour();
            else
                Debug.LogError("[Main Menu] Existing XRGuidedTour was not found.");
        }

        Debug.Log($"[Main Menu] {request.Mode} launched; snap {request.SnapAngle:0}°, " +
            $"movement {(request.ContinuousMovement ? "continuous" : "teleportation")}.");
        Destroy(gameObject);
    }
}

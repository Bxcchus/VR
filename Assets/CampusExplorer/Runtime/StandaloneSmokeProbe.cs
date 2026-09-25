using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer
{
    public sealed class StandaloneSmokeProbe : MonoBehaviour
    {
        void Awake()
        {
            if (Environment.GetCommandLineArgs().Contains("-campusSmoke"))
                StartCoroutine(ValidateAndExit());
        }

        IEnumerator ValidateAndExit()
        {
            yield return null;
            yield return null;

            var valid = UnityEngine.Object.FindAnyObjectByType<XRInteractionSimulator>() != null
                && InputSystem.devices.OfType<XRHMD>().Any()
                && InputSystem.devices.OfType<XRController>().Any()
                && UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>() != null
                && UnityEngine.Object.FindAnyObjectByType<PointOfInterest>() != null
                && UnityEngine.Object.FindAnyObjectByType<InfoPanel>(FindObjectsInactive.Include) != null;

            if (valid)
                Debug.Log("[Campus Explorer] STANDALONE SMOKE PASS - simulator devices, teleportation, POI and panel are initialized.");
            else
                Debug.LogError("[Campus Explorer] STANDALONE SMOKE FAIL - one or more Increment A systems did not initialize.");

            Application.Quit(valid ? 0 : 2);
        }
    }
}

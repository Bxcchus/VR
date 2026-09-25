using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Tests
{
    public sealed class IncrementAPlayModeTests
    {
        [UnityTest]
        public IEnumerator SceneStartsSimulatorAndSupportsCoreInteractions()
        {
            yield return SceneManager.LoadSceneAsync("IncrementA", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var simulator = Object.FindAnyObjectByType<XRInteractionSimulator>();
            Assert.That(simulator, Is.Not.Null, "The official XRI simulator must be present.");
            Assert.That(simulator.isActiveAndEnabled, Is.True, "The XRI simulator must be active in Play Mode.");
            Assert.That(InputSystem.devices.OfType<XRHMD>().Any(), Is.True, "The simulator must create a tracked HMD input device.");
            Assert.That(InputSystem.devices.OfType<XRController>().Any(), Is.True, "The simulator must create at least one XR controller input device.");

            var point = Object.FindAnyObjectByType<PointOfInterest>();
            var panel = Object.FindAnyObjectByType<InfoPanel>(FindObjectsInactive.Include);
            Assert.That(point, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.IsOpen, Is.False);

            point.GetComponent<XRSimpleInteractable>().selectEntered.Invoke(null);
            yield return null;
            Assert.That(panel.IsOpen, Is.True, "Selecting the point of interest must open its panel.");

            point.GetComponent<XRSimpleInteractable>().selectEntered.Invoke(null);
            yield return null;
            Assert.That(panel.IsOpen, Is.False, "Selecting the point again must close its panel.");

            var provider = Object.FindAnyObjectByType<TeleportationProvider>();
            var origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.That(provider, Is.Not.Null, "The XR rig must expose a teleportation provider.");
            Assert.That(origin, Is.Not.Null);
            var startingPosition = origin.transform.position;
            var queued = provider.QueueTeleportRequest(new TeleportRequest
            {
                destinationPosition = new Vector3(0f, 0f, -2.35f),
                destinationRotation = Quaternion.identity,
                matchOrientation = MatchOrientation.WorldSpaceUp,
                requestTime = Time.time,
            });
            Assert.That(queued, Is.True);
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(origin.transform.position, startingPosition), Is.GreaterThan(1f),
                "The locomotion pipeline must move the XR Origin after a teleport request.");
        }
    }
}

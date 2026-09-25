using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Tests
{
    public sealed class CampusTourPlayModeTests
    {
        [UnityTest]
        public IEnumerator FourSpacesSupportACompleteTour()
        {
            yield return SceneManager.LoadSceneAsync("CampusTour", LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.That(Object.FindAnyObjectByType<XRInteractionSimulator>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).Length, Is.EqualTo(4));

            var points = Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include)
                .OrderBy(point => point.PointId)
                .ToArray();
            Assert.That(points.Length, Is.EqualTo(4));
            Assert.That(points.Select(point => point.PointId).Distinct().Count(), Is.EqualTo(4));

            var progress = Object.FindAnyObjectByType<TourProgress>();
            Assert.That(progress, Is.Not.Null);
            Assert.That(progress.VisitedCount, Is.Zero);
            Assert.That(progress.IsComplete, Is.False);

            foreach (var point in points)
            {
                point.GetComponent<XRSimpleInteractable>().selectEntered.Invoke(null);
                yield return null;
            }

            Assert.That(progress.VisitedCount, Is.EqualTo(4));
            Assert.That(progress.IsComplete, Is.True);

            points[0].GetComponent<XRSimpleInteractable>().selectEntered.Invoke(null);
            yield return null;
            Assert.That(progress.VisitedCount, Is.EqualTo(4), "Revisiting a point must not increase progress.");
        }

        [UnityTest]
        public IEnumerator NotreDameSceneSupportsSimulatorAndCompleteTour()
        {
            yield return SceneManager.LoadSceneAsync("NotreDameTour", LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.That(Object.FindAnyObjectByType<XRInteractionSimulator>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).Length, Is.EqualTo(4));

            var originalModel = GameObject.Find("Notre-Dame - Original GLB - Raiz - CC BY 4.0");
            Assert.That(originalModel, Is.Not.Null);
            Assert.That(originalModel.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(4));
            Assert.That(originalModel.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .All(material => material != null && material.mainTexture != null), Is.True);
            Assert.That(GameObject.Find("Notre-Dame - Reconstructed XR Nave"), Is.Null);
            var collisionRoot = GameObject.Find("Notre-Dame Navigation Collision");
            Assert.That(collisionRoot, Is.Not.Null);
            Assert.That(collisionRoot.GetComponentsInChildren<Renderer>(true), Is.Empty);

            var points = Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include)
                .OrderBy(point => point.PointId)
                .ToArray();
            Assert.That(points.Select(point => point.PointId), Is.EquivalentTo(new[]
            {
                "entree-nef", "arcades", "transept", "choeur"
            }));

            var progress = Object.FindAnyObjectByType<TourProgress>();
            Assert.That(progress, Is.Not.Null);
            foreach (var point in points)
            {
                point.GetComponent<XRSimpleInteractable>().selectEntered.Invoke(null);
                yield return null;
            }

            Assert.That(progress.VisitedCount, Is.EqualTo(4));
            Assert.That(progress.IsComplete, Is.True);
        }
    }
}

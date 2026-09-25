using System.Collections;
using System.Linq;
using CampusExplorer;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;

namespace CampusExplorer.Tests.PlayMode
{
    public sealed class MainMenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator MainMenuHasOneXRRigAndWorkingExclusivePanels()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
            yield return null;

            var manager = Object.FindAnyObjectByType<MainMenuManager>();
            Assert.That(manager, Is.Not.Null, "MainMenu must own a menu manager.");
            Assert.That(SceneUtility.GetScenePathByBuildIndex(0), Does.EndWith("/MainMenu.unity"));
            Assert.That(SceneUtility.GetScenePathByBuildIndex(1), Does.EndWith("/ModernVillaTour.unity"));
            Assert.That(Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRInteractionManager>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include), Has.Length.EqualTo(2),
                "The menu must reuse both Quest Touch ray interactors.");
            var simulator = manager.transform.root.Find("XR Interaction Simulator (Editor)");
            Assert.That(simulator, Is.Not.Null);
            if (XRGeneralSettings.Instance != null)
                Assert.That(simulator.gameObject.activeSelf,
                    Is.EqualTo(!XRGeneralSettings.Instance.InitManagerOnStart),
                    "The synthetic HMD must run only in explicit desktop simulator mode.");
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(EventSystem.current, Is.Not.Null);
            Assert.That(EventSystem.current.GetComponent<XRUIInputModule>(), Is.Not.Null,
                "Quest Touch trigger clicks require XRUIInputModule.");

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            var menuCanvas = canvases.SingleOrDefault(canvas =>
                canvas.renderMode == RenderMode.WorldSpace &&
                canvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null);
            Assert.That(menuCanvas, Is.Not.Null,
                "The main menu needs a raycastable world-space Canvas.");
            Assert.That(menuCanvas.GetComponentsInChildren<Collider>(true), Is.Empty,
                "UI should never obstruct the XR CharacterController.");
            Assert.That(manager.MainButtonsRoot.activeSelf, Is.True);
            Assert.That(manager.ControlsPanel.activeSelf || manager.ControlsButtonsPanel.activeSelf ||
                        manager.ComfortPanel.activeSelf ||
                        manager.CreditsPanel.activeSelf, Is.False);

            foreach (var label in new[]
                     {
                         "START FREE TOUR", "GUIDED TOUR", "CONTROLS", "COMFORT", "CREDITS", "QUIT"
                     })
            {
                var button = FindButton(menuCanvas, label);
                Assert.That(button, Is.Not.Null, $"Missing main button {label}.");
                Assert.That(button.interactable, Is.True, $"Main button {label} must accept Quest UI clicks.");
                Assert.That(button.onClick.GetPersistentEventCount(), Is.GreaterThan(0),
                    $"Main button {label} has no serialized callback.");
            }

            var controls = manager.ControlsPanel;
            var controlsButtons = manager.ControlsButtonsPanel;
            var comfort = manager.ComfortPanel;
            var credits = manager.CreditsPanel;
            Assert.That(controls, Is.Not.Null);
            Assert.That(controlsButtons, Is.Not.Null);
            Assert.That(comfort, Is.Not.Null);
            Assert.That(credits, Is.Not.Null);
            Assert.That(controls.activeSelf || controlsButtons.activeSelf || comfort.activeSelf ||
                        credits.activeSelf, Is.False,
                "Secondary panels must start closed.");

            FindButton(menuCanvas, "CONTROLS").onClick.Invoke();
            yield return null;
            AssertExclusive(controls, controlsButtons, comfort, credits, controls);
            Assert.That(manager.MainButtonsRoot.activeSelf, Is.False);
            Assert.That(FindButton(controls.transform, "BACK"), Is.Not.Null);
            var controlsCopy = AllText(controls.transform);
            foreach (var phrase in new[] { "LEFT STICK", "RIGHT STICK", "INDEX TRIGGER", "MENU BUTTON", "TELEPORT" })
                Assert.That(controlsCopy, Does.Contain(phrase));
            var nextPage = FindButton(controls.transform, "QUEST TOUCH BUTTONS  >");
            Assert.That(nextPage, Is.Not.Null);
            Assert.That(nextPage.onClick.GetPersistentEventCount(), Is.GreaterThan(0));
            nextPage.onClick.Invoke();
            yield return null;
            AssertExclusive(controls, controlsButtons, comfort, credits, controlsButtons);
            var buttonsCopy = AllText(controlsButtons.transform);
            foreach (var phrase in new[]
                     {
                         "LEFT CONTROLLER", "RIGHT CONTROLLER", "GRIP", "INDEX TRIGGER",
                         "A  JUMP", "B  NO VILLA ACTION", "X / Y  NO VILLA ACTION",
                         "MENU  OPEN"
                     })
                Assert.That(buttonsCopy, Does.Contain(phrase), $"Controls page must explain {phrase}.");
            var previousPage = FindButton(controlsButtons.transform, "<  BASICS  1 / 2");
            Assert.That(previousPage, Is.Not.Null);
            Assert.That(previousPage.onClick.GetPersistentEventCount(), Is.GreaterThan(0));
            previousPage.onClick.Invoke();
            yield return null;
            AssertExclusive(controls, controlsButtons, comfort, credits, controls);
            FindButton(controls.transform, "QUEST TOUCH BUTTONS  >").onClick.Invoke();
            FindButton(controlsButtons.transform, "BACK").onClick.Invoke();
            yield return null;
            Assert.That(manager.MainButtonsRoot.activeSelf, Is.True,
                "Back on the button guide must return to the main menu.");
            AssertExclusive(controls, controlsButtons, comfort, credits, null);
            manager.OpenComfort();
            yield return null;
            AssertExclusive(controls, controlsButtons, comfort, credits, comfort);
            Assert.That(FindButton(comfort.transform, "BACK"), Is.Not.Null);

            FindButton(comfort.transform, "SNAP TURN 30°").onClick.Invoke();
            Assert.That(manager.SelectedSnapAngle, Is.EqualTo(30f).Within(0.01f));
            FindButton(comfort.transform, "SNAP TURN 45°").onClick.Invoke();
            Assert.That(manager.SelectedSnapAngle, Is.EqualTo(45f).Within(0.01f));
            FindButton(comfort.transform, "TELEPORTATION / COMFORT").onClick.Invoke();
            Assert.That(manager.UseContinuousMovement, Is.False);
            FindButton(comfort.transform, "CONTINUOUS MOVEMENT").onClick.Invoke();
            Assert.That(manager.UseContinuousMovement, Is.True);
            Assert.That(manager.ComfortStatusText, Is.Not.Null);
            Assert.That(manager.ComfortStatusText.text, Is.Not.Empty,
                "The visible Comfort panel should report its selected mode.");

            manager.OpenCredits();
            yield return null;
            AssertExclusive(controls, controlsButtons, comfort, credits, credits);
            Assert.That(FindButton(credits.transform, "BACK"), Is.Not.Null);
            var creditsCopy = AllText(credits.transform);
            foreach (var phrase in new[] { "MODERN LUXURY VILLA", "AUTHOR", "SOURCE", "LICENSE" })
                Assert.That(creditsCopy, Does.Contain(phrase));
            FindButton(credits.transform, "BACK").onClick.Invoke();
            yield return null;
            Assert.That(controls.activeSelf || controlsButtons.activeSelf || comfort.activeSelf ||
                        credits.activeSelf, Is.False,
                "Back must restore the main button list.");
            Assert.That(manager.MainButtonsRoot.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator FreeTourReplacesMenuSceneWithoutStartingGuidedTour()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
            var manager = Object.FindAnyObjectByType<MainMenuManager>();
            Assert.That(manager, Is.Not.Null);

            manager.SetSnap30();
            manager.SetTeleportMovement();
            FindButton(FindMenuCanvas(), "START FREE TOUR").onClick.Invoke();
            yield return WaitForScene("ModernVillaTour", 60f);
            yield return new WaitForSecondsRealtime(5f);

            Assert.That(Object.FindObjectsByType<MainMenuManager>(FindObjectsInactive.Include), Is.Empty,
                "Menu scene objects must not persist into the villa.");
            Assert.That(Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            var tour = Object.FindAnyObjectByType<XRGuidedTour>();
            Assert.That(tour, Is.Not.Null);
            Assert.That(tour.IsTourActive, Is.False);
            Assert.That(tour.PanelRoot.activeSelf, Is.False);
            var roomMenu = Object.FindAnyObjectByType<XRRoomMenu>();
            Assert.That(roomMenu, Is.Not.Null);
            Assert.That(roomMenu.IsOpen, Is.False);
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var turns = origin.GetComponentsInChildren<SnapTurnProvider>(true);
            Assert.That(turns, Is.Not.Empty);
            foreach (var turn in turns)
                Assert.That(turn.turnAmount, Is.EqualTo(30f).Within(0.01f));
            var move = origin.GetComponentsInChildren<MonoBehaviour>(true)
                .FirstOrDefault(component => component.GetType().Name == "DynamicMoveProvider") as Behaviour;
            Assert.That(move, Is.Not.Null, "The known-good rig must retain its continuous movement provider.");
            Assert.That(move.enabled, Is.False, "Comfort teleport mode must disable smooth movement.");
        }

        [UnityTest]
        public IEnumerator GuidedLaunchStartsExistingKitchenStepAfterSceneReady()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
            var manager = Object.FindAnyObjectByType<MainMenuManager>();
            Assert.That(manager, Is.Not.Null);

            FindButton(FindMenuCanvas(), "GUIDED TOUR").onClick.Invoke();
            yield return WaitForScene("ModernVillaTour", 60f);
            var timeout = Time.realtimeSinceStartup + 15f;
            XRGuidedTour tour = null;
            while (Time.realtimeSinceStartup < timeout)
            {
                tour = Object.FindAnyObjectByType<XRGuidedTour>();
                if (tour != null && tour.IsTourActive && tour.PanelRoot.activeSelf)
                    break;
                yield return null;
            }

            Assert.That(Object.FindObjectsByType<MainMenuManager>(FindObjectsInactive.Include), Is.Empty);
            Assert.That(Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(tour, Is.Not.Null);
            Assert.That(tour.IsTourActive, Is.True);
            Assert.That(tour.IsComplete, Is.False);
            Assert.That(tour.CurrentStep, Is.EqualTo(0));
            Assert.That(tour.CurrentRoom, Is.EqualTo("Kitchen"));
            Assert.That(tour.StepCount, Is.EqualTo(6));
            Assert.That(tour.TourContent.activeSelf, Is.True);
            Assert.That(tour.CompletionContent.activeSelf, Is.False);
            var teleporter = Object.FindAnyObjectByType<XRRoomTeleporter>();
            Assert.That(teleporter, Is.Not.Null);
            Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo("Kitchen"),
                "Guided launch must reuse the validated Kitchen anchor.");
        }

        [UnityTest]
        public IEnumerator GuidedLaunchReachesMasterBedroomFromMainMenu()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
            FindButton(FindMenuCanvas(), "GUIDED TOUR").onClick.Invoke();
            yield return WaitForScene("ModernVillaTour", 60f);

            var tour = Object.FindAnyObjectByType<XRGuidedTour>();
            var teleporter = Object.FindAnyObjectByType<XRRoomTeleporter>();
            var origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.That(tour, Is.Not.Null);
            Assert.That(teleporter, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);

            var deadline = Time.realtimeSinceStartup + 15f;
            while ((!tour.IsTourActive || tour.CurrentStep != 0 || !tour.PanelRoot.activeSelf ||
                    teleporter.IsTeleporting) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(tour.IsTourActive && tour.CurrentStep == 0 && tour.PanelRoot.activeSelf, Is.True,
                "GUIDED TOUR must start at the Kitchen after the villa XR rig is ready.");

            var trackedHeadHeight = origin.CameraInOriginSpacePos.y;
            var trackedCameraLocalHeight = origin.Camera.transform.localPosition.y;
            for (var expectedStep = 1; expectedStep <= 3; expectedStep++)
            {
                tour.NextButton.onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 5f;
                while ((tour.CurrentStep != expectedStep || teleporter.IsTeleporting ||
                        !tour.PanelRoot.activeSelf) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(tour.CurrentStep, Is.EqualTo(expectedStep));
                Assert.That(tour.PanelRoot.activeSelf, Is.True);
            }

            Assert.That(tour.CurrentRoom, Is.EqualTo("Master Bedroom"));
            Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo("Master Bedroom"));
            Assert.That(tour.TourContent.activeSelf, Is.True);
            Assert.That(tour.CompletionContent.activeSelf, Is.False);
            var stepLabel = tour.TourContent.transform.Find("Step")?.GetComponent<Text>();
            Assert.That(stepLabel, Is.Not.Null);
            Assert.That(stepLabel.text, Is.EqualTo("Step 4 / 6"));

            var bedroomIndex = System.Array.IndexOf(teleporter.RoomNames, "Master Bedroom");
            Assert.That(bedroomIndex, Is.GreaterThanOrEqualTo(0));
            var anchor = teleporter.RoomAnchors[bedroomIndex];
            Assert.That(anchor, Is.Not.Null);
            var expectedArrival = new Vector3(0.80f, 68.12665f, -34.00f);
            Assert.That(Vector3.Distance(anchor.position, expectedArrival), Is.LessThan(0.03f),
                "The guided tour must use the safe arrival inside the master bedroom.");
            var headPosition = origin.Camera.transform.position;
            Assert.That(Vector2.Distance(new Vector2(headPosition.x, headPosition.z),
                new Vector2(expectedArrival.x, expectedArrival.z)), Is.LessThan(0.18f),
                "The tracked head must arrive above the master-bedroom anchor.");
            Assert.That(origin.CameraInOriginSpacePos.y, Is.EqualTo(trackedHeadHeight).Within(0.01f),
                "The guided teleport must preserve HMD tracking height.");
            Assert.That(origin.Camera.transform.localPosition.y,
                Is.EqualTo(trackedCameraLocalHeight).Within(0.01f));
            Assert.That(headPosition.y - expectedArrival.y, Is.EqualTo(trackedHeadHeight).Within(0.04f),
                "The HMD must remain at its tracked height above the bedroom floor.");
        }

        static Button FindButton(Component root, string label)
        {
            return root.GetComponentsInChildren<Button>(true).FirstOrDefault(button =>
            {
                var text = button.GetComponentInChildren<Text>(true);
                return text != null && text.text.Trim().ToUpperInvariant() == label;
            });
        }

        static string AllText(Component root)
        {
            return string.Join(" ", root.GetComponentsInChildren<Text>(true)
                .Select(component => component.text)).ToUpperInvariant();
        }

        static Canvas FindMenuCanvas()
        {
            return Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include).Single(canvas =>
                canvas.renderMode == RenderMode.WorldSpace &&
                canvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null);
        }

        static void AssertExclusive(GameObject controls, GameObject controlsButtons, GameObject comfort,
            GameObject credits, GameObject expected)
        {
            Assert.That(controls.activeSelf, Is.EqualTo(controls == expected));
            Assert.That(controlsButtons.activeSelf, Is.EqualTo(controlsButtons == expected));
            Assert.That(comfort.activeSelf, Is.EqualTo(comfort == expected));
            Assert.That(credits.activeSelf, Is.EqualTo(credits == expected));
        }

        static IEnumerator WaitForScene(string name, float seconds)
        {
            var timeout = Time.realtimeSinceStartup + seconds;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
        }
    }
}

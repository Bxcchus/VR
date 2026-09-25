using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusExplorer
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class XRInteractiveSlidingDoor : MonoBehaviour
    {
        [SerializeField] string doorId;
        [SerializeField] Transform firstPanel;
        [SerializeField] Transform secondPanel;
        [SerializeField] Transform firstCollisionTarget;
        [SerializeField] Transform secondCollisionTarget;
        [SerializeField] BoxCollider firstCollider;
        [SerializeField] BoxCollider secondCollider;
        [SerializeField] XRSimpleInteractable interactable;
        [SerializeField] Vector3 firstOpenWorldOffset;
        [SerializeField] Vector3 secondOpenWorldOffset;
        [SerializeField] float animationDuration = 0.7f;

        InputAction triggerAction;
        Vector3 firstClosedPanelPosition;
        Vector3 secondClosedPanelPosition;
        Quaternion firstClosedPanelRotation;
        Quaternion secondClosedPanelRotation;
        Vector3 firstClosedTargetPosition;
        Vector3 secondClosedTargetPosition;
        Coroutine animationRoutine;
        bool targetOpen;

        public string DoorId => doorId;
        public Transform FirstPanel => firstPanel;
        public Transform SecondPanel => secondPanel;
        public BoxCollider FirstCollider => firstCollider;
        public BoxCollider SecondCollider => secondCollider;
        public XRSimpleInteractable Interactable => interactable;
        public Vector3 FirstOpenWorldOffset => firstOpenWorldOffset;
        public Vector3 SecondOpenWorldOffset => secondOpenWorldOffset;
        public float AnimationDuration => animationDuration;
        public bool IsOpen { get; private set; }
        public bool IsAnimating => animationRoutine != null;
        public int ToggleCount { get; private set; }
        public bool HasQuestTriggerBindings
        {
            get
            {
                if (triggerAction == null)
                    return false;
                var left = false;
                var right = false;
                foreach (var binding in triggerAction.bindings)
                {
                    left |= binding.path == "<XRController>{LeftHand}/triggerPressed";
                    right |= binding.path == "<XRController>{RightHand}/triggerPressed";
                }
                return left && right;
            }
        }

        public void Configure(string id, Transform panelA, Transform panelB,
            Transform collisionTargetA, Transform collisionTargetB,
            BoxCollider colliderA, BoxCollider colliderB, XRSimpleInteractable xrInteractable,
            Vector3 openOffsetA, Vector3 openOffsetB, float duration)
        {
            doorId = id;
            firstPanel = panelA;
            secondPanel = panelB;
            firstCollisionTarget = collisionTargetA;
            secondCollisionTarget = collisionTargetB;
            firstCollider = colliderA;
            secondCollider = colliderB;
            interactable = xrInteractable;
            firstOpenWorldOffset = openOffsetA;
            secondOpenWorldOffset = openOffsetB;
            animationDuration = duration;
        }

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponent<XRSimpleInteractable>();

            firstClosedPanelPosition = firstPanel.position;
            secondClosedPanelPosition = secondPanel.position;
            firstClosedPanelRotation = firstPanel.rotation;
            secondClosedPanelRotation = secondPanel.rotation;
            firstClosedTargetPosition = firstCollisionTarget.position;
            secondClosedTargetPosition = secondCollisionTarget.position;
            targetOpen = false;
            IsOpen = false;

            triggerAction = new InputAction("Toggle Villa Sliding Door", InputActionType.Button);
            triggerAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
            triggerAction.AddBinding("<XRController>{RightHand}/triggerPressed");
        }

        void OnEnable()
        {
            if (triggerAction == null)
                return;
            triggerAction.performed += OnTriggerPressed;
            triggerAction.Enable();
        }

        void OnDisable()
        {
            if (triggerAction == null)
                return;
            triggerAction.Disable();
            triggerAction.performed -= OnTriggerPressed;
        }

        void OnDestroy()
        {
            triggerAction?.Dispose();
            if (firstPanel != null)
                firstPanel.SetPositionAndRotation(firstClosedPanelPosition, firstClosedPanelRotation);
            if (secondPanel != null)
                secondPanel.SetPositionAndRotation(secondClosedPanelPosition, secondClosedPanelRotation);
        }

        void OnTriggerPressed(InputAction.CallbackContext context)
        {
            if (interactable != null && interactable.isHovered)
                ToggleDoor();
        }

        public void ToggleDoor()
        {
            targetOpen = !targetOpen;
            ToggleCount++;
            if (animationRoutine != null)
                StopCoroutine(animationRoutine);
            animationRoutine = StartCoroutine(AnimateTo(targetOpen));
            Debug.Log($"[Sliding Door] {doorId}: {(targetOpen ? "OPEN" : "CLOSE")} requested.");
        }

        IEnumerator AnimateTo(bool open)
        {
            var firstPanelFrom = firstPanel.position;
            var secondPanelFrom = secondPanel.position;
            var firstTargetFrom = firstCollisionTarget.position;
            var secondTargetFrom = secondCollisionTarget.position;
            var firstPanelTo = firstClosedPanelPosition + (open ? firstOpenWorldOffset : Vector3.zero);
            var secondPanelTo = secondClosedPanelPosition + (open ? secondOpenWorldOffset : Vector3.zero);
            var firstTargetTo = firstClosedTargetPosition + (open ? firstOpenWorldOffset : Vector3.zero);
            var secondTargetTo = secondClosedTargetPosition + (open ? secondOpenWorldOffset : Vector3.zero);
            var elapsed = 0f;
            var duration = Mathf.Max(0.05f, animationDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var eased = normalized * normalized * (3f - 2f * normalized);
                firstPanel.position = Vector3.Lerp(firstPanelFrom, firstPanelTo, eased);
                secondPanel.position = Vector3.Lerp(secondPanelFrom, secondPanelTo, eased);
                firstCollisionTarget.position = Vector3.Lerp(firstTargetFrom, firstTargetTo, eased);
                secondCollisionTarget.position = Vector3.Lerp(secondTargetFrom, secondTargetTo, eased);
                yield return null;
            }

            firstPanel.position = firstPanelTo;
            secondPanel.position = secondPanelTo;
            firstCollisionTarget.position = firstTargetTo;
            secondCollisionTarget.position = secondTargetTo;
            IsOpen = open;
            animationRoutine = null;
            Debug.Log($"[Sliding Door] {doorId}: {(open ? "OPEN" : "CLOSED")}.");
        }
    }
}

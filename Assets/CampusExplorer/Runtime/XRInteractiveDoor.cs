using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusExplorer
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class XRInteractiveDoor : MonoBehaviour
    {
        [SerializeField] Transform doorMesh;
        [SerializeField] BoxCollider doorCollider;
        [SerializeField] XRSimpleInteractable interactable;
        [SerializeField] float openAngle = -90f;
        [SerializeField] float animationDuration = 0.6f;

        InputAction triggerAction;
        Quaternion closedLocalRotation;
        Vector3 closedDoorWorldPosition;
        Quaternion closedDoorWorldRotation;
        Vector3 closedDoorOffsetFromHinge;
        Quaternion closedDoorRotationFromHinge;
        Coroutine animationRoutine;
        bool targetOpen;

        public Transform DoorMesh => doorMesh;
        public BoxCollider DoorCollider => doorCollider;
        public XRSimpleInteractable Interactable => interactable;
        public float OpenAngle => openAngle;
        public float AnimationDuration => animationDuration;
        public bool IsOpen { get; private set; }
        public bool TargetOpen => targetOpen;
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

        public void Configure(Transform sourceDoor, BoxCollider movingCollider,
            XRSimpleInteractable xrInteractable, float angle, float duration)
        {
            doorMesh = sourceDoor;
            doorCollider = movingCollider;
            interactable = xrInteractable;
            openAngle = angle;
            animationDuration = duration;
        }

        void Awake()
        {
            if (interactable == null)
                interactable = GetComponent<XRSimpleInteractable>();

            closedLocalRotation = transform.localRotation;
            targetOpen = false;
            IsOpen = false;

            if (doorMesh != null)
            {
                closedDoorWorldPosition = doorMesh.position;
                closedDoorWorldRotation = doorMesh.rotation;
                closedDoorOffsetFromHinge = Quaternion.Inverse(transform.rotation) *
                                            (doorMesh.position - transform.position);
                closedDoorRotationFromHinge = Quaternion.Inverse(transform.rotation) * doorMesh.rotation;
            }

            triggerAction = new InputAction("Toggle Villa Door", InputActionType.Button);
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
            if (doorMesh != null)
                doorMesh.SetPositionAndRotation(closedDoorWorldPosition, closedDoorWorldRotation);
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
            Debug.Log($"[Interactive Door] {(targetOpen ? "OPEN" : "CLOSE")} requested.");
        }

        IEnumerator AnimateTo(bool open)
        {
            var from = transform.localRotation;
            var to = closedLocalRotation * Quaternion.Euler(0f, open ? openAngle : 0f, 0f);
            var elapsed = 0f;
            var duration = Mathf.Max(0.05f, animationDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var eased = normalized * normalized * (3f - 2f * normalized);
                transform.localRotation = Quaternion.Slerp(from, to, eased);
                ApplyDoorPose();
                yield return null;
            }

            transform.localRotation = to;
            ApplyDoorPose();
            IsOpen = open;
            animationRoutine = null;
            Debug.Log($"[Interactive Door] {(open ? "OPEN" : "CLOSED")}.");
        }

        void ApplyDoorPose()
        {
            if (doorMesh == null)
                return;
            doorMesh.SetPositionAndRotation(
                transform.position + transform.rotation * closedDoorOffsetFromHinge,
                transform.rotation * closedDoorRotationFromHinge);
        }
    }
}

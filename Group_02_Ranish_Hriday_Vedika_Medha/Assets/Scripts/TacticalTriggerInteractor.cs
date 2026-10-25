using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IVRAR.Group02.SensoryStress
{
    /// <summary>
    /// Harmless controller raycast trigger interactor.
    /// Casts forward from the controller/prop to register engagement decisions on targets.
    /// Supports XR Controller triggers as well as Editor mouse/keyboard fallback.
    /// </summary>
    public class TacticalTriggerInteractor : MonoBehaviour
    {
        [Header("Raycast Settings")]
        public Transform rayOrigin;
        public float maxRange = 50f;
        public LayerMask targetLayerMask = ~0;
        public LineRenderer laserPointer;

        [Header("Audio Feedback")]
        public AudioSource audioSource;
        public AudioClip clickClip;

        private void Awake()
        {
            if (rayOrigin == null) rayOrigin = transform;
            if (laserPointer == null) laserPointer = GetComponent<LineRenderer>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        private void Update()
        {
            UpdateLaser();
            if (IsTriggerPressed())
            {
                PerformEngagement();
            }
        }

        private void UpdateLaser()
        {
            if (laserPointer == null) return;
            Vector3 start = rayOrigin.position;
            Vector3 end = start + (rayOrigin.forward * maxRange);
            if (Physics.Raycast(start, rayOrigin.forward, out RaycastHit hit, maxRange, targetLayerMask))
            {
                end = hit.point;
            }
            laserPointer.SetPosition(0, start);
            laserPointer.SetPosition(1, end);
        }

        public bool IsTriggerPressed()
        {
            // Legacy / Standard Input fallback (Immediate Editor playtesting without VR controllers)
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
                return true;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
#endif
            return false;
        }

        public void PerformEngagement()
        {
            if (audioSource != null && clickClip != null)
                audioSource.PlayOneShot(clickClip);

            Vector3 start = rayOrigin.position;
            Vector3 direction = rayOrigin.forward;

            if (Physics.Raycast(start, direction, out RaycastHit hit, maxRange, targetLayerMask))
            {
                TargetTrialMetadata target = hit.collider.GetComponentInParent<TargetTrialMetadata>();
                if (target != null)
                {
                    target.RegisterDecision(true);
                    Debug.Log($"[TacticalTrigger] Engaged target: {target.TrialId} (Hostile={target.IsHostile})");
                }
            }
        }
    }
}

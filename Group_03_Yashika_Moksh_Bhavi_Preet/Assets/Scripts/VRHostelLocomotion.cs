using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Robust VR Locomotion for University Hostel environment conforming to NBC 2016 / NFPA 101.
/// Supports Meta Quest Touch controllers, Meta XR Simulator, and Keyboard/Mouse input.
/// Provides physical CharacterController collision, proper gravity, stair descent snapping,
/// dynamic head-tracking synchronization, and camera wall clipping protection.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VRHostelLocomotion : MonoBehaviour
{
    [Header("Locomotion Speeds")]
    [Tooltip("Standard walking speed in m/s (natural pace)")]
    public float walkSpeed = 2.5f;

    [Tooltip("Sprint speed in m/s (holding Left Shift or clicking thumbstick)")]
    public float sprintSpeed = 4.5f;

    [Tooltip("Gravity acceleration (m/s^2)")]
    public float gravity = -9.81f;

    [Tooltip("Downward snap force applied when grounded to cleanly follow stairs/ramps downwards")]
    public float groundStickForce = -4.0f;

    [Header("Rotation")]
    [Tooltip("Enable snap turning with right thumbstick or Q/E")]
    public bool snapTurn = true;
    public float snapTurnAngle = 45.0f;
    public float smoothTurnSpeed = 90.0f;

    [Header("References")]
    public Transform centerEyeAnchor;
    public Transform trackingSpace;

    [Header("Collision & Wall Pushback")]
    [Tooltip("Minimum distance from eye to any wall before pushback kicks in")]
    public float wallPushbackDistance = 0.25f;
    public LayerMask collisionMask = ~0; // All layers by default

    private CharacterController _characterController;
    private float _verticalVelocity = 0f;
    private bool _snapTurnReady = true;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();

        // Ensure optimal CharacterController parameters for hostel geometry (NBC stairs)
        if (_characterController != null)
        {
            _characterController.stepOffset = 0.35f;
            _characterController.slopeLimit = 55.0f;
            _characterController.minMoveDistance = 0.001f;
            if (_characterController.radius > 0.35f)
            {
                _characterController.radius = 0.3f;
            }
        }

        // Auto-find anchors if not explicitly assigned
        if (centerEyeAnchor == null)
        {
            var eye = transform.Find("TrackingSpace/CenterEyeAnchor");
            if (eye != null) centerEyeAnchor = eye;
            else
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) centerEyeAnchor = cam.transform;
            }
        }

        if (trackingSpace == null)
        {
            var ts = transform.Find("TrackingSpace");
            if (ts != null) trackingSpace = ts;
        }
    }

    private void Start()
    {
        // Disable conflicting legacy OVRPlayerController if present
        var ovrPlayer = GetComponent<OVRPlayerController>();
        if (ovrPlayer != null && ovrPlayer.enabled)
        {
            Debug.Log("[VRHostelLocomotion] Disabling legacy OVRPlayerController to prevent input & gravity conflicts.");
            ovrPlayer.enabled = false;
        }
    }

    private void Update()
    {
        if (_characterController == null) return;

        SyncCharacterControllerToHead();
        HandleRotation();
        HandleMovement();
        ApplyWallPushback();
    }

    /// <summary>
    /// Synchronizes CharacterController capsule height and center with head tracking pose.
    /// This ensures that the collision capsule accurately covers the player whether standing,
    /// crouching, or moving in room-scale / simulator.
    /// </summary>
    private void SyncCharacterControllerToHead()
    {
        if (centerEyeAnchor == null) return;

        float headHeight = Mathf.Clamp(centerEyeAnchor.localPosition.y, 1.0f, 2.4f);
        _characterController.height = headHeight;

        // Keep capsule center aligned with head XZ in local tracking space
        _characterController.center = new Vector3(
            centerEyeAnchor.localPosition.x,
            headHeight * 0.5f,
            centerEyeAnchor.localPosition.z
        );
    }

    /// <summary>
    /// Reads movement input from Meta Touch thumbstick, Keyboard (New Input System & Legacy).
    /// Moves CharacterController in the direction the HMD camera is facing.
    /// </summary>
    private void HandleMovement()
    {
        Vector2 input = Vector2.zero;

        // 1. Meta Touch Controller input (Left thumbstick)
        Vector2 ovrStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        if (ovrStick.sqrMagnitude > 0.01f)
        {
            input = ovrStick;
        }

        // 2. Keyboard input (Unity New Input System)
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            Vector2 kb = Vector2.zero;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) kb.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) kb.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) kb.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) kb.x += 1f;

            if (kb.sqrMagnitude > 0.01f)
            {
                input = kb;
            }
        }
#endif

        // 3. Fallback Legacy Input
#if ENABLE_LEGACY_INPUT_MANAGER
        if (input.sqrMagnitude <= 0.01f)
        {
            Vector2 legKb = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) legKb.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) legKb.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) legKb.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) legKb.x += 1f;

            if (legKb.sqrMagnitude > 0.01f)
            {
                input = legKb;
            }
        }
#endif

        if (input.sqrMagnitude > 1.0f)
        {
            input.Normalize();
        }

        // Sprinting
        bool isSprinting = false;
        if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick)) isSprinting = true;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) isSprinting = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) isSprinting = true;
#endif

        float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

        // Calculate planar forward/right directions from CenterEyeAnchor
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        if (centerEyeAnchor != null)
        {
            forward = Vector3.ProjectOnPlane(centerEyeAnchor.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(centerEyeAnchor.right, Vector3.up).normalized;
        }

        Vector3 moveHorizontal = (forward * input.y + right * input.x) * currentSpeed;

        // Grounding and Gravity handling
        if (_characterController.isGrounded)
        {
            // Apply downward stick force to cleanly track stairs and ramps downwards without flying
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity = groundStickForce;
            }
        }
        else
        {
            // In mid-air, apply continuous gravity
            _verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 totalVelocity = moveHorizontal + Vector3.up * _verticalVelocity;
        _characterController.Move(totalVelocity * Time.deltaTime);
    }

    /// <summary>
    /// Handles turning via Right Thumbstick or Q/E keys.
    /// </summary>
    private void HandleRotation()
    {
        float turnInput = 0f;

        // Meta Touch Controller (Right Thumbstick)
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (Mathf.Abs(rightStick.x) > 0.2f)
        {
            turnInput = rightStick.x;
        }

        // Keyboard Q / E
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.qKey.isPressed) turnInput -= 1f;
            if (Keyboard.current.eKey.isPressed) turnInput += 1f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.Q)) turnInput -= 1f;
        if (Input.GetKey(KeyCode.E)) turnInput += 1f;
#endif

        if (snapTurn)
        {
            if (Mathf.Abs(turnInput) > 0.5f)
            {
                if (_snapTurnReady)
                {
                    float angle = Mathf.Sign(turnInput) * snapTurnAngle;
                    RotateRig(angle);
                    _snapTurnReady = false;
                }
            }
            else
            {
                _snapTurnReady = true;
            }
        }
        else
        {
            if (Mathf.Abs(turnInput) > 0.05f)
            {
                RotateRig(turnInput * smoothTurnSpeed * Time.deltaTime);
            }
        }
    }

    /// <summary>
    /// Rotates the rig around the center eye anchor position.
    /// </summary>
    private void RotateRig(float angle)
    {
        if (centerEyeAnchor != null)
        {
            transform.RotateAround(centerEyeAnchor.position, Vector3.up, angle);
        }
        else
        {
            transform.Rotate(Vector3.up, angle);
        }
    }

    /// <summary>
    /// Prevents the VR head / camera from clipping through solid walls if the user
    /// physically leans or translates their head towards a wall.
    /// </summary>
    private void ApplyWallPushback()
    {
        if (centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;
        // Check for collisions near head in 8 planar directions
        int rays = 8;
        for (int i = 0; i < rays; i++)
        {
            float ang = i * (360f / rays) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

            RaycastHit hit;
            if (Physics.SphereCast(headPos - dir * 0.1f, 0.08f, dir, out hit, wallPushbackDistance + 0.1f, collisionMask, QueryTriggerInteraction.Ignore))
            {
                // Don't push back against our own character controller
                if (hit.collider == _characterController) continue;

                float penetration = (wallPushbackDistance + 0.1f) - hit.distance;
                if (penetration > 0f)
                {
                    Vector3 pushDir = -dir * penetration;
                    _characterController.Move(pushDir);
                }
            }
        }
    }
}

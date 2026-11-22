using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float runSpeed = 8f;
    // Facing is instant: the character looks along its movement the frame the input changes (no turn smoothing).
    [SerializeField, Tooltip("Visual yaw correction in degrees. Use 90 when the model faces Visual -X.")]
    private float modelFacingOffset = 90f;
    [SerializeField] private RuntimeAnimatorController visualController;
    [SerializeField] private Avatar visualAvatar;

    private Rigidbody body;
    // The village player's own PlayerEnergy, or the suspended village one in region scenes (SpiritGridScene).
    // Energy 0 slows walking and running by PlayerEnergy's exhausted speed multiplier; moving never costs energy.
    private PlayerEnergy energy;
    private Vector2 moveInput;
    private bool runHeld;
    private Transform movementCamera;
    private Transform visual;
    private Animator visualAnimator;
    private static readonly int MovingParameter = Animator.StringToHash("Moving");
    private static readonly int WalkSpeedParameter = Animator.StringToHash("WalkSpeed");
    private static readonly int SeedParameter = Animator.StringToHash("Seed");
    // v2.1_walk spans frames 0..47 at 24 fps and contains one left/right stride.
    private const float SourceWalkStepSeconds = 47f / (24f * 2f);
    private readonly FootstepTimer footsteps = new FootstepTimer();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        visual = transform.Find("Visual");
        if (visualController != null && visual != null && visual.childCount > 0)
        {
            // Animate the imported skeleton, while the Visual parent keeps its yaw correction.
            GameObject model = visual.GetChild(0).gameObject;
            if (!model.TryGetComponent(out visualAnimator)) visualAnimator = model.AddComponent<Animator>();
            visualAnimator.applyRootMotion = false;
            if (visualAvatar != null) visualAnimator.avatar = visualAvatar;
            visualAnimator.runtimeAnimatorController = visualController;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        moveInput = Vector2.zero;
        runHeld = false;
        if (keyboard == null || !Application.isFocused || SceneTransitionService.IsTraveling)
        {
            footsteps.Stop();
            UpdateLocomotion(false);
            return;
        }

        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        moveInput = Vector2.ClampMagnitude(new Vector2(x, z), 1f);
        runHeld = keyboard.leftShiftKey.isPressed;

        // Footsteps only while input moves the body for real (not standing, pushing a wall, paused or locked).
        bool moving = moveInput.sqrMagnitude > 0.01f && Time.timeScale > 0f && FootstepTimer.IsMovingFast(body);
        footsteps.Tick(moving, runHeld, energy != null ? energy.SpeedMultiplier : 1f, Time.deltaTime);
        UpdateLocomotion(moving);
    }

    // A disabled controller (sleep, trading post, travel lock) starts its next walk with a fresh step timer.
    private void OnDisable()
    {
        footsteps.Stop();
        UpdateLocomotion(false);
        if (AnimatorReady) visualAnimator.ResetTrigger(SeedParameter);
    }

    private bool AnimatorReady => visualAnimator != null && visualAnimator.isActiveAndEnabled &&
        visualAnimator.runtimeAnimatorController != null && visualAnimator.isInitialized;

    private void UpdateLocomotion(bool moving)
    {
        if (!AnimatorReady) return;
        visualAnimator.SetBool(MovingParameter, moving);
        if (!moving) return;
        Vector3 velocity = body.linearVelocity;
        float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        AudioLibrary audio = AudioManager.Instance != null ? AudioManager.Instance.Library : null;
        float stepInterval = runHeld ? (audio != null ? audio.runStepInterval : 0.28f)
                                     : (audio != null ? audio.walkStepInterval : 0.42f);
        float referenceSpeed = runHeld ? runSpeed : moveSpeed;
        float speedRatio = planarSpeed / Mathf.Max(referenceSpeed, 0.01f);
        visualAnimator.SetFloat(WalkSpeedParameter, SourceWalkStepSeconds * speedRatio / Mathf.Max(0.05f, stepInterval));
    }

    public void PlayPlantSeed()
    {
        if (isActiveAndEnabled && AnimatorReady) visualAnimator.SetTrigger(SeedParameter);
    }

    private void FixedUpdate()
    {
        if (SceneTransitionService.IsTraveling) return;
        // Keep gravity and ground collision response on the vertical axis.
        if (movementCamera == null && Camera.main != null)
            movementCamera = Camera.main.transform;
        Vector3 moveDirection = CalculateMoveDirection(moveInput, movementCamera);
        Vector3 velocity = body.linearVelocity;
        if (energy == null) energy = PlayerEnergy.For(gameObject);
        float speed = (runHeld ? runSpeed : moveSpeed) * (energy != null ? energy.SpeedMultiplier : 1f);
        velocity.x = moveDirection.x * speed;
        velocity.z = moveDirection.z * speed;
        body.linearVelocity = velocity;

        // Players without a separate visual child turn the body itself, also at once.
        if (visual == null && moveDirection.sqrMagnitude > 0.0001f)
            body.MoveRotation(Quaternion.LookRotation(moveDirection, Vector3.up));
    }

    private void LateUpdate()
    {
        // Turn the visible body independently of physics rotation constraints/interpolation.
        // No input means no rotation update, preserving the last visible heading.
        if (visual == null || moveInput.sqrMagnitude < 0.0001f) return;
        if (movementCamera == null && Camera.main != null) movementCamera = Camera.main.transform;
        // The same direction FixedUpdate drives the body in, read this frame so a new key turns the model at once
        // (90° and 180° alike). Yaw only; the model's facing offset is applied on top.
        Vector3 direction = CalculateMoveDirection(moveInput, movementCamera);
        if (direction.sqrMagnitude < 0.0001f) return;
        visual.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, modelFacingOffset, 0f);
    }

    private static Vector3 CalculateMoveDirection(Vector2 input, Transform view)
    {
        if (view == null)
            return new Vector3(input.x, 0f, input.y);

        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        // Also support a camera looking straight down without losing forward input.
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(view.up, Vector3.up);
        forward.Normalize();
        Vector3 right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
    }
}

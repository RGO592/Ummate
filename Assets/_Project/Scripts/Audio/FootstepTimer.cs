using UnityEngine;

// Timer-based footsteps for a player without animation events: one step every walk/run interval
// (AudioLibrary) while the player is really moving. Standing, locked input or travel reset it.
public sealed class FootstepTimer
{
    private float untilNextStep;
    private bool wasMoving;

    // speedMultiplier < 1 (no energy) slows the steps along with the movement.
    public void Tick(bool moving, bool running, float speedMultiplier, float deltaTime)
    {
        AudioLibrary library = AudioManager.Instance != null ? AudioManager.Instance.Library : null;
        if (library == null || !moving)
        {
            Stop();
            return;
        }
        float interval = (running ? library.runStepInterval : library.walkStepInterval) / Mathf.Max(0.1f, speedMultiplier);
        if (!wasMoving)
        {
            wasMoving = true;
            untilNextStep = library.firstStepDelay;
        }
        // Switching walk → run must not wait out the longer walk interval.
        untilNextStep = Mathf.Min(untilNextStep, interval) - deltaTime;
        if (untilNextStep > 0f) return;
        AudioManager.Play(running ? GameSfx.FootstepRun : GameSfx.FootstepWalk);
        untilNextStep = interval;
    }

    public void Stop() => wasMoving = false;

    public static bool IsMovingFast(Rigidbody body)
    {
        if (body == null || body.isKinematic) return false;
        Vector3 velocity = body.linearVelocity;
        float min = AudioManager.Instance != null && AudioManager.Instance.Library != null
            ? AudioManager.Instance.Library.minStepSpeed : 0.5f;
        return velocity.x * velocity.x + velocity.z * velocity.z >= min * min;
    }
}

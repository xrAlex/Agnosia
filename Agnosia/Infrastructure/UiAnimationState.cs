namespace Agnosia.Infrastructure;

public static class UiAnimationState
{
    public static event Action? Changed;
    public static bool IsActive { get; private set; } = true;

    public static void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        Changed?.Invoke();
    }
}

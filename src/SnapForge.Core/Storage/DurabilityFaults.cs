namespace SnapForge.Core.Storage;

/// <summary>
/// Fault-injection seam for crash testing (FA-015/016/005/006). Storage code calls <see cref="Checkpoint"/> at every
/// durable boundary (a file flushed but not yet renamed, a rename done, a commit point passed, a file about to be
/// deleted). Tests install a <see cref="Hook"/> that throws at the n-th checkpoint to simulate a crash there, then
/// reopen the library and check that it is coherent. No effect in production (the hook is null).
/// </summary>
internal static class DurabilityFaults
{
    /// <summary>Called with the name of each checkpoint as it is reached (flows with the async context).</summary>
    internal static AsyncLocal<Action<string>?> Hook { get; } = new();

    internal static void Checkpoint(string point) => Hook.Value?.Invoke(point);
}

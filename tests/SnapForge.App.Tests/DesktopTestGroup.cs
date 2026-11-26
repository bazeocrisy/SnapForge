namespace SnapForge.App.Tests;

/// <summary>
/// Tests that show windows, use the clipboard or inject real mouse input. They share one UI thread and must
/// never run in parallel with each other (two tests moving the same cursor would corrupt each other).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopTestGroup
{
    public const string Name = "Desktop";
}

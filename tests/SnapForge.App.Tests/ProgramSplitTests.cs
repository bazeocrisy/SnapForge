using System.IO;
using System.Text.Json;
using SnapForge.App.Services;

namespace SnapForge.App.Tests;

/// <summary>RC3: SnapForge Capture and SnapForge Editor hand work to each other through per-program inboxes.</summary>
[Collection(DesktopTestGroup.Name)]
public sealed class ProgramSplitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "snapforge-handoff-" + Guid.NewGuid().ToString("N"));

    public ProgramSplitTests() => Handoff.RootOverride = _root;

    public void Dispose()
    {
        Handoff.RootOverride = null;
        Handoff.StartOverride = null;
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RC3_requests_are_delivered_to_the_right_program_in_order_and_removed_once_handled()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        Handoff.Write(AppRole.Editor, new HandoffRequest(Handoff.OpenCapture, first));
        Handoff.Write(AppRole.Editor, new HandoffRequest(Handoff.PlayVideo, second));
        Handoff.Write(AppRole.Capture, new HandoffRequest(Handoff.RecordPanel));

        IReadOnlyList<(string File, HandoffRequest Request)> editor = Handoff.Pending(AppRole.Editor);
        Assert.Equal([first, second], editor.Select(p => p.Request.Id!.Value));
        Assert.Equal([Handoff.OpenCapture, Handoff.PlayVideo], editor.Select(p => p.Request.Action));
        Assert.Equal(Handoff.RecordPanel, Assert.Single(Handoff.Pending(AppRole.Capture)).Request.Action);

        foreach ((string file, _) in editor)
        {
            Handoff.Complete(file);
        }

        Assert.Empty(Handoff.Pending(AppRole.Editor));
        Assert.Single(Handoff.Pending(AppRole.Capture)); // the other program's inbox is untouched
    }

    [Fact]
    public void RC3_a_request_survives_until_the_target_program_reads_it_and_stale_or_damaged_requests_are_discarded()
    {
        // The Editor is not running and cannot be started (as when it crashed or is not installed next to Capture):
        // the request stays in its inbox and is read at its next start.
        Handoff.StartOverride = _ => false;
        Guid id = Guid.NewGuid();
        Assert.Equal(HandoffDelivery.Queued, Handoff.Send(AppRole.Editor, new HandoffRequest(Handoff.OpenCapture, id)));
        Assert.Equal(id, Assert.Single(Handoff.Pending(AppRole.Editor)).Request.Id);

        string dir = Handoff.InboxDirectory(AppRole.Editor);
        File.WriteAllText(Path.Combine(dir, "0000000000000000001-broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(dir, "0000000000000000002-old.json"),
            JsonSerializer.Serialize(new HandoffRequest(Handoff.OpenCapture, Guid.NewGuid(), CreatedUtc: DateTime.UtcNow.AddDays(-2))));

        Assert.Equal(id, Assert.Single(Handoff.Pending(AppRole.Editor)).Request.Id);
        Assert.Single(Directory.GetFiles(dir, "*.json")); // damaged and day-old requests were deleted, not acted on
    }

    [Fact]
    public void RC3_a_started_target_is_reported_and_the_request_is_already_waiting_for_it()
    {
        AppRole? started = null;
        Handoff.StartOverride = role =>
        {
            started = role;
            return true;
        };

        Assert.Equal(HandoffDelivery.Started, Handoff.Send(AppRole.Editor, new HandoffRequest(Handoff.ShowLibrary)));
        Assert.Equal(AppRole.Editor, started);
        Assert.Equal(Handoff.ShowLibrary, Assert.Single(Handoff.Pending(AppRole.Editor)).Request.Action);
    }

    [Fact]
    public void RC3_editor_command_line_maps_to_requests()
    {
        Guid id = Guid.NewGuid();
        Assert.Equal(new HandoffRequest(Handoff.OpenCapture, id), Handoff.FromArguments(["--open", id.ToString()]));
        Assert.Equal(new HandoffRequest(Handoff.PlayVideo, id), Handoff.FromArguments(["--play", id.ToString()]));
        Assert.Equal(Handoff.ShowLibrary, Handoff.FromArguments(["--library"])!.Action);
        Assert.Null(Handoff.FromArguments(["--open", "not-a-guid"]));
        Assert.Null(Handoff.FromArguments([]));
    }

    [Fact]
    public void RC3_each_program_and_profile_has_its_own_single_instance_key()
    {
        Assert.NotEqual(Handoff.InstanceKey(AppRole.Capture), Handoff.InstanceKey(AppRole.Editor));
        // SnapForge Capture keeps the key of every earlier SnapForge, so a running older copy is recognised.
        Assert.Equal("SnapForge-SingleInstance-7c1d", Handoff.InstanceName(AppRole.Capture));
    }
}

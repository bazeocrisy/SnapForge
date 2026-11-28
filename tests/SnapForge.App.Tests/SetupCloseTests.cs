using System.Diagnostics;
using System.Windows;
using SnapForge.App.Services;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Tests;

/// <summary>
/// RC4: when SnapForge Setup asks SnapForge Editor to close (install, repair, upgrade), the Editor goes through
/// <see cref="MainWindow.CloseForRealAsync"/>: an edit made just before the request is saved to the Library first —
/// nothing waits for the debounced autosave and nothing is lost.
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class SetupCloseTests
{
    [Fact]
    public void RC4_setup_close_request_saves_an_edit_made_just_before_it() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var w = new MainWindow(services) { Width = 1200, Height = 800, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        EditorSession session = w.LoadImage(TestImages.Gradient(320, 200), DateTime.Now);
        var sw = Stopwatch.StartNew();
        while (!session.InLibrary && sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(50);
        }

        Assert.True(session.InLibrary);
        Guid id = session.CaptureId!.Value;
        var arrow = new ArrowAnnotation(new PointD(10, 10), new PointD(200, 150));
        session.Document.Execute(new AddAnnotationCommand(arrow));
        Assert.True(session.Document.IsDirty); // unsaved edit at the moment Setup asks

        Assert.True(await w.CloseForRealAsync()); // what Setup's close request runs in SnapForge Editor
        Assert.False(w.IsVisible);
        Assert.Equal(arrow, services.Library.Load(id).Annotations.OfType<ArrowAnnotation>().Single());
    });
}

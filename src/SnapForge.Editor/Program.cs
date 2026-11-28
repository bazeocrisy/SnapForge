using SnapForge.App.Services;

namespace SnapForge.Editor;

/// <summary>Entry point of SnapForge Editor (SnapForgeEditor.exe): the shared application in the Editor role.</summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        SnapForge.App.App.Role = AppRole.Editor;
        var app = new SnapForge.App.App();
        app.InitializeComponent();
        return app.Run();
    }
}

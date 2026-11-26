using System.Runtime.ExceptionServices;

namespace SnapForge.App.Tests;

/// <summary>WPF objects require a single-threaded apartment; xUnit runs tests on MTA thread-pool threads.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // Rethrown on the test thread below.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

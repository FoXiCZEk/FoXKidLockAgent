using FoXKidLockAgent.Services;
using FoXKidLockAgent.Windows;
using System.Windows;

namespace FoXKidLockAgent;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private AgentController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Global\ParentalLockPC_SingleInstance", out var created);
        if (!created) { Shutdown(); return; }
        _controller = new AgentController(AgentSettings.Load());
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

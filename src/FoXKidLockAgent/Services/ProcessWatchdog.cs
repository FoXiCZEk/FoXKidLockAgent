using System.Diagnostics;
using System.IO;
using System.Management;

namespace FoXKidLockAgent.Services;

public sealed class ProcessWatchdog : IDisposable
{
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private ManagementEventWatcher? _watcher;
    private volatile bool _locked;
    public event Action<string>? ProcessTerminated;

    public void UpdateBlockedList(IEnumerable<string> names)
    {
        lock (_gate) { _blocked.Clear(); foreach (var name in names) _blocked.Add(Path.GetFileNameWithoutExtension(name.Trim())); }
    }
    public void SetLockState(bool locked) { _locked = locked; if (locked) KillExisting(); }
    public void Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(1), "TargetInstance ISA 'Win32_Process'"));
            _watcher.EventArrived += (_, e) =>
            {
                if (!_locked || e.NewEvent["TargetInstance"] is not ManagementBaseObject target) return;
                var name = Convert.ToString(target["Name"]) ?? "";
                if (!IsBlocked(name)) return;
                try { Process.GetProcessById(Convert.ToInt32(target["ProcessId"])).Kill(); ProcessTerminated?.Invoke(name); } catch { }
            };
            _watcher.Start();
        }
        catch { /* WMI can be disabled by policy; existing-process checks still run on state changes. */ }
    }
    private bool IsBlocked(string name) { lock (_gate) return _blocked.Contains(Path.GetFileNameWithoutExtension(name)); }
    private void KillExisting()
    {
        string[] names; lock (_gate) names = _blocked.ToArray();
        foreach (var name in names) foreach (var process in Process.GetProcessesByName(name))
            try { process.Kill(); ProcessTerminated?.Invoke(process.ProcessName); } catch { }
    }
    public void Dispose() { _watcher?.Stop(); _watcher?.Dispose(); }
}

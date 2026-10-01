using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;
using ParentalLock.Agent.Native;
using ParentalLock.Agent.Windows;

namespace ParentalLock.Agent.Services;

public sealed class AgentController : IDisposable
{
    private readonly AgentSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ProcessWatchdog _watchdog = new();
    private readonly LowLevelKeyboardHook _keyboardHook = new();
    private readonly List<Window> _overlays = [];
    private readonly CancellationTokenSource _cancel = new();
    private int _killedCount;
    private string? _lastKilled;
    private bool _locked;

    public AgentController(AgentSettings settings)
    {
        _settings = settings;
        _watchdog.ProcessTerminated += name => { Interlocked.Increment(ref _killedCount); _lastKilled = name; ShowLock(); };
    }

    public void Start()
    {
        _watchdog.Start();
        ShowLock();
        _ = Task.Run(SyncLoopAsync);
    }

    private async Task SyncLoopAsync()
    {
        while (!_cancel.IsCancellationRequested)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"{_settings.ServerUrl.TrimEnd('/')}/api/agent/heartbeat", new AgentHeartbeatRequest { KilledCount = _killedCount, KilledProcess = _lastKilled }, _cancel.Token);
                var state = await response.Content.ReadFromJsonAsync<AgentHeartbeatResponse>(cancellationToken: _cancel.Token);
                if (state is not null) ApplyState(state);
            }
            catch (OperationCanceledException) { break; }
            catch { /* Offline fail-safe: preserve last known state. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cancel.Token); } catch (OperationCanceledException) { break; }
        }
    }

    private void ApplyState(AgentHeartbeatResponse state)
    {
        _watchdog.UpdateBlockedList(state.BlockedProcesses);
        var shouldLock = !string.Equals(state.Status, "unlocked_playing", StringComparison.OrdinalIgnoreCase);
        _watchdog.SetLockState(shouldLock);
        System.Windows.Application.Current.Dispatcher.Invoke(() => { if (shouldLock) ShowLock(); else HideLock(); });
    }

    private void ShowLock()
    {
        if (_locked) return;
        _locked = true; _keyboardHook.EnableHook();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            Window window = screen.Primary ? new KioskWindow(_settings.ServerUrl) : new BlackoutWindow();
            window.Left = screen.Bounds.Left; window.Top = screen.Bounds.Top;
            window.Width = screen.Bounds.Width; window.Height = screen.Bounds.Height;
            window.Show(); _overlays.Add(window);
        }
    }
    private void HideLock()
    {
        _locked = false; _keyboardHook.DisableHook();
        foreach (var overlay in _overlays) overlay.Close();
        _overlays.Clear();
    }
    public void Dispose() { _cancel.Cancel(); HideLock(); _watchdog.Dispose(); _keyboardHook.Dispose(); _http.Dispose(); _cancel.Dispose(); }
}

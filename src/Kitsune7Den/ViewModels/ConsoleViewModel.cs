using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class ConsoleViewModel : ObservableObject
{
    private readonly TelnetService _telnetService;
    private readonly LogWatcherService _logWatcher;
    private readonly ServerProcessService _processService;
    private readonly List<string> _commandHistory = [];
    private int _historyIndex = -1;

    [ObservableProperty] private string _commandInput = "";
    [ObservableProperty] private bool _autoScroll = true;

    public ObservableCollection<string> LogLines { get; } = [];

    public ConsoleViewModel(TelnetService telnetService, LogWatcherService logWatcher, ServerProcessService processService,
        RestartService restartService)
    {
        _telnetService = telnetService;
        _logWatcher = logWatcher;
        _processService = processService;

        // Log watcher output
        _logWatcher.LogEntryReceived += entry =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add($"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Message}");
                TrimLines();
            });
        };

        // Telnet output
        _telnetService.DataReceived += line =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(line);
                TrimLines();
            });
        };

        // Automatic restart progress
        restartService.StatusChanged += line =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add($"[{DateTime.Now:HH:mm:ss}] [Restart] {line}");
                TrimLines();
            });
        };

        // Process stdout
        _processService.OutputReceived += line =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(line);
                TrimLines();
            });
        };
    }

    [RelayCommand]
    private async Task SendCommand()
    {
        if (string.IsNullOrWhiteSpace(CommandInput)) return;

        var cmd = CommandInput.Trim();
        _commandHistory.Add(cmd);
        _historyIndex = _commandHistory.Count;

        LogLines.Add($"> {cmd}");
        // A shutdown typed here is deliberate: don't let crash auto-restart bring it back
        if (cmd.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
            _processService.NoteShutdownRequested();
        await _telnetService.SendCommandAsync(cmd);
        CommandInput = "";
    }

    [RelayCommand]
    private void HistoryUp()
    {
        if (_commandHistory.Count == 0) return;
        _historyIndex = Math.Max(0, _historyIndex - 1);
        CommandInput = _commandHistory[_historyIndex];
    }

    [RelayCommand]
    private void HistoryDown()
    {
        if (_commandHistory.Count == 0) return;
        _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
        CommandInput = _historyIndex < _commandHistory.Count ? _commandHistory[_historyIndex] : "";
    }

    [RelayCommand]
    private void Clear()
    {
        LogLines.Clear();
    }

    private void TrimLines()
    {
        while (LogLines.Count > 5000)
            LogLines.RemoveAt(0);
    }
}

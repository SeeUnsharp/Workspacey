using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Workspacey.App.Services;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;
using Workspacey.Core.Services;

namespace Workspacey.App.ViewModels;

/// <summary>Settings window: the pick shortcut and the list of workspace rules.</summary>
public sealed partial class MainWindowViewModel(
    IPickShortcutService shortcuts,
    IWorkspaceRuleRepository rules,
    IVirtualDesktopService desktops,
    WorkspaceAssignmentService assignments,
    IPickWorkflow pickWorkflow,
    ISystemSettingsLauncher systemSettings,
    IDesktopIntegration desktopIntegration,
    ILogger<MainWindowViewModel> logger) : ViewModelBase
{
    private const string NoShortcutText = "Not set";

    /// <summary>Case-insensitive, numbers compared by value ("Game 2" before "Game 10").</summary>
    private static readonly StringComparer NaturalOrder = StringComparer.Create(
        CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(15);

    private KeyChord? _registeredChord;
    private int _recordingSession;
    private Task _pauseChange = Task.CompletedTask;

    /// <summary>All rules, sorted; <see cref="Rules"/> is the part matching <see cref="SearchText"/>.</summary>
    private List<RuleItemViewModel> _allRules = [];

    public ObservableCollection<RuleItemViewModel> Rules { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInApplicationMenu { get; private set; }

    /// <summary>True when only one workspace exists, so rules have nowhere useful to send windows.</summary>
    [ObservableProperty]
    public partial bool SuggestNewDesktop { get; private set; }

    public string EmptyListText => _allRules.Count == 0 ? "No workspace rules yet." : "No rules match the search.";

    [ObservableProperty]
    public partial string ShortcutText { get; private set; } = NoShortcutText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyShortcutCommand))]
    public partial KeyChord? RecordedChord { get; private set; }

    [ObservableProperty]
    public partial bool IsRecording { get; private set; }

    /// <summary>The recorded combination belongs to another action; Apply stays disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyShortcutCommand))]
    public partial bool RecordedChordIsTaken { get; private set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddDesktopCommand))]
    public partial string NewDesktopName { get; set; } = string.Empty;

    public string RecorderText => IsRecording
        ? "Press the key combination… (Esc cancels)"
        : RecordedChord?.ToString() ?? ShortcutText;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            // Keeps shortcut and menu launchers valid after the program (e.g. the AppImage) was moved.
            await desktopIntegration.RefreshInstalledLaunchersAsync();
            IsInApplicationMenu = desktopIntegration.IsMenuEntryInstalled;
        }
        catch (Exception e)
        {
            ReportError("Could not update the installed launchers", e);
        }

        try
        {
            _registeredChord = await shortcuts.GetAsync();
            ShortcutText = _registeredChord?.ToString() ?? NoShortcutText;
            RecordedChord = null;
            OnPropertyChanged(nameof(RecorderText));
        }
        catch (Exception e)
        {
            ReportError("Could not read the current shortcut", e);
        }

        await RefreshRulesAsync();
    }

    [RelayCommand]
    private void StartRecording()
    {
        IsRecording = true;

        // Never leave the desktop's shortcuts paused if the user walks away mid-recording.
        var session = ++_recordingSession;
        _ = Task.Delay(RecordingTimeout).ContinueWith(
            _ => { if (IsRecording && session == _recordingSession) StopRecording(); },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Ends recording without a result (Esc, timeout, window closed or lost focus).</summary>
    public void StopRecording()
    {
        if (IsRecording) IsRecording = false;
    }

    /// <summary>Like <see cref="StopRecording"/>, but completes once global shortcuts are resumed.</summary>
    public Task StopRecordingAsync()
    {
        StopRecording();
        return _pauseChange;
    }

    /// <summary>Called by the view for every key press while recording.</summary>
    public void OnKeyRecorded(KeyChord? chord, bool cancel)
    {
        if (!IsRecording) return;

        if (cancel)
        {
            StopRecording();
        }
        else if (chord is not null)
        {
            if (chord.IsValidGlobalShortcut)
            {
                RecordedChord = chord;
                RecordedChordIsTaken = false;
                StopRecording();
                _ = CheckRecordedChordAsync(chord);
            }
            else
            {
                SetStatus($"{chord} needs a modifier (Meta, Ctrl, Alt or Shift).", isError: true);
            }
        }
    }

    /// <summary>Tells right away whether the recorded combination is free, instead of only after Apply.</summary>
    private async Task CheckRecordedChordAsync(KeyChord chord)
    {
        if (chord == _registeredChord)
        {
            SetStatus($"{chord} is already your pick shortcut.");
            return;
        }

        try
        {
            var availability = await shortcuts.CheckAvailabilityAsync(chord);
            if (RecordedChord != chord) return; // the user recorded something else meanwhile

            RecordedChordIsTaken = !availability.Accepted;
            if (availability.Accepted) SetStatus($"{chord} is free. Click Apply to use it.");
            else SetStatus(ConflictMessage(chord, availability.ConflictingAction), isError: true);
        }
        catch (Exception e)
        {
            // Not fatal: Apply checks again and reports a conflict then.
            logger.LogWarning(e, "Checking {Chord} for conflicts failed", chord);
            SetStatus($"Click Apply to use {chord}.");
        }
    }

    private static string ConflictMessage(KeyChord chord, string? owner) =>
        $"{chord} is already used by {(owner is null ? "another shortcut" : $"\"{owner}\"")}. Record a different combination.";

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecorderText));
        _pauseChange = PauseGlobalShortcutsAsync(value);
    }

    /// <summary>
    /// While recording, the desktop's own global shortcuts are paused; otherwise a combination that is
    /// already taken (e.g. Meta+W) would trigger its action instead of reaching the recorder.
    /// </summary>
    private async Task PauseGlobalShortcutsAsync(bool paused)
    {
        try
        {
            await shortcuts.SetGlobalShortcutsPausedAsync(paused);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not {Action} global shortcuts", paused ? "pause" : "resume");
        }
    }

    private bool CanApplyShortcut() => RecordedChord is not null && RecordedChord != _registeredChord && !RecordedChordIsTaken;

    [RelayCommand(CanExecute = nameof(CanApplyShortcut))]
    private async Task ApplyShortcutAsync()
    {
        if (RecordedChord is not { } chord) return;

        try
        {
            var result = await shortcuts.SetAsync(chord);
            if (result.Accepted)
            {
                _registeredChord = chord;
                ShortcutText = chord.ToString();
                SetStatus($"Shortcut {chord} is active. Press it anywhere to pick a window.");
            }
            else
            {
                // The previous shortcut stays active, so ShortcutText remains correct.
                SetStatus(ConflictMessage(chord, result.ConflictingAction), isError: true);
            }
        }
        catch (Exception e)
        {
            ReportError("Registering the shortcut failed", e);
        }

        RecordedChord = null;
        OnPropertyChanged(nameof(RecorderText));
    }

    [RelayCommand]
    private async Task ClearShortcutAsync()
    {
        try
        {
            await shortcuts.ClearAsync();
            _registeredChord = null;
            RecordedChord = null;
            ShortcutText = NoShortcutText;
            OnPropertyChanged(nameof(RecorderText));
            SetStatus("Shortcut removed.");
        }
        catch (Exception e)
        {
            ReportError("Removing the shortcut failed", e);
        }
    }

    [RelayCommand]
    private async Task PickWindowAsync()
    {
        try
        {
            SetStatus("Click the window or game you want to assign…");
            var saved = await pickWorkflow.RunAsync();
            SetStatus(saved ? "Rule saved." : null);
            if (saved) await RefreshRulesAsync();
        }
        catch (Exception e)
        {
            ReportError("Picking a window failed", e);
        }
    }

    [RelayCommand]
    private async Task RefreshRulesAsync()
    {
        try
        {
            var available = await desktops.GetDesktopsAsync();
            SuggestNewDesktop = available.Count <= 1;
            var all = await rules.GetAllAsync();

            // Display order only: the order in kwinrulesrc (KWin's evaluation order) is left untouched.
            _allRules = [.. all
                .Select(rule => new RuleItemViewModel(rule, available, ChangeRuleDesktopAsync, DeleteRuleCommand))
                .OrderBy(item => item.Description, NaturalOrder)
                .ThenBy(item => item.Rule.WindowClass, NaturalOrder)];
            ApplySearch();
        }
        catch (Exception e)
        {
            ReportError("Could not load window rules", e);
        }
    }

    private bool CanAddDesktop() => !string.IsNullOrWhiteSpace(NewDesktopName);

    [RelayCommand(CanExecute = nameof(CanAddDesktop))]
    private async Task AddDesktopAsync()
    {
        try
        {
            var created = await desktops.CreateDesktopAsync(NewDesktopName.Trim());
            NewDesktopName = string.Empty;
            SetStatus($"Workspace {created.DisplayName} added.");
            await RefreshRulesAsync(); // rule dropdowns offer the new workspace
        }
        catch (Exception e)
        {
            ReportError("Adding the workspace failed", e);
        }
    }

    [RelayCommand]
    private async Task ToggleApplicationMenuAsync()
    {
        try
        {
            if (desktopIntegration.IsMenuEntryInstalled)
            {
                await desktopIntegration.RemoveMenuEntryAsync();
                SetStatus("Removed Workspacey from the application menu.");
            }
            else
            {
                await desktopIntegration.InstallMenuEntryAsync();
                SetStatus("Workspacey is now in the application menu.");
            }
        }
        catch (Exception e)
        {
            ReportError("Changing the application menu entry failed", e);
        }

        IsInApplicationMenu = desktopIntegration.IsMenuEntryInstalled;
        OnPropertyChanged(nameof(IsInApplicationMenu)); // the CheckBox toggled itself; resync even if unchanged
    }

    [RelayCommand]
    private void OpenWorkspaceSettings()
    {
        try
        {
            systemSettings.OpenWorkspaceSettings();
        }
        catch (Exception e)
        {
            ReportError("Could not open System Settings", e);
        }
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(RuleItemViewModel? item)
    {
        if (item is null) return;

        try
        {
            await assignments.DeleteAsync(item.Rule.Id);
            _allRules.Remove(item);
            Rules.Remove(item);
            OnPropertyChanged(nameof(EmptyListText));
            SetStatus($"Removed rule \"{item.Description}\".");
        }
        catch (Exception e)
        {
            ReportError("Deleting the rule failed", e);
        }
    }

    private async Task<bool> ChangeRuleDesktopAsync(RuleItemViewModel item, VirtualDesktop desktop)
    {
        try
        {
            item.Rule = await assignments.ChangeDesktopAsync(item.Rule, desktop.Id);
            SetStatus($"\"{item.Description}\" now opens on {desktop.DisplayName}.");
            return true;
        }
        catch (Exception e)
        {
            ReportError("Changing the workspace failed", e);
            return false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void ApplySearch()
    {
        Rules.Clear();
        foreach (var item in _allRules.Where(item => TextSearch.Matches(
                     SearchText, item.Description, item.Rule.WindowClass, item.SelectedDesktop?.DisplayName)))
        {
            Rules.Add(item);
        }

        OnPropertyChanged(nameof(EmptyListText));
    }

    private void ReportError(string message, Exception e)
    {
        logger.LogError(e, "{Message}", message);
        SetStatus($"{message}: {e.Message}", isError: true);
    }

    private void SetStatus(string? message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}

/// <summary>One rule in the settings list. Picking a different workspace saves immediately.</summary>
public sealed class RuleItemViewModel : ViewModelBase
{
    private readonly Func<RuleItemViewModel, VirtualDesktop, Task<bool>> _changeDesktop;
    private VirtualDesktop? _selectedDesktop;

    public RuleItemViewModel(
        WorkspaceRule rule,
        IReadOnlyList<VirtualDesktop> desktops,
        Func<RuleItemViewModel, VirtualDesktop, Task<bool>> changeDesktop,
        IRelayCommand<RuleItemViewModel> deleteCommand)
    {
        Rule = rule;
        Desktops = desktops;
        DeleteCommand = deleteCommand;
        _changeDesktop = changeDesktop;
        _selectedDesktop = desktops.FirstOrDefault(d => rule.DesktopIds.Contains(d.Id));
    }

    public WorkspaceRule Rule { get; set; }

    public IReadOnlyList<VirtualDesktop> Desktops { get; }

    /// <summary><c>null</c> when the rule points at a workspace that no longer exists.</summary>
    public VirtualDesktop? SelectedDesktop
    {
        get => _selectedDesktop;
        set
        {
            var previous = _selectedDesktop;
            if (value is null || !SetProperty(ref _selectedDesktop, value)) return;
            _ = SaveDesktopAsync(value, previous);
        }
    }

    public string Description => string.IsNullOrWhiteSpace(Rule.Description) ? Rule.WindowClass : Rule.Description;

    public string Details => Rule.MatchMode switch
    {
        ClassMatchMode.Exact => $"class = {Rule.WindowClass}",
        ClassMatchMode.Substring => $"class contains {Rule.WindowClass}",
        _ => $"class matches /{Rule.WindowClass}/",
    } + (Rule.Policy == RulePolicy.Force ? " · forced" : string.Empty)
      + (Rule.HasOtherConditions ? " · more conditions (see KDE Window Rules)" : string.Empty);

    public IRelayCommand<RuleItemViewModel> DeleteCommand { get; }

    private async Task SaveDesktopAsync(VirtualDesktop desktop, VirtualDesktop? previous)
    {
        // Show the stored state again if saving failed (the parent reports the error).
        if (!await _changeDesktop(this, desktop))
        {
            _selectedDesktop = previous;
            OnPropertyChanged(nameof(SelectedDesktop));
        }
    }
}

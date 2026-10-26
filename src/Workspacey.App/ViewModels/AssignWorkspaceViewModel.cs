using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;
using Workspacey.Core.Services;

namespace Workspacey.App.ViewModels;

/// <summary>The popup shown after a window was picked.</summary>
public sealed partial class AssignWorkspaceViewModel(
    IVirtualDesktopService desktops,
    WorkspaceAssignmentService assignments,
    ILogger<AssignWorkspaceViewModel> logger) : ViewModelBase
{
    private WindowInfo? _window;

    /// <summary>The rule announced as "Saving updates that rule"; saving must update exactly this one.</summary>
    private string? _existingRuleId;

    /// <summary>Raised when the dialog should close; the argument tells whether a rule was saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    public ObservableCollection<VirtualDesktop> Desktops { get; } = [];

    /// <summary>With a single workspace there is nowhere to send the window; suggest creating one.</summary>
    public bool SuggestNewDesktop => Desktops.Count <= 1;

    public IReadOnlyList<Choice<ClassMatchMode>> MatchModes { get; } =
    [
        new(ClassMatchMode.Exact, "Exact match"),
        new(ClassMatchMode.Substring, "Contains"),
        new(ClassMatchMode.Regex, "Regular expression"),
    ];

    public IReadOnlyList<Choice<RulePolicy>> Policies { get; } =
    [
        new(RulePolicy.ApplyInitially, "Open there (can be moved afterwards)"),
        new(RulePolicy.Force, "Always keep it there (force)"),
    ];

    public bool Saved { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ExistingRuleText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial VirtualDesktop? SelectedDesktop { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string WindowClass { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Choice<ClassMatchMode> SelectedMatchMode { get; set; } = null!;

    [ObservableProperty]
    public partial Choice<RulePolicy> SelectedPolicy { get; set; } = null!;

    [ObservableProperty]
    public partial bool MoveWindowNow { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddDesktopCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddDesktopCommand))]
    public partial string NewDesktopName { get; set; } = string.Empty;

    public async Task LoadAsync(WindowInfo window)
    {
        _window = window;
        Caption = new[] { window.Caption, window.MatchClass, window.ResourceName }
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "(unnamed window)";
        WindowClass = window.MatchClass;
        SelectedMatchMode = MatchModes[0];
        SelectedPolicy = Policies[0];

        if (string.IsNullOrEmpty(window.MatchClass))
        {
            // KWin matches class rules against the resource class only; the resource name won't do.
            ErrorMessage = "This window reports no window class, so a KWin rule cannot recognise it. " +
                           "Enter a class under Matching only if you know what to match.";
        }

        try
        {
            Desktops.Clear();
            foreach (var desktop in await desktops.GetDesktopsAsync()) Desktops.Add(desktop);
            OnPropertyChanged(nameof(SuggestNewDesktop));

            var existing = await assignments.FindRuleForAsync(window);
            _existingRuleId = existing?.Id;
            if (existing is not null)
            {
                SelectedDesktop = Desktops.FirstOrDefault(d => existing.DesktopIds.Contains(d.Id));
                SelectedMatchMode = MatchModes.First(m => m.Value == existing.MatchMode);
                SelectedPolicy = Policies.First(p => p.Value == existing.Policy);
                WindowClass = existing.WindowClass;
                ExistingRuleText = $"Already assigned to {SelectedDesktop?.DisplayName ?? "an unknown workspace"} " +
                                   $"by rule \"{existing.Description}\". Saving updates that rule.";
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Loading workspaces failed");
            ErrorMessage = $"Could not read workspaces from KWin: {e.Message}";
        }
    }

    /// <summary>Keyboard shortcut in the popup: pressing 1..9 selects that workspace (Enter then saves).</summary>
    public bool TrySelectByPosition(int position)
    {
        var desktop = Desktops.FirstOrDefault(d => d.Position == position);
        if (desktop is null) return false;

        SelectedDesktop = desktop;
        return true;
    }

    private bool CanAddDesktop() => !IsBusy && !string.IsNullOrWhiteSpace(NewDesktopName);

    private bool CanSave() => !IsBusy && SelectedDesktop is not null && !string.IsNullOrWhiteSpace(WindowClass);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_window is null || SelectedDesktop is null) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await assignments.AssignAsync(new AssignmentRequest(
                _window,
                SelectedDesktop.Id,
                WindowClass.Trim(),
                SelectedMatchMode.Value,
                SelectedPolicy.Value,
                MoveWindowNow,
                _existingRuleId));

            Saved = true;
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Saving the window rule failed");
            ErrorMessage = $"Saving failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Creates a workspace and selects it, so Enter afterwards assigns the window to it.</summary>
    [RelayCommand(CanExecute = nameof(CanAddDesktop))]
    private async Task AddDesktopAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var created = await desktops.CreateDesktopAsync(NewDesktopName.Trim());
            Desktops.Add(created);
            OnPropertyChanged(nameof(SuggestNewDesktop));
            SelectedDesktop = created;
            NewDesktopName = string.Empty;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Creating a workspace failed");
            ErrorMessage = $"Creating the workspace failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}

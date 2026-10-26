using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Workspacey.App.ViewModels;
using Workspacey.App.Views;
using Workspacey.Core.Abstractions;

namespace Workspacey.App.Services;

public interface IPickWorkflow
{
    /// <summary>
    /// Select a window with the crosshair cursor, then ask which workspace it belongs on.
    /// Returns <c>true</c> if a rule was saved.
    /// </summary>
    Task<bool> RunAsync();
}

public sealed class PickWorkflow(
    IWindowPicker picker,
    Func<AssignWorkspaceViewModel> createViewModel,
    ILogger<PickWorkflow> logger) : IPickWorkflow
{
    public async Task<bool> RunAsync()
    {
        var window = await picker.PickWindowAsync();
        if (window is null) return false;
        logger.LogInformation("Picked '{Caption}' (class '{Class}')", window.Caption, window.MatchClass);

        var viewModel = createViewModel();
        await viewModel.LoadAsync(window);
        var dialog = new AssignWorkspaceWindow { DataContext = viewModel };

        // Modal over the settings window when it is open; standalone when started by the shortcut.
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is { IsVisible: true }) return await dialog.ShowDialog<bool>(owner);

        var closed = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => closed.TrySetResult(viewModel.Saved);
        dialog.Show();
        dialog.Activate();
        return await closed.Task;
    }
}

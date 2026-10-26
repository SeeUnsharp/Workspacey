using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Workspacey.App.ViewModels;

namespace Workspacey.App.Views;

public sealed partial class AssignWorkspaceWindow : Window
{
    private AssignWorkspaceViewModel? _viewModel;

    public AssignWorkspaceWindow()
    {
        InitializeComponent();

        // Tunnel: the ListBox would otherwise swallow digits (type-to-search) and Enter.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        DesktopList.DoubleTapped += (_, _) => Save();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null) _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel = DataContext as AssignWorkspaceViewModel;
        if (_viewModel is not null) _viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // With a single workspace the next step is creating one, so start typing its name right away.
        if (_viewModel?.SuggestNewDesktop == true) NewDesktopNameBox.Focus();
        else DesktopList.Focus();
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.KeyModifiers != KeyModifiers.None) return;

        // Enter in the "new workspace" box creates it; the list then gets focus so the next Enter saves.
        if (e.Source == NewDesktopNameBox && e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            if (!_viewModel.AddDesktopCommand.CanExecute(null)) return; // name is required
            await _viewModel.AddDesktopCommand.ExecuteAsync(null);
            DesktopList.Focus();
            return;
        }

        if (IsEditingOrChoosing(e.Source)) return;

        if (e.Key is Key.Enter or Key.Return)
        {
            Save();
            e.Handled = true;
            return;
        }

        var position = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };

        if (position >= 0 && _viewModel.TrySelectByPosition(position))
        {
            DesktopList.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Keys typed into the class text box or an open combo box belong to those controls.</summary>
    private static bool IsEditingOrChoosing(object? source) =>
        source is TextBox ||
        (source as Visual)?.FindAncestorOfType<ComboBox>(includeSelf: true) is { IsDropDownOpen: true } ||
        source is ComboBoxItem;

    private void Save()
    {
        if (_viewModel?.SaveCommand.CanExecute(null) == true) _viewModel.SaveCommand.Execute(null);
    }

    private void OnCloseRequested(object? sender, bool saved) => Close(saved);
}

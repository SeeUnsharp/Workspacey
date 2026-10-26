using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Workspacey.App.Input;
using Workspacey.App.ViewModels;

namespace Workspacey.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Tunnel so the recorder sees keys before buttons react to Space/Enter.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => ViewModel?.StopRecording();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { } viewModel) await viewModel.LoadCommand.ExecuteAsync(null);
    }

    // Recording pauses all global shortcuts; make sure they come back whenever the user leaves.
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (ViewModel is not { IsRecording: true } viewModel) return;

        // Closing ends the process: wait until kglobalaccel has resumed the shortcuts, then close for real.
        e.Cancel = true;
        await viewModel.StopRecordingAsync();
        Close();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsRecording: true } viewModel)
        {
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;
        var cancel = e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None;
        viewModel.OnKeyRecorded(cancel ? null : KeyChordMapper.Map(e.Key, e.KeyModifiers), cancel);
    }
}

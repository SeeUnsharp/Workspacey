using CommunityToolkit.Mvvm.ComponentModel;

namespace Workspacey.App.ViewModels;

public abstract class ViewModelBase : ObservableObject;

/// <summary>A value with a user facing label, for combo boxes over enums.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

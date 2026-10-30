using System.Windows.Input;

namespace CodeForge;

/// <summary>A command backed by two lambdas, for menus, toolbar buttons and keyboard shortcuts.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
}

/// <summary>A row in the Error List.</summary>
public sealed record ErrorRow(Core.Diagnostic Diagnostic, string FullPath)
{
    public string Icon => Diagnostic.Severity == Core.Severity.Error ? "" : "";
    public System.Windows.Media.Brush IconBrush => Themes.Brush(Diagnostic.Severity == Core.Severity.Error ? "#E51400" : "#CCA700");
    public string Code => Diagnostic.Code ?? "";
    public string Message => Diagnostic.Message;
    public string FileName => System.IO.Path.GetFileName(FullPath);
    public int Line => Diagnostic.Line;
}

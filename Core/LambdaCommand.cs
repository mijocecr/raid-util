using System;
using System.Windows.Input;

namespace RAID_Util.Core;

public class LambdaCommand : ICommand
{
    private readonly Action _action;

    public LambdaCommand(Action action)
    {
        _action = action;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _action();

    public event EventHandler? CanExecuteChanged;
}

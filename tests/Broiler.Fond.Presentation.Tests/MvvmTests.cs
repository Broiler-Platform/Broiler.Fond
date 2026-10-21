using Broiler.Fond.Presentation.Mvvm;

namespace Broiler.Fond.Presentation.Tests;

internal static class MvvmTests
{
    private sealed class TestViewModel : ObservableObject
    {
        private string _name = "Initial";
        private int _counter;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Counter
        {
            get => _counter;
            set => SetProperty(_counter, value, val => _counter = val);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // 1. ObservableObject property changed & changing notifications
        var vm = new TestViewModel();
        string? changingName = null;
        string? changedName = null;
        vm.PropertyChanging += (s, e) => changingName = e.PropertyName;
        vm.PropertyChanged += (s, e) => changedName = e.PropertyName;

        vm.Name = "Updated";
        check(changingName == "Name", "PropertyChanging fired with property name.");
        check(changedName == "Name", "PropertyChanged fired with property name.");
        check(vm.Name == "Updated", "Property value was updated.");

        // No-op when setting same value
        changingName = null;
        changedName = null;
        vm.Name = "Updated";
        check(changingName is null, "PropertyChanging not fired when value unchanged.");
        check(changedName is null, "PropertyChanged not fired when value unchanged.");

        // Callback property setting
        vm.Counter = 42;
        check(changingName == "Counter", "PropertyChanging fired for callback setter.");
        check(changedName == "Counter", "PropertyChanged fired for callback setter.");
        check(vm.Counter == 42, "Counter value was updated via callback.");

        // 2. RelayCommand
        bool executed = false;
        bool canExecute = true;
        var cmd = new RelayCommand(() => executed = true, () => canExecute);

        check(cmd.CanExecute(null), "RelayCommand CanExecute returns true.");
        cmd.Execute(null);
        check(executed, "RelayCommand executed action.");

        canExecute = false;
        executed = false;
        check(!cmd.CanExecute(null), "RelayCommand CanExecute returns false when guarded.");
        cmd.Execute(null);
        check(!executed, "RelayCommand does not execute when CanExecute is false.");

        bool canExecuteChangedFired = false;
        cmd.CanExecuteChanged += (s, e) => canExecuteChangedFired = true;
        cmd.NotifyCanExecuteChanged();
        check(canExecuteChangedFired, "NotifyCanExecuteChanged fires CanExecuteChanged event.");

        // 3. Generic RelayCommand<T>
        string? receivedParam = null;
        var genericCmd = new RelayCommand<string>(p => receivedParam = p, p => p != "forbidden");
        check(genericCmd.CanExecute("allowed"), "Generic command CanExecute returns true for allowed.");
        check(!genericCmd.CanExecute("forbidden"), "Generic command CanExecute returns false for forbidden.");
        genericCmd.Execute("test-param");
        check(receivedParam == "test-param", "Generic command received parameter.");

        // 4. AsyncRelayCommand
        bool asyncExecuted = false;
        var asyncCmd = new AsyncRelayCommand(async ct =>
        {
            await Task.Yield();
            asyncExecuted = true;
        });

        check(!asyncCmd.IsExecuting, "AsyncRelayCommand is initially not executing.");
        Task t = asyncCmd.ExecuteAsync();
        t.GetAwaiter().GetResult();
        check(asyncExecuted, "AsyncRelayCommand executed asynchronously.");
        check(!asyncCmd.IsExecuting, "AsyncRelayCommand IsExecuting returns to false after completion.");
    }
}

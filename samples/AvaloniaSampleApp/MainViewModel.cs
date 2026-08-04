using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AvaloniaSampleApp;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _name = "";
    private string _greeting = "Hello!";

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string Greeting
    {
        get => _greeting;
        private set { if (_greeting != value) { _greeting = value; OnPropertyChanged(); } }
    }

    public ICommand GreetCommand { get; }

    public MainViewModel()
    {
        GreetCommand = new RelayCommand(() =>
            Greeting = string.IsNullOrWhiteSpace(Name) ? "Hello!" : $"Hello, {Name}!");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class RelayCommand : ICommand
{
    private readonly System.Action _execute;
    public RelayCommand(System.Action execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
    public event System.EventHandler? CanExecuteChanged { add { } remove { } }
}

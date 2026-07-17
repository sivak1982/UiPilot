using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SampleApp;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _name = "World";
    private string _greeting = "";

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

    public ObservableCollection<string> History { get; } = new ObservableCollection<string>();

    public ICommand GreetCommand { get; }

    public MainViewModel()
    {
        GreetCommand = new RelayCommand(Greet);
    }

    private void Greet()
    {
        Greeting = $"Hello, {Name}!";
        History.Insert(0, $"{DateTime.Now:HH:mm:ss} - {Greeting}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}

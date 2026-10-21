using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Broiler.Fond.Presentation.Mvvm;

/// <summary>
/// Lightweight, reflection-free base class providing compiled property change notification
/// compliant with Native AOT trimming requirements.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void OnPropertyChanging([CallerMemberName] string? propertyName = null)
    {
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        OnPropertyChanging(propertyName);
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected bool SetProperty<T>(T oldValue, T newValue, Action<T> callback, [CallerMemberName] string? propertyName = null)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return false;
        }

        OnPropertyChanging(propertyName);
        callback(newValue);
        OnPropertyChanged(propertyName);
        return true;
    }
}

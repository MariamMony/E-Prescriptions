using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
namespace CommunityToolkit.Mvvm.ComponentModel
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? n = null)
        { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(n); return true; }
    }
}
namespace CommunityToolkit.Mvvm.Input
{
    public interface IRelayCommand : System.Windows.Input.ICommand { void NotifyCanExecuteChanged(); }
    public class RelayCommand : IRelayCommand
    {
        readonly Action _a; readonly Func<bool>? _c;
        public RelayCommand(Action a, Func<bool>? c = null) { _a = a; _c = c; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => _c?.Invoke() ?? true;
        public void Execute(object? p) => _a();
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    public class RelayCommand<T> : IRelayCommand<T>
    {
        readonly Action<T?> _a; readonly Predicate<T?>? _c;
        public RelayCommand(Action<T?> a, Predicate<T?>? c = null) { _a = a; _c = c; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => _c?.Invoke((T?)p) ?? true;
        public void Execute(object? p) => _a((T?)p);
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
namespace System.Windows.Input { public interface ICommand { event EventHandler? CanExecuteChanged; bool CanExecute(object? p); void Execute(object? p); } }
namespace System.Windows
{
    public enum MessageBoxButton { OK, OKCancel, YesNoCancel = 3, YesNo }
    public enum MessageBoxImage { None, Error = 16, Warning = 48, Information = 64, Question = 32 }
    public enum MessageBoxResult { None, OK, Cancel, Yes = 6, No }
    [Flags] public enum MessageBoxOptions { None = 0, RightAlign = 0x80000, RtlReading = 0x100000 }
    public static class MessageBox
    {
        public static MessageBoxResult Show(string t) => MessageBoxResult.OK;
        public static MessageBoxResult Show(string t, string c) => MessageBoxResult.OK;
        public static MessageBoxResult Show(string t, string c, MessageBoxButton b) => MessageBoxResult.OK;
        public static MessageBoxResult Show(string t, string c, MessageBoxButton b, MessageBoxImage i) => MessageBoxResult.OK;
        public static MessageBoxResult Show(string t, string c, MessageBoxButton b, MessageBoxImage i, MessageBoxResult d) => d;
        public static MessageBoxResult Show(string t, string c, MessageBoxButton b, MessageBoxImage i, MessageBoxResult d, MessageBoxOptions o) => d;
    }
}
namespace System.Windows.Threading
{
    public class DispatcherTimer { public TimeSpan Interval; public event EventHandler? Tick; public void Start() { } public void Stop() { } public bool IsEnabled => false; }
}
namespace System.Media { public class SystemSound { public void Play() { } } public static class SystemSounds { public static SystemSound Beep = new(); public static SystemSound Asterisk = new(); public static SystemSound Exclamation = new(); public static SystemSound Hand = new(); public static SystemSound Question = new(); } }
namespace CommunityToolkit.Mvvm.Input { public interface IRelayCommand<T> : IRelayCommand { } }
namespace ePrescriptions.App.Services
{
    public interface IPdfReportService { string GenerateA4InvoicePdf(ePrescriptions.Domain.Entities.Invoice invoice, ePrescriptions.Domain.Entities.PharmacySettings settings); }
}
namespace ePrescriptions.App.Services { public static class ThermalReceiptPrinter {
  public static bool PrintReceipt(ePrescriptions.Domain.Entities.Invoice i, ePrescriptions.Domain.Entities.PharmacySettings s, out string err) { err = ""; return true; }
  public static bool OpenCashDrawer(ePrescriptions.Domain.Entities.PharmacySettings s, out string err) { err = ""; return true; } } }
namespace System.Windows { public class Application { public static Application? Current => null; public void Shutdown() { } } }

using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;

namespace ePrescriptions.App.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IUserSessionService _sessionService;

        private string _username = "admin";
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private bool _isLockMode = false;
        public bool IsLockMode
        {
            get => _isLockMode;
            set => SetProperty(ref _isLockMode, value);
        }

        private string _currentUserDisplay = string.Empty;
        public string CurrentUserDisplay
        {
            get => _currentUserDisplay;
            set => SetProperty(ref _currentUserDisplay, value);
        }

        private string _loginErrorMessage = string.Empty;
        public string LoginErrorMessage
        {
            get => _loginErrorMessage;
            set => SetProperty(ref _loginErrorMessage, value);
        }

        public IRelayCommand SubmitCommand { get; }
        public IRelayCommand SwitchUserCommand { get; }
        public IRelayCommand ExitAppCommand { get; }

        public event Action? LoginSucceeded;

        public LoginViewModel(IUserSessionService sessionService)
        {
            _sessionService = sessionService;
            Title = "تسجيل الدخول وقفل الشاشة";

            SubmitCommand = new RelayCommand(ExecuteSubmit);
            SwitchUserCommand = new RelayCommand(ExecuteSwitchUser);
            ExitAppCommand = new RelayCommand(() => Application.Current.Shutdown());

            UpdateState();
            _sessionService.LockStateChanged += locked => UpdateState();
        }

        public void SetMode(bool lockMode)
        {
            IsLockMode = lockMode;
            Password = string.Empty;
            LoginErrorMessage = string.Empty;
            UpdateState();
        }

        private void UpdateState()
        {
            if (_sessionService.IsLocked && _sessionService.CurrentUser != null)
            {
                IsLockMode = true;
                CurrentUserDisplay = _sessionService.CurrentUser.FullName;
                Username = _sessionService.CurrentUser.Username;
            }
            else
            {
                IsLockMode = false;
                CurrentUserDisplay = string.Empty;
            }
        }

        private void ExecuteSubmit()
        {
            LoginErrorMessage = string.Empty;

            if (IsLockMode)
            {
                if (_sessionService.Unlock(Password, out string error))
                {
                    Password = string.Empty;
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    LoginErrorMessage = error;
                }
            }
            else
            {
                if (_sessionService.Login(Username, Password, out string error))
                {
                    Password = string.Empty;
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    LoginErrorMessage = error;
                }
            }
        }

        private void ExecuteSwitchUser()
        {
            _sessionService.Logout();
            IsLockMode = false;
            Username = string.Empty;
            Password = string.Empty;
            LoginErrorMessage = string.Empty;
        }
    }
}

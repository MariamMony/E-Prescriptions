using System.Windows;
using System.Windows.Controls;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class FirstRunSetupWindow : Window
    {
        private readonly FirstRunSetupViewModel _viewModel;

        public FirstRunSetupWindow(FirstRunSetupViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.SetupCompleted += () =>
            {
                DialogResult = true;
                Close();
            };
        }

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb) _viewModel.Password = pb.Password;
        }

        private void TxtConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb) _viewModel.ConfirmPassword = pb.Password;
        }
    }
}

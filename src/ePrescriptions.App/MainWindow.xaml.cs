using System.Windows;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

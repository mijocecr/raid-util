using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RAID_Util.Views
{
    public partial class IncorrectPasswordDialog : Window
    {
        public IncorrectPasswordDialog()
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private void OnOk(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
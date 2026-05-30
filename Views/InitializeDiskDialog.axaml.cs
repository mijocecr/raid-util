using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RAID_Util.Views
{
    public partial class InitializeDiskDialog : Window
    {
        public string SelectedLabel { get; private set; } = "";
        public string SelectedFs { get; private set; } = "";

        public InitializeDiskDialog()
        {
            InitializeComponent();

            ApplyBtn.Click += OnApply;
            CancelBtn.Click += (_, _) => Close(null);
        }

        private void OnApply(object? sender, RoutedEventArgs e)
        {
            SelectedLabel = LabelBox.Text?.Trim() ?? "";
            SelectedFs = (FsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(SelectedLabel) || string.IsNullOrWhiteSpace(SelectedFs))
            {
                Close(null);
                return;
            }

            Close(this);
        }
    }
}
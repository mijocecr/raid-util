using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RAID_Util.Views;

public sealed class InitializeDiskResult
{
    public string Label { get; }
    public string FileSystem { get; }

    public InitializeDiskResult(string label, string fileSystem)
    {
        Label = label;
        FileSystem = fileSystem;
    }
}

public partial class InitializeDiskDialog : Window
{
    public InitializeDiskDialog()
    {
        InitializeComponent();

        ApplyBtn.Click += OnApply;
        CancelBtn.Click += (_, _) => Close(null);
    }

    public string SelectedLabel { get; private set; } = "";
    public string SelectedFs { get; private set; } = "";

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        SelectedLabel = LabelBox.Text?.Trim() ?? "";
        SelectedFs = (FsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(SelectedLabel) ||
            string.IsNullOrWhiteSpace(SelectedFs))
        {
            // No cerrar con datos inválidos: devolver null
            Close(null);
            return;
        }

        // Devolver un resultado claro y tipado
        var result = new InitializeDiskResult(SelectedLabel, SelectedFs);
        Close(result);
    }
}
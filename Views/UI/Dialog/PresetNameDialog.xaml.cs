using System.Windows;
using System.Windows.Input;
using MFAWPF.Views.UserControl;

namespace MFAWPF.Views.UI.Dialog;

/// <summary>
/// Dialog for entering or modifying a task sequence preset name.
/// </summary>
public partial class PresetNameDialog : CustomWindow
{
    public string PresetName { get; private set; } = string.Empty;

    public PresetNameDialog(string title, string initialName)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        PresetNameTextBox.Text = initialName ?? string.Empty;
    }

    private void PresetNameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        PresetNameTextBox.Focus();
        PresetNameTextBox.SelectAll();
    }

    private void PresetNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        PresetName = PresetNameTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

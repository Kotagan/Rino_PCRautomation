using System.Windows;
using System.Windows.Controls;
using MFAWPF.Helper;
using MFAWPF.Models;
using MFAWPF.ViewModels.UI;

namespace MFAWPF.Views.UI;

public partial class TaskSequenceView
{
    public TaskSequenceViewModel ViewModel { get; }

    public TaskSequenceView()
    {
        ViewModel = Instances.TaskSequenceViewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshTasksFromQueue();
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: TaskPreset preset })
        {
            ViewModel.DeletePresetCommand.Execute(preset);
        }
    }

    private void OnOrderTextBoxPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void OnOrderTextBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is TextBox textBox)
        {
            var binding = System.Windows.Data.BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            binding?.UpdateSource();
            System.Windows.Input.Keyboard.ClearFocus();
        }
    }
}

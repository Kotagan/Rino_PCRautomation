using System.Windows;
using System.Windows.Controls;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.Models;
using MFAWPF.ViewModels.UI;
using MFAWPF.Views.UI.Dialog;

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

    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var focusedElement = System.Windows.Input.FocusManager.GetFocusedElement(this) as UIElement;
        if (focusedElement is TextBox textBox)
        {
            var binding = System.Windows.Data.BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            binding?.UpdateSource();
        }
        System.Windows.Input.Keyboard.ClearFocus();

        ViewModel.ApplyPreset();
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: TaskPreset preset })
        {
            ViewModel.DeletePreset(preset);
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var focusedElement = System.Windows.Input.FocusManager.GetFocusedElement(this) as UIElement;
        if (focusedElement is TextBox textBox)
        {
            var binding = System.Windows.Data.BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            binding?.UpdateSource();
        }
        System.Windows.Input.Keyboard.ClearFocus();

        if (ViewModel.SelectedPreset == null)
        {
            var dialog = new PresetNameDialog("SavePreset".ToLocalization(), string.Empty)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PresetName))
            {
                ViewModel.CreatePreset(dialog.PresetName);
            }
            return;
        }

        ViewModel.SaveCurrentPreset();
    }

    private void DeletePresetActionButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteCurrentPreset();
    }

    private void NewPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PresetNameDialog("NewPreset".ToLocalization(), string.Empty)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PresetName))
        {
            ViewModel.CreatePreset(dialog.PresetName);
        }
    }

    private void RenamePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPreset == null)
        {
            return;
        }

        var dialog = new PresetNameDialog("RenamePreset".ToLocalization(), ViewModel.SelectedPreset.Name)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PresetName))
        {
            ViewModel.RenamePreset(ViewModel.SelectedPreset, dialog.PresetName);
        }
    }
}

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
}

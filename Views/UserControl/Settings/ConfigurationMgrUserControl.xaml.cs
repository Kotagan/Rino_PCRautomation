using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MFAWPF.Configuration;

namespace MFAWPF.Views.UserControl.Settings;

public partial class ConfigurationMgrUserControl
{
    public ConfigurationMgrUserControl()
    {
        InitializeComponent();
    }

    private void DeleteConfigurationButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: MFAConfiguration configuration } && configuration.IsDeletable)
        {
            e.Handled = true;
            configuration.DeleteConfigurationCommand.Execute(configuration);
        }
    }

    private void DeleteConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MFAConfiguration configuration } && configuration.IsDeletable)
        {
            configuration.DeleteConfigurationCommand.Execute(configuration);
        }
    }
}

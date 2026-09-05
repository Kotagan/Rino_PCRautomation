using MFAWPF.Helper;
using MFAWPF.ViewModels.UserControl.Settings;
using System.Windows.Controls;

namespace MFAWPF.Views.UserControl.Settings;

public partial class GuiSettingsUserControl
{
    public GuiSettingsUserControlModel ViewModel { get; set; }
    private bool _isHandlingThemeChange;

    public GuiSettingsUserControl()
    {
        ViewModel = Instances.GuiSettingsUserControlModel;
        DataContext = this;
        InitializeComponent();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isHandlingThemeChange) return;

        int newIndex = ThemeComboBox.SelectedIndex;
        if (newIndex < 0 || newIndex == ViewModel.ThemeIndex) return;

        _isHandlingThemeChange = true;
        ViewModel.SuppressThemeUpdate = true;
        ViewModel.ThemeIndex = newIndex;
        ViewModel.SuppressThemeUpdate = false;

        ThemeTransitionHelper.AnimateThemeSwitch(ThemeComboBox, () =>
        {
            ViewModel.ApplyTheme(newIndex);
            _isHandlingThemeChange = false;
        });
    }
}


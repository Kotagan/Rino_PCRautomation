using MFAWPF.Helper;
using MFAWPF.ViewModels.UserControl.Settings;

namespace MFAWPF.Views.UserControl.Settings;

public partial class SoftwareNotificationSettingsUserControl
{
    public SoftwareNotificationSettingsUserControlModel ViewModel { get; set; }

    public SoftwareNotificationSettingsUserControl()
    {
        ViewModel = Instances.SoftwareNotificationSettingsUserControlModel;
        DataContext = this;
        InitializeComponent();
    }
}

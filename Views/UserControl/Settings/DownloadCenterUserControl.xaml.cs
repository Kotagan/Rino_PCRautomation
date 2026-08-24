using System.Windows;
using System.Windows.Controls;
using MFAWPF.ViewModels.UserControl.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MFAWPF.Views.UserControl.Settings;

public partial class DownloadCenterUserControl : System.Windows.Controls.UserControl
{
    internal DownloadCenterViewModel ViewModel { get; }

    public DownloadCenterUserControl()
    {
        ViewModel = App.Services.GetRequiredService<DownloadCenterViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        IsVisibleChanged += async (_, eventArgs) =>
        {
            if (eventArgs.NewValue is bool isVisible && isVisible)
            {
                await ViewModel.EnsureLoadedAsync();
            }
        };
    }
}

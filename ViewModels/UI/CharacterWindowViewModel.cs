using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace MFAWPF.ViewModels.UI;

internal partial class CharacterWindowViewModel : ViewModel
{
    [ObservableProperty] private ImageSource? _currentFrame;

    [ObservableProperty] private double _characterSize = 180d;

    [ObservableProperty] private double _displayHeight = 180d;

    [ObservableProperty] private double _mirrorScale = 1d;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private bool _isStatusVisible;
}

using MFAWPF.ViewModels.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MFAWPF.Views.UI;

/// <summary>
/// Provides configuration and preview controls for the desktop character.
/// </summary>
internal partial class CharacterView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterView"/> class.
    /// </summary>
    public CharacterView(CharacterViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CharacterViewModel viewModel)
        {
            return;
        }

        viewModel.RefreshTaskOptions();
        Canvas.SetLeft(
            FootAnchorMarker,
            viewModel.SelectedProfile.FootAnchorX * PreviewSurface.Width -
            FootAnchorMarker.Width / 2d);
        Canvas.SetTop(
            FootAnchorMarker,
            viewModel.SelectedProfile.FootAnchorY * PreviewSurface.Height -
            FootAnchorMarker.Height / 2d);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CharacterViewModel viewModel ||
            PreviewSurface.ActualWidth <= 0d ||
            PreviewSurface.ActualHeight <= 0d)
        {
            return;
        }

        var point = e.GetPosition(PreviewSurface);
        viewModel.SetFootAnchor(
            point.X / PreviewSurface.ActualWidth,
            point.Y / PreviewSurface.ActualHeight);
        Canvas.SetLeft(FootAnchorMarker, point.X - FootAnchorMarker.Width / 2d);
        Canvas.SetTop(FootAnchorMarker, point.Y - FootAnchorMarker.Height / 2d);
    }
}

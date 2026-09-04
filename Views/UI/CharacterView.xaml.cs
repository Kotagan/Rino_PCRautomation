using MFAWPF.ViewModels.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MFAWPF.Views.UI;

/// <summary>
/// Provides configuration and preview controls for the desktop character.
/// </summary>
internal partial class CharacterView
{
    private FrameworkElement[]? _sections;
    private bool _isSynchronizingNavigation;

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
        viewModel.SetPreviewActive(true);
        Canvas.SetLeft(
            FootAnchorMarker,
            viewModel.SelectedProfile.FootAnchorX * PreviewSurface.Width -
            FootAnchorMarker.Width / 2d);
        Canvas.SetTop(
            FootAnchorMarker,
            viewModel.SelectedProfile.FootAnchorY * PreviewSurface.Height -
            FootAnchorMarker.Height / 2d);

        EnsureSections();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, SynchronizeSectionSelection);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterViewModel viewModel)
        {
            viewModel.SetPreviewActive(false);
        }
    }

    private void OnSectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        EnsureSections();
        if (_isSynchronizingNavigation ||
            _sections is null ||
            SectionList.SelectedIndex < 0 ||
            SectionList.SelectedIndex >= _sections.Length)
        {
            return;
        }

        var section = _sections[SectionList.SelectedIndex];
        var position = section.TranslatePoint(new Point(), SettingsSectionsPanel);
        SettingsViewer.ScrollToVerticalOffset(position.Y);
    }

    private void OnSettingsViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (e.VerticalChange != 0d || e.HorizontalChange != 0d)
        {
            Helper.Animations.ApplePopupAnimationBehavior.CloseAllOpenPopups(SettingsViewer);
        }

        SynchronizeSectionSelection();
    }

    private void EnsureSections() =>
        _sections ??=
        [
            ProfileSection,
            PreviewSection,
            MovementSection,
            AnimationSection,
            TaskRulesSection,
        ];

    private void SynchronizeSectionSelection()
    {
        EnsureSections();
        if (_sections is null || _sections.Length == 0 || _isSynchronizingNavigation)
        {
            return;
        }

        var selectedIndex = 0;
        var viewportTop = SettingsViewer.VerticalOffset + 12d;
        for (var index = 0; index < _sections.Length; index++)
        {
            var position = _sections[index].TranslatePoint(new Point(), SettingsSectionsPanel);
            if (position.Y > viewportTop)
            {
                break;
            }

            selectedIndex = index;
        }

        if (SettingsViewer.VerticalOffset + SettingsViewer.ViewportHeight >=
            SettingsViewer.ExtentHeight - 2d)
        {
            selectedIndex = _sections.Length - 1;
        }

        if (SectionList.SelectedIndex == selectedIndex)
        {
            return;
        }

        _isSynchronizingNavigation = true;
        SectionList.SelectedIndex = selectedIndex;
        _isSynchronizingNavigation = false;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MFAWPF.Helper;
using MFAWPF.ViewModels.UI;

namespace MFAWPF.Views.UI;

public partial class SettingsView
{
    public SettingsViewModel ViewModel { get; set; }

    public SettingsView()
    {
        ViewModel = Instances.SettingsViewModel;
        DataContext = this;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateDividerPositions);
            Dispatcher.BeginInvoke(DispatcherPriority.Render, UpdateDividerPositions);
        };

        Viewer.SizeChanged += (_, _) => UpdateDividerPositions();

        if (Viewer.Content is Grid grid)
        {
            var stackPanel = grid.Children.OfType<StackPanel>().FirstOrDefault();
            if (stackPanel != null)
            {
                stackPanel.SizeChanged += (_, _) => UpdateDividerPositions();
            }
        }

        Viewer.ScrollChanged += (_, se) =>
        {
            if (ViewModel.DividerVerticalOffsetList.Count == 0 || ViewModel.DividerVerticalOffsetList.All(y => y == 0))
            {
                UpdateDividerPositions();
            }

            if (se.VerticalChange != 0 || se.HorizontalChange != 0)
            {
                Helper.Animations.ApplePopupAnimationBehavior.CloseAllOpenPopups(Viewer);
            }
        };

        Viewer.PreviewMouseWheel += (_, _) =>
        {
            Helper.Animations.ApplePopupAnimationBehavior.CloseAllOpenPopups(Viewer);
        };
    }

    private void UpdateDividerPositions()
    {
        if (Viewer.Content is Grid grid)
        {
            var stackPanel = grid.Children.OfType<StackPanel>().FirstOrDefault();
            if (stackPanel == null) return;

            double currentY = 0;
            var dividerPositions = new List<double>();
            foreach (var child in stackPanel.Children)
            {
                if (child is FrameworkElement element)
                {
                    dividerPositions.Add(currentY);
                    currentY += element.ActualHeight + element.Margin.Top + element.Margin.Bottom;
                }
            }

            if (dividerPositions.Count > 0)
            {
                ViewModel.DividerVerticalOffsetList = dividerPositions;
            }
        }
    }
}

using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WPFLocalizeExtension.Engine;

namespace MFAWPF.Views.UI;

public partial class NotifyIcon
{
    private readonly int _menuItemNum;

    public NotifyIcon()
    {
        InitializeComponent();

        InitIcon();

        if (notifyIcon.ContextMenu is not null)
        {
            _menuItemNum = notifyIcon.ContextMenu.Items.Count;
        }
    }

    private void InitIcon()
    {
        notifyIcon.Icon = IconHelper.ICON;
        notifyIcon.Visibility = ConfigurationHelper.GetValue(ConfigurationKeys.EnableShowIcon, true) ? Visibility.Visible : Visibility.Collapsed;

        notifyIcon.Click += NotifyIcon_MouseClick;
        notifyIcon.MouseDoubleClick += NotifyIcon_MouseClick;

        if (notifyIcon.ContextMenu is not null)
        {
            notifyIcon.ContextMenu.Opened += (_, _) => UpdateTrayMenuState();
        }

        headerMenu.Click += (_, _) => Instances.RootView.ShowWindow();
        windowActionMenu.Click += ToggleMainWindowVisibility;
        startMenu.Click += StartTask;
        stopMenu.Click += StopTask;
        tasksMenu.Click += (_, _) => Instances.RootView.SwitchToTaskSequenceTab();
        settingsMenu.Click += (_, _) => Instances.RootView.SwitchToSettingsTab();
        exitMenu.Click += App_exit;

        BuildLanguageSubmenu();
        LanguageHelper.LanguageChanged += (_, _) => UpdateTrayMenuState();
    }

    private void BuildLanguageSubmenu()
    {
        switchLangMenu.Items.Clear();

        foreach (var lang in LanguageHelper.SupportedLanguages)
        {
            var itemStyle = TryFindResource("TrayMenuItemStyle") as Style;
            var langMenu = new MenuItem
            {
                Header = lang.Name,
                Tag = lang.Key,
                Style = itemStyle
            };

            langMenu.Click += (_, _) =>
            {
                LanguageHelper.ChangeLanguage(lang);
                var index = LanguageHelper.SupportedLanguages.ToList().FindIndex(language => language.Key == lang.Key);
                ConfigurationHelper.SetValue(ConfigurationKeys.LangIndex, index == -1 ? 0 : index);
                UpdateTrayMenuState();
            };

            switchLangMenu.Items.Add(langMenu);
        }

        UpdateLanguageCheckmarks();
    }

    private void UpdateLanguageCheckmarks()
    {
        var currentLangKey = LocalizeDictionary.Instance.Culture?.Name?.ToLowerInvariant()
            ?? LanguageHelper.SupportedLanguages[0].Key;

        var checkGeometry = TryFindResource("TrayCheckGeometry") as Geometry;

        foreach (var obj in switchLangMenu.Items)
        {
            if (obj is not MenuItem item || item.Tag is not string langKey)
            {
                continue;
            }

            var isCurrent = string.Equals(langKey, currentLangKey, StringComparison.OrdinalIgnoreCase)
                || currentLangKey.StartsWith(langKey, StringComparison.OrdinalIgnoreCase);

            if (isCurrent && checkGeometry != null)
            {
                item.Icon = new Path
                {
                    Data = checkGeometry,
                    Stroke = Application.Current.FindResource("PrimaryBrush") as Brush,
                    StrokeThickness = 2,
                    Width = 12,
                    Height = 12,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                item.Icon = new Border
                {
                    Width = 12,
                    Height = 12
                };
            }
        }
    }

    private void UpdateTrayMenuState()
    {
        headerMenu.ApplyTemplate();

        var isRunning = Instances.RootViewModel.IsRunning;
        var isVisible = Instances.RootViewModel.IsVisible;

        // Update Header Status Badge
        var statusDot = headerMenu.Template?.FindName("StatusDot", headerMenu) as Ellipse;
        var statusText = headerMenu.Template?.FindName("StatusText", headerMenu) as TextBlock;

        if (isRunning)
        {
            if (statusDot != null)
            {
                statusDot.Fill = Application.Current.FindResource("PrimaryBrush") as Brush;
            }
            if (statusText != null)
            {
                statusText.Text = "StatusRunning".ToLocalization();
                statusText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            }
            startMenu.Visibility = Visibility.Collapsed;
            stopMenu.Visibility = Visibility.Visible;
        }
        else
        {
            if (statusDot != null)
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
            if (statusText != null)
            {
                statusText.Text = "StatusIdle".ToLocalization();
                statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            }
            startMenu.Visibility = Visibility.Visible;
            stopMenu.Visibility = Visibility.Collapsed;
        }

        // Update Window Action Toggle
        if (isVisible)
        {
            windowActionText.Text = "HideMainWindow".ToLocalization();
            if (Application.Current.FindResource("WindowMinGeometry") is Geometry minGeo)
            {
                windowActionIcon.Data = minGeo;
            }
        }
        else
        {
            windowActionText.Text = "OpenMainWindow".ToLocalization();
            if (Application.Current.FindResource("WindowRestoreGeometry") is Geometry restoreGeo)
            {
                windowActionIcon.Data = restoreGeo;
            }
        }

        UpdateLanguageCheckmarks();
    }

    // 不知道是干嘛的，先留着
    // ReSharper disable once UnusedMember.Local
    private void AddMenuItemOnFirst(string text, Action action)
    {
        var menuItem = new MenuItem
        {
            Header = text
        };
        menuItem.Click += (_, _) => { action?.Invoke(); };
        if (notifyIcon.ContextMenu is null)
        {
            return;
        }

        if (notifyIcon.ContextMenu.Items.Count == _menuItemNum)
        {
            notifyIcon.ContextMenu.Items.Insert(0, menuItem);
        }
        else
        {
            notifyIcon.ContextMenu.Items[0] = menuItem;
        }
    }

    private static void NotifyIcon_MouseClick(object sender, RoutedEventArgs e) =>
        Instances.RootView.ShowWindow();

    private void ToggleMainWindowVisibility(object sender, RoutedEventArgs e)
    {
        if (Instances.RootViewModel.IsVisible)
        {
            Instances.RootViewModel.IsVisible = false;
        }
        else
        {
            Instances.RootView.ShowWindow();
        }
    }

    private static void StartTask(object sender, RoutedEventArgs e) =>
        Instances.TaskQueueView.Start();

    private static void StopTask(object sender, RoutedEventArgs e) =>
        Instances.TaskQueueView.Stop();

    private static void App_exit(object sender, RoutedEventArgs e)
        => Instances.RootView.ExitApplication();

    private void App_hide(object sender, RoutedEventArgs e) =>
        Instances.RootViewModel.IsVisible = false;

    private void App_show(object sender, RoutedEventArgs e)
        => Instances.RootView.ShowWindow();
}

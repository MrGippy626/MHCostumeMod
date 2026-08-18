using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CostumeManager
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            WindowIcon.Apply(this);
            AppState.CaptureDispatcher();

            BtnLog.IsChecked = true;
            foreach (string line in AppState.LogLines) AppendLine(line);
            AppState.LogLines.CollectionChanged += OnLogChanged;

            RootGrid.Loaded += (s2, e2) =>
            {
                _ready = true;
                AppState.Log("Costume Manager started.");
            };

            ContentFrame.Navigate(typeof(Views.HomePage));
        }

        bool _ready;

        void AppendLine(string line)
        {
            var run = new Run { Text = line ?? "" };

            Brush b = LineBrush(line, out bool bold);
            if (b != null) run.Foreground = b;
            if (bold) run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

            var p = new Paragraph { Margin = new Thickness(0) };
            p.Inlines.Add(run);
            LogText.Blocks.Add(p);
        }

        Brush LineBrush(string line, out bool bold)
        {
            bold = false;
            if (string.IsNullOrEmpty(line)) return null;

            if (line.Contains("DONE", StringComparison.Ordinal))
            {
                bold = true;
                return App.Current.Resources["LogDoneInk"] as Brush;
            }
            if (line.Contains("⛔", StringComparison.Ordinal)
             || line.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
             || line.Contains("refusing", StringComparison.OrdinalIgnoreCase))
                return App.Current.Resources["LogErrorInk"] as Brush;

            if (line.Contains("⚠", StringComparison.Ordinal))
                return App.Current.Resources["WarnInk"] as Brush;

            return null;
        }

        const double MinLogHeight = 72;

        const double MinContentHeight = 220;

        bool _dragging;
        double _dragStartY;
        double _dragStartHeight;

        void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
        {

            if (LogScroll.Visibility != Visibility.Visible) return;

            _dragStartY = e.GetCurrentPoint(RootGrid).Position.Y;
            _dragStartHeight = LogScroll.ActualHeight;
            _dragging = ((UIElement)sender).CapturePointer(e.Pointer);
        }

        void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;

            double delta = e.GetCurrentPoint(RootGrid).Position.Y - _dragStartY;
            double want = _dragStartHeight - delta;

            double max = Math.Max(MinLogHeight, RootGrid.ActualHeight - MinContentHeight);
            LogScroll.Height = Math.Clamp(want, MinLogHeight, max);
        }

        void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        }

        Brush _gripRest;

        void Grip_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _gripRest ??= GripPill.Fill;
            GripPill.Fill = App.Current.Resources["AccentInk"] as Brush ?? _gripRest;
        }

        void Grip_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_gripRest != null) GripPill.Fill = _gripRest;
        }

        void OnLogChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                && e.NewItems != null)
            {
                foreach (object o in e.NewItems) AppendLine(o as string);
            }
            else
            {
                LogText.Blocks.Clear();
                foreach (string line in AppState.LogLines) AppendLine(line);
            }

            TxtLogCount.Text = AppState.LogLines.Count + " line(s)";
            if (!_ready) return;

            LogScroll.UpdateLayout();
            LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null, true);
        }

        void LogToggle(object sender, RoutedEventArgs e)
        {
            if (LogScroll == null) return;
            LogScroll.Visibility = (BtnLog.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        void ClearLog_Click(object sender, RoutedEventArgs e) => AppState.ClearLog();

        void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage
            {
                RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            };
            pkg.SetText(string.Join(Environment.NewLine, AppState.LogLines));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            AppState.Log($"log copied to the clipboard ({AppState.LogLines.Count} line(s))");
        }

        void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;

            Type page = (item.Tag as string) switch
            {
                "install"  => typeof(Views.InstallPage),
                "textures" => typeof(Views.TexturesPage),
                "icons"    => typeof(Views.IconsPage),
                "effects"  => typeof(Views.EffectsPage),
                "manage"   => typeof(Views.ManagePage),
                "repair"   => typeof(Views.RepairPage),
                "settings" => typeof(Views.SettingsPage),
                _          => typeof(Views.HomePage),
            };

            if (ContentFrame.CurrentSourcePageType != page)
                ContentFrame.Navigate(page);
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UsageNotch.App;

internal sealed class PolicyWindow : Window
{
    public PolicyWindow(string title, string file)
    {
        Style = (Style)Application.Current.FindResource("PeakWindowStyle");
        Title = "Peak · " + title; Width = 720; Height = 620;
        MinWidth = 500; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 22, 22));
        string text;
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Peak;component/Policies/" + file));
            using var reader = new StreamReader(resource!.Stream); text = reader.ReadToEnd();
        }
        catch { text = "This document is unavailable. Read the policies at https://peakforwindows.vercel.app or contact peakforwindows.support@gmail.com."; }
        Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24),
            Background = Brushes.Transparent, Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0), FontSize = 14 };
    }
}

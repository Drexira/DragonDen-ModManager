using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;

namespace DragonDen.ModManager.Services;

public sealed class Toasts
{
    private readonly ObservableCollection<object> items = new();
    private ItemsControl? host;

    public void Attach(ItemsControl hostControl)
    {
        host = hostControl;
        host.ItemsSource = items;
        host.ItemTemplate = new FuncDataTemplate<object>((obj, _) =>
        {
            if (obj is Toast t)
                return new Border
                {
                    Child = new TextBlock { Text = t.Text, Margin = new Thickness(10), Foreground = Brushes.White },
                    Background = new SolidColorBrush(0xCC000000),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 8)
                };
            if (obj is ToastProgress p)
            {
                var title = new TextBlock { Text = p.Title, Foreground = Brushes.White };

                var status = new TextBlock { Foreground = Brushes.LightGray };
                status.Bind(TextBlock.TextProperty, new Binding("Job.Status"));

                var bar = new ProgressBar { Minimum = 0, Maximum = 100 };
                bar.Bind(ProgressBar.IsIndeterminateProperty, new Binding("Job.IsIndeterminate"));
                bar.Bind(ProgressBar.ValueProperty, new Binding("Job.Progress"));

                return new Border
                {
                    Background = new SolidColorBrush(0xCC000000),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10),
                    Child = new StackPanel { Spacing = 6, Children = { title, status, bar } }
                };
            }

            return new TextBlock { Text = "toast?" };
        }, true);
    }

    public void Show(string text)
    {
        if (host == null) return;
        var t = new Toast { Text = text };
        items.Insert(0, t);
        _ = DismissLater(t, 3500);
    }

    public void ShowProgress(InstallJob job)
    {
        if (host == null) return;
        var p = new ToastProgress { Title = $"Installing {job.Title}", Job = job };
        items.Insert(0, p);
        _ = WatchJob(job, p);
    }

    private async Task WatchJob(InstallJob job, ToastProgress p)
    {
        while (!(job.Progress >= 100 && !job.IsIndeterminate))
            await Task.Delay(200);
        await Task.Delay(3000);
        items.Remove(p);
    }

    private async Task DismissLater(object t, int ms)
    {
        await Task.Delay(ms);
        items.Remove(t);
    }

    public sealed class Toast
    {
        public string Text { get; set; } = "";
    }

    public sealed class ToastProgress
    {
        public string Title { get; set; } = "";
        public int Progress { get; set; }
        public bool IsIndeterminate { get; set; }
        public string Status { get; set; } = "";
        public InstallJob? Job { get; set; }
        public bool Completed { get; set; }
    }
}
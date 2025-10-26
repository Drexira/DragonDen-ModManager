using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.Views;

public partial class InstallationQueueDialog : Window
{
    public InstallationQueueDialog()
    {
        InitializeComponent();

        CloseBtn.Click += (_, __) => Close();

        ClearCompletedBtn.Click += (_, __) =>
        {
            var done = App.Queue.Jobs.Where(j => j.IsCompleted).ToList();
            foreach (var j in done) App.Queue.Jobs.Remove(j);
            UpdateCounts();
            RefreshItems();
        };

        App.Queue.Jobs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (InstallJob j in e.NewItems)
                    WireJob(j);
            if (e.OldItems != null)
                foreach (InstallJob j in e.OldItems)
                    UnwireJob(j);

            UpdateCounts();
            RefreshItems();
        };

        foreach (var j in App.Queue.Jobs) WireJob(j);

        var template = new FuncDataTemplate<InstallJob>((job, _) =>
        {
            var title = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.Bind(TextBlock.TextProperty, new Binding("Title"));

            var status = new TextBlock { Foreground = Brushes.LightGray, TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = new TextBlock { Foreground = Brushes.LightGray, TextTrimming = TextTrimming.CharacterEllipsis };
            var eta = new TextBlock { Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Right };

            var mainBar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (IBrush)Application.Current!.Resources["Dd.Track"]
            };
            mainBar.Bind(ProgressBar.ValueProperty, new Binding("Progress"));
            mainBar.Bind(ProgressBar.IsIndeterminateProperty, new Binding("IsIndeterminate"));

            var subBar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (IBrush)Application.Current!.Resources["Dd.Track.Sub"]
            };
            subBar.Bind(ProgressBar.ValueProperty, new Binding("SubPercent"));
            subBar.Resources["ThemeAccentBrush"] = (IBrush)Application.Current!.Resources["Dd.Orange.Sub"];

            var size = new TextBlock { Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Right };
            size.Bind(TextBlock.TextProperty, new Binding("DoneBytes") { StringFormat = "{}{0:N0} B" });

            var actionBtn = new Button { IsVisible = false, MinWidth = 84, Padding = new Thickness(8, 4) };
            SetActionButtonState(actionBtn, job);
            job.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(job.IsCompleted))
                    SetActionButtonState(actionBtn, job);
            };
            actionBtn.Click += (_, __) =>
            {
                if (job.IsCompleted)
                {
                    App.Queue.Jobs.Remove(job);
                    UpdateCounts();
                    RefreshItems();
                }
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
                Children =
                {
                    title.With(c =>
                    {
                        Grid.SetRow(c, 0);
                        Grid.SetColumn(c, 0);
                    }),

                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Margin = new Thickness(0, 2, 0, 6),
                        Children =
                        {
                            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { status, sub } }
                                .With(c => Grid.SetColumn(c, 0)),
                            eta.With(c => Grid.SetColumn(c, 1))
                        }
                    }.With(c => Grid.SetRow(c, 1)),

                    mainBar.With(c =>
                    {
                        Grid.SetRow(c, 2);
                        Grid.SetColumnSpan(c, 2);
                    }),
                    subBar.With(c =>
                    {
                        Grid.SetRow(c, 3);
                        Grid.SetColumnSpan(c, 2);
                    }),

                    /*size.With(c =>
                    {
                        Grid.SetRow(c,4);
                        Grid.SetColumnSpan(c,2);
                        c.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                        c.Margin = new Thickness(0,4,0,0);
                    }),*/

                    actionBtn.With(c =>
                    {
                        Grid.SetRow(c, 0);
                        Grid.SetColumn(c, 1);
                        c.VerticalAlignment = VerticalAlignment.Top;
                    })
                }
            };

            var card = new Border
            {
                Classes = { "card" },
                Child = grid
            };

            void ApplyVisualState()
            {
                var current = Notifications.Current.CurrentInstall;

                card.Classes.Remove("state-active");
                card.Classes.Remove("state-queued");
                card.Classes.Remove("state-done");

                if (job.IsCompleted)
                {
                    card.Classes.Add("state-done");
                    status.Text = "Done Installing";
                    sub.Text = "";
                    eta.Text = "";
                    mainBar.IsVisible = false;
                    subBar.IsVisible = false;
                }
                else if (ReferenceEquals(job, current))
                {
                    card.Classes.Add("state-active");
                    status.Text = "Installing";
                    sub.Text = job.SubTask ?? "";
                    eta.Text = job.Eta ?? "";
                    mainBar.IsVisible = true;
                    subBar.IsVisible = true;
                }
                else
                {
                    card.Classes.Add("state-queued");
                    var pos = GetQueuePosition(job);
                    status.Text = $"#{pos} in queue";
                    sub.Text = "";
                    eta.Text = "";
                    mainBar.IsVisible = false;
                    subBar.IsVisible = false;
                }
            }

            ApplyVisualState();

            job.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InstallJob.IsCompleted) ||
                    e.PropertyName == nameof(InstallJob.SubTask) ||
                    e.PropertyName == nameof(InstallJob.Eta) ||
                    e.PropertyName == nameof(InstallJob.Progress) ||
                    e.PropertyName == nameof(InstallJob.IsIndeterminate))
                    ApplyVisualState();
            };

            Notifications.Current.OnInstallChanged += ApplyVisualState;

            return card;
        }, true);

        ActiveList.ItemTemplate = template;
        CompletedList.ItemTemplate = template;

        UpdateCounts();
        RefreshItems();
    }

    private void WireJob(InstallJob j)
    {
        j.PropertyChanged += OnJobPropertyChanged;
    }

    private void UnwireJob(InstallJob j)
    {
        j.PropertyChanged -= OnJobPropertyChanged;
    }

    private void OnJobPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallJob.IsCompleted))
        {
            UpdateCounts();
            RefreshItems();
        }
    }

    private static void SetActionButtonState(Button btn, InstallJob job)
    {
        btn.Classes.Clear();
        if (job.IsCompleted)
        {
            btn.Content = "Clear";
            btn.Classes.Add("btn-muted");
            btn.IsVisible = true;
        }
    }

    private int GetQueuePosition(InstallJob job)
    {
        var current = Notifications.Current.CurrentInstall;
        var ordered = App.Queue.Jobs.ToList();
        var tail = ordered.Where(j => !j.IsCompleted && !ReferenceEquals(j, current)).ToList();
        var idx = tail.IndexOf(job);
        return idx < 0 ? 0 : idx + 1;
    }

    private void UpdateCounts()
    {
        var current = Notifications.Current.CurrentInstall;
        var activeCount = App.Queue.Jobs.Count(j => !j.IsCompleted);
        var cur = App.Queue.Jobs.Contains(current) && !current.IsCompleted ? 1 : 0;

        CurrentCount.Text = cur.ToString();
        QueueCount.Text = Math.Max(0, activeCount - cur).ToString();
        CompletedCount.Text = App.Queue.Jobs.Count(j => j.IsCompleted).ToString();
    }

    private void RefreshItems()
    {
        var current = Notifications.Current.CurrentInstall;

        var active = App.Queue.Jobs
            .Where(j => !j.IsCompleted)
            .OrderByDescending(j => ReferenceEquals(j, current))
            .ThenBy(j => App.Queue.Jobs.IndexOf(j))
            .ToList();

        var completed = App.Queue.Jobs
            .Where(j => j.IsCompleted)
            .ToList();

        ActiveList.ItemsSource = active;
        CompletedList.ItemsSource = completed;
    }
}

public static class WithExt
{
    public static T With<T>(this T obj, Action<T> a)
    {
        a(obj);
        return obj;
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace DragonDen.ModManager.Views;

public partial class AlphaNoticeDialog : Window
{
    private readonly string githubUrl;
    private readonly string discordUrl;
    private readonly string modPageUrl;

    public AlphaNoticeDialog() : this("", "", "") { }

    public AlphaNoticeDialog(string githubUrl, string discordUrl, string modPageUrl)
    {
        InitializeComponent();

        this.githubUrl = githubUrl;
        this.discordUrl = discordUrl;
        this.modPageUrl = modPageUrl;

        AgreeBtn.Click += OnAgree;
        DisagreeBtn.Click += OnDisagree;
        GithubBtn.Click += OnOpenGithub;
        DiscordBtn.Click += OnOpenDiscord;
        ModPageBtn.Click += OnOpenModPage;

        KeyDown += OnKeyDown;
    }

    private async void OnOpenGithub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var top = GetTopLevel(this);
            if (top != null && !string.IsNullOrWhiteSpace(githubUrl))
                await top.Launcher.LaunchUriAsync(new Uri(githubUrl));
        }
        catch
        {
            // good girl action
        }
    }

    private async void OnOpenDiscord(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var top = GetTopLevel(this);
            if (top != null && !string.IsNullOrWhiteSpace(discordUrl))
                await top.Launcher.LaunchUriAsync(new Uri(discordUrl));
        }
        catch
        {
            // good girl action
        }
    }

    private async void OnOpenModPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var top = GetTopLevel(this);
            if (top != null && !string.IsNullOrWhiteSpace(modPageUrl))
                await top.Launcher.LaunchUriAsync(new Uri(modPageUrl));
        }
        catch
        {
            // good girl action
        }
    }

    private void OnAgree(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnDisagree(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
        }
    }

    public static async Task<bool> ShowAsync(Window owner, string githubIssuesUrl, string discordInviteUrl, string modPageUrl, CancellationToken ct = default)
    {
        var dlg = new AlphaNoticeDialog(githubIssuesUrl, discordInviteUrl, modPageUrl)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        await using var reg = ct.Register(() => {
            try
            {
                dlg.Close(false);
            }
            catch
            {
                // good girl action
            } 
        });
        var t = dlg.ShowDialog<bool>(owner);
        return await t;
    }
}
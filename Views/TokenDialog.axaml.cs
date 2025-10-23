using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DragonDen.ModManager.Views;

public partial class TokenDialog : Window
{
    public enum Result
    {
        None,
        Set,
        CloseApp
    }

    public TokenDialog()
    {
        InitializeComponent();
        SetBtn.Click += OnSetAsync;
        CloseBtn.Click += (_, __) => Close(Result.CloseApp);
    }

    private async void OnSetAsync(object? s, RoutedEventArgs e)
    {
        var token = (TokenBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            App.Toasts.Show("Please paste your Forge token.");
            return;
        }

        SetBtn.IsEnabled = false;
        CloseBtn.IsEnabled = false;
        SetBtn.Content = "Validating…";

        var apiUp = await CheckApiHealthAsync();
        var tokenOk = apiUp && await CheckTokenValidAsync(token);
        SetBtn.Content = "Save Token";
        SetBtn.IsEnabled = true;
        CloseBtn.IsEnabled = true;

        if (!apiUp)
        {
            App.Toasts.Show("Could not reach Forge API. Try again in a moment.");
            return;
        }

        if (!tokenOk)
        {
            App.Toasts.Show("That token didn't work. Ensure it's valid and Read-only.");
            return;
        }

        App.Config.Forge.Token = token;
        App.SaveConfig();
        App.RaiseConfigChanged();
        App.Toasts.Show("Token saved.");
        Close(Result.Set);
    }

    private static async Task<bool> CheckApiHealthAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            using var resp = await http.GetAsync("https://forge.sp-tarkov.com/api/v0/ping");
            if (!resp.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean()
                                                                        && doc.RootElement.TryGetProperty("data", out var d)
                                                                        && d.TryGetProperty("message", out var m)
                                                                        && string.Equals(m.GetString(), "pong", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CheckTokenValidAsync(string token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.GetAsync("https://forge.sp-tarkov.com/api/v0/mods?per_page=1&page=1");
            if (resp.StatusCode == HttpStatusCode.Unauthorized ||
                resp.StatusCode == HttpStatusCode.Forbidden)
                return false;

            if (!resp.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("success", out var s) || !s.GetBoolean())
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnOpenTokenHelp(object? s, RoutedEventArgs e)
    {
        try
        {
            var url = "https://forge.sp-tarkov.com/user/api-tokens";
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            App.Toasts.Show("Could not open browser.");
        }
    }

    private void OnShowToggle(object? s, RoutedEventArgs e)
    {
        var show = ShowToggle.IsChecked == true;
        TokenBox.PasswordChar = show ? '\0' : '•';
        ShowToggle.Content = show ? "Hide" : "Show";
    }
}
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DragonDen.ModManager.Services;

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
            Notifications.Current.ShowWarning("Missing Token", "Please enter your Forge API token before continuing.");
            Console.WriteLine("[TokenDialog] No token entered by user.");
            return;
        }

        SetBtn.IsEnabled = false;
        CloseBtn.IsEnabled = false;
        SetBtn.Content = "Validating...";

        var apiUp = await CheckApiHealthAsync();
        var tokenOk = apiUp && await CheckTokenValidAsync(token);
        SetBtn.Content = "Save Token";
        SetBtn.IsEnabled = true;
        CloseBtn.IsEnabled = true;

        if (!apiUp)
        {
            Notifications.Current.ShowError("Connection Failed", "Could not reach Forge API. Please try again later.");
            Console.WriteLine("[TokenDialog] Forge API unreachable during validation.");
            return;
        }

        if (!tokenOk)
        {
            Notifications.Current.ShowError("Invalid Token", "Token validation failed. Ensure it’s a valid Read-only token.");
            Console.WriteLine("[TokenDialog] Provided Forge token was invalid or rejected.");
            return;
        }

        App.Config.Forge.Token = token;
        App.SaveConfig();
        App.RaiseConfigChanged();
        Notifications.Current.ShowSuccess("Token Saved", "Your Forge API token has been saved successfully.");
        Console.WriteLine("[TokenDialog] Forge token saved and config updated.");
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
        catch (Exception ex)
        {
            Console.WriteLine("[TokenDialog] API health check failed: " + ex);
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
        catch (Exception ex)
        {
            Console.WriteLine("[TokenDialog] Token validation failed: " + ex);
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
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Failed", "Couldn't open token help page in your browser.");
            Console.WriteLine("[TokenDialog] Failed to open token help page: " + ex);
        }
    }

    private void OnShowToggle(object? s, RoutedEventArgs e)
    {
        var show = ShowToggle.IsChecked == true;
        TokenBox.PasswordChar = show ? '\0' : '•';
        ShowToggle.Content = show ? "Hide" : "Show";
    }
}
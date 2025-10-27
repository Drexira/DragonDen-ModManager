using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DragonDen.ModManager.Services;

public sealed class WebImage : Image
{
    public static readonly StyledProperty<string?> UrlProperty = AvaloniaProperty.Register<WebImage, string?>(nameof(Url));

    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private CancellationTokenSource? _loadCts;

    static WebImage()
    {
        UrlProperty.Changed.AddClassHandler<WebImage>((ctl, e) => ctl.OnUrlChanged(e));
    }

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    private void OnUrlChanged(AvaloniaPropertyChangedEventArgs e)
    {
        _ = StartLoadAsync(e.NewValue as string);
    }

    private async Task StartLoadAsync(string? url)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        Source = null;

        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            if (LooksLikeHttp(url))
            {
                var bmp = await LoadHttpAsync(url, ct).ConfigureAwait(false);
                if (bmp != null) await Dispatcher.UIThread.InvokeAsync(() => Source = bmp, DispatcherPriority.Background);
            }
            else
            {
                if (File.Exists(url))
                {
                    await using var fs = File.OpenRead(url);
                    var bmp = await Task.Run(() => Bitmap.DecodeToWidth(fs, 512, BitmapInterpolationMode.MediumQuality), ct);
                    await Dispatcher.UIThread.InvokeAsync(() => Source = bmp, DispatcherPriority.Background);
                }
                else
                {
                    try
                    {
                        var uri = new Uri(url, UriKind.RelativeOrAbsolute);
                        var bmp = new Bitmap(uri.ToString());
                        await Dispatcher.UIThread.InvokeAsync(() => Source = bmp, DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[WebImage] failed local/uri: " + ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine("[WebImage] load error: " + ex);
        }
    }

    private static bool LooksLikeHttp(string s)
    {
        return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string CachePathFor(string url)
    {
        var thumbs = Path.Combine(Paths.CacheDir, "thumbs");
        Directory.CreateDirectory(thumbs);
        string ext;
        try
        {
            ext = Path.GetExtension(new Uri(url).AbsolutePath);
        }
        catch
        {
            ext = ".png";
        }

        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(thumbs, name + ext);
    }

    private static async Task<Bitmap?> LoadHttpAsync(string url, CancellationToken ct)
    {
        var cachePath = CachePathFor(url);

        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            try
            {
                await using var fs = File.OpenRead(cachePath);
                return await Task.Run(() => Bitmap.DecodeToWidth(fs, 512, BitmapInterpolationMode.MediumQuality), ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebImage] failed loading cached image: {ex}");
            }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using (var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fs = File.Open(cachePath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await net.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        await using var local = File.OpenRead(cachePath);
        return await Task.Run(() => Bitmap.DecodeToWidth(local, 512, BitmapInterpolationMode.MediumQuality), ct);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        try
        {
            _loadCts?.Cancel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebImage] OnDetachedFromVisualTree cancel error: {ex}");
        }
    }
}
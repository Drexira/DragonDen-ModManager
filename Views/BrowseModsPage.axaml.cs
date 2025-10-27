using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.ViewModels;
using CacheDb = DragonDen.ModManager.Storage.CacheDb;

namespace DragonDen.ModManager.Views;

public partial class BrowseModsPage : UserControl
{
    private static readonly string[] LoadingTips =
    {
        "Warming up the den...",
        "Syncing with Euphoria's trader backend...",
        "Verifying quest chains (Therapist → Euphoria)...",
        "Packing LooseLoot crates...",
        "Calibrating Zone Maker gizmos...",
        "Compiling VCQL zones...",
        "Scanning map locations for Quest Immersion...",
        "Sharpening mods & tools...",
        "Optimizing raid timers and stash weights...",
        "Taming search gremlins...",
        "Counting ammo boxes (twice)...",
        "Sealing SICC pouches...",
        "Dusting the hideout workbench...",
        "Negotiating with PMCs at extract...",
        "Despawning rogue scavs from staging...",
        "Polishing Dragon Den optics & sights...",
        "Fetching scrolls from Forge...",
        "Hydrating Neon Signs shaders...",
        "Pinging Lighthouse rogues...",
        "Feeding the dragons...",
        "Praying to Nikita for netcode mercy...",
        "Bartering a LedX for two bolts and a dream...",
        "Insuring that ratty wallet you swear you'll keep...",
        "Wiping blood off the Slick, totally factory new...",
        "Teaching scavs the sacred words: “Hold your fire!”",
        "Assembling a budget Chad kit (oxymoron detected)...",
        "Camping the marked room (for science)...",
        "Staring at a Red Rebel like it's affordable...",
        "Injecting SJ6 and regretting life choices...",
        "Rebinding VoIP to 'Apologize to Killa'...",
        "Consulting Therapist: “Can Propital fix desync?”",
        "Turning bush mode ON (invisible +10 charisma)...",
        "Feeding Flea Tax: 35% for 'health'...",
        "Rolling M62s like they're rubles...",
        "Begging Fence for scav karma forgiveness...",
        "Asking Tagilla to chill with the hammer...",
        "Explaining to Sanitar why CMS is not surgery...",
        "Mining Reserve for bitcoin, GPU screams in pain...",
        "Sacrificing a Tetriz to RNGesus...",
        "Extract camping awareness seminar: eyes behind head...",
        "Teaching AI scavs to use indoor voices...",
        "Recalibrating footstep volume to 'panic'...",
        "Printing toilets on Interchange (art installation)...",
        "Negotiating with a bush that just shot me...",
        "Installing extra pockets into your pockets...",
        "Microwaving moonshine for performance gains...",
        "Whispering sweet nothings to the loot pool...",
        "Polishing your dogtag for posthumous glam...",
        "Turning one duct tape into four somehow...",
        "Consulting the Oracle of Jaeger (he grunted)...",
        "Spawning a GPU then losing it to Alt+F4...",
        "Reinforcing your rat license with glitter...",
        "Refactoring spaghetti to linguine code...",
        "Replacing if statements with cope statements...",
        "De-squeaking Killa's Adidas...",
        "Rewiring Shoreline's power to a potato...",
        "Giving Sanitar a Nerf kit for safety...",
        "Teaching Reshala to tip 15% at Dorms...",
        "Hiring Rogues as QA (they shot the bug report)...",
        "Balancing bosses by giving them feelings...",
        "Adding a 'No Bushes' graphics preset...",
        "Applying thermal paste to your legs for speed...",
        "Crossfading gunfire with whale songs...",
        "Deploying decoy PMC that screams 'Friendly!'",
        "Enchanting Slick with 'Attract Bullets +3'...",
        "Repacking rounds alphabetically...",
        "Converting the hideout into an AirBnB...",
        "Taping a flashlight to a flashlight...",
        "Trying to pet a stray grenade...",
        "Haggling with Therapist using dad jokes...",
        "Teaching grenades to ask consent before bouncing...",
        "Installing recoil dampeners on your eyebrows...",
        "Upgrading VoIP to include sighs in Dolby Atmos...",
        "Replacing Interchange lighting with candles...",
        "Bundling painkillers with existential advice...",
        "Adding a 'No Fall Damage' sticker to reality...",
        "Kicking Factory's door until it opens emotionally...",
        "Convincing Gluhar to start a book club...",
        "Wiring customs extract to a mood ring...",
        "Replacing errors with 'skill issue' popups...",
        "Capping frame drops with duct tape...",
        "Porting scavs to turn-based mode...",
        "Infusing loot crates with Schrödinger's GPU...",
        "Upgrading your stash to a black hole...",
        "Teaching bullets basic conflict resolution...",
        "Summoning a friendly cultist (they waved)...",
        "Burying bitcoins for the winter migration...",
        "Jiggle-peeking imposter syndrome...",
        "Rolling back the wipe you dreamed about...",
        "Compressing mods with dragon breath...",
        "Installing RTX on your soul...",
        "Defragging your backpack in Morse code...",
        "Adding a tooltip: 'Don't stand there.'",
        "Sanitizing shoreline water with hope...",
        "Training AI to miss on purpose (you're welcome)...",
        "Converting ricochets to jazz notes...",
        "Aligning scopes with astrology...",
        "Upgrading the flea to farmer's market status...",
        "Teaching pockets to say “I'm full.”",
        "Bundling stash tabs with therapy sessions...",
        "Rebinding 'Alt+F4' to 'Self Care'...",
        "Replacing shoreline fog with vibes...",
        "Awarding +1 charisma for saying 'Howdy' in Labs...",
        "Installing anti-mosquito suppressors on legs...",
        "Introducing sprint cooldown: 'Out of Cope'",
        "Filing insurance claim under 'bear attack'...",
        "Polishing keys so they feel important...",
        "Turning GPU fans into tiny helicopters...",
        "Rewriting AI pathing to 'anywhere but you'...",
        "Adding a ping counter for your emotions...",
        "Cooking lunch on a barrel at Factory...",
        "Teaching flashbangs to use their indoor light...",
        "Putting wheels on the stash (mobile hoarder)...",
        "Adding ambient noise: 'Anxiety Hum v2'",
        "Consulting Jaeger about salad buffs...",
        "Refitting backpacks with clown car tech...",
        "Replacing ricochet sounds with 'boop'",
        "Granting invisibility when you sneeze IRL...",
        "Offloading recoil to your credit score...",
        "Patching shoreline bugs with beach towels...",
        "Rebalancing hatchets with dad strength...",
        "Teaching lasers to draw smiley faces...",
        "Refactoring code that refactors you back...",
        "Deploying loot that screams when picked up...",
        "Syncing extracts to your horoscope...",
        "Reducing desync by asking nicely...",
        "Replacing stamina with spite...",
        "Looting your own dignity (found 0.001 kg)...",
        "Rerolling AI: now with midlife crisis...",
        "Enabling friendly fire for bad vibes...",
        "Installing NVGs with night light mode...",
        "Auto-sorting stash by chaos theory...",
        "Rewriting pathfinding in crayon...",
        "Applying bugfix: 'Bullets are now polite.'",
        "Spawning a GPU inside another GPU (yo dawg)...",
        "Caffeinating scavs, now they jitter-peek...",
        "Attaching suppressors to your feelings...",
        "Rolling a charisma check on Killa's drip...",
        "Filling your mag with compliments...",
        "Giving Rashala a calendar, to stop double booking Dorms...",
        "Tuning footstep audio to 'paranoia major'...",
        "Adding quest: 'Find Peace (0/1)'",
        "Buffing bandages with glitter healing...",
        "Consulting Therapist: diagnosis 'tarkovitis'...",
        "Importing dragons to balance Labs...",
        "Checking if the wipe wiped your memory...",
        "Deploying a cache that caches caches...",
        "Installing ray tracing on shoreline fog (more fog)...",
        "Rebinding 'Push To Talk' to 'Beg For Mercy'...",
        "Enabling loot to loot you back...",
        "Giving PMCs tiny top hats for accuracy +2...",
        "Embedding patch notes into a matryoshka doll...",
        "Teaching backpacks to say 'one more slot, bro'...",
        "Assigning your stash a union rep...",
        "Patching pain sounds with motivational quotes...",
        "Adding achievement: 'Died With Dignity' (secret)...",
        "Rolling back your last bad decision (fail)..."
    };

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private readonly object _indexGate = new();
    private readonly Random _rng = new();

    private readonly DispatcherTimer _tipsTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private bool _ignoreNextReset;
    private CancellationTokenSource? _indexCts;
    private int _indexRunId;
    private bool _isIndexing;
    private bool _isSearching;

    private DateTime _modalStart;

    private int _page = 1, _lastPage = 1;

    private string? _pendingSptMajor;
    private int _searchRunId;
    private bool _softSearch;
    private int _totalMatches;
    private bool _updatingSptFilter;
    private bool _pagingInitialized;

    public BrowseModsPage()
    {
        InitializeComponent();

        RefreshBtn.Click += async (_, __) => await StartIndexingAsync(true);
        ClearBtn.Click += OnClear;

        CategoryBox.SelectionChanged += async (_, __) =>
        {
            SaveUiPrefs();
            await PerformSearch(true);
        };
        SortBox.SelectionChanged += async (_, __) =>
        {
            SaveUiPrefs();
            await PerformSearch(true);
        };
        PageSizeBox.SelectionChanged += async (_, __) =>
        {
            SaveUiPrefs();
            await PerformSearch(true);
        };

        FeaturedChk.IsCheckedChanged += OnHideTogglesChanged;
        AdsChk.IsCheckedChanged += OnHideTogglesChanged;
        AiChk.IsCheckedChanged += OnHideTogglesChanged;
        HideInstalledChk.IsCheckedChanged += OnHideTogglesChanged;
        App.InstallsChanged += OnInstallsChanged;

        PrevBtn.Click += async (_, __) =>
        {
            if (_lastPage > 1 && _page > 1)
            {
                _debounce.Stop();
                _ignoreNextReset = true;
                _page--;
                await PerformSearch(false);
            }
        };
        NextBtn.Click += async (_, __) =>
        {
            if (_page < _lastPage)
            {
                _debounce.Stop();
                _ignoreNextReset = true;
                _page++;
                await PerformSearch(false);
            }
        };
        
        _debounce.Tick += OnDebounceTick;

        SearchBox.PropertyChanged += async (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                _debounce.Stop();
                _softSearch = true;
                _debounce.Start();
            }
        };
        SearchBox.KeyDown += OnSearchKeyDown;

        _page = 1;
        _lastPage = 0;
        UpdatePagingUi();

        App.SearchByAuthorRequested = authorName =>
        {
            try
            {
                SearchBox.Text = "@" + (authorName ?? "").Trim();
                _ = PerformSearch(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to search by author: " + ex);
            }
        };

        AddHandler(PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel);

        SelectSort(App.Config.UI.SearchSort);
        SelectPageSize(App.Config.UI.SearchPageSize);

        AttachedToVisualTree += (_, __) =>
        {
            if (_pagingInitialized) return;
            _pagingInitialized = true;
            _page = Math.Max(1, _page);
            UpdatePagingUi();
        };

        _ = LoadCategoriesThenSearch();
        App.ConfigChanged += OnAppConfigChanged;
    }
    
    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        await PerformSearch(true, softOverlay: true);
        _softSearch = false;
    }

    private async void OnAppConfigChanged()
    {
        var detected = DetectSptMajorFromConfig();
        if (string.IsNullOrWhiteSpace(detected)) return;

        await LoadAllSptFiltersAsync();

        Dispatcher.UIThread.Post(() =>
        {
            _pendingSptMajor = detected;
            TryApplyPendingSptFilter();
        });
    }

    private async void OnHideTogglesChanged(object? sender, RoutedEventArgs e)
    {
        if (_isIndexing) return;

        await PerformSearch(true);
    }

    private static bool TryFindSptExeForRoot(string root, out string exePath)
    {
        var p1 = Path.Combine(root, "SPT.Server.exe");
        var p2 = Path.Combine(root, "SPT", "SPT.Server.exe");
        if (File.Exists(p1))
        {
            exePath = p1;
            return true;
        }

        if (File.Exists(p2))
        {
            exePath = p2;
            return true;
        }

        exePath = "";
        return false;
    }

    private static string DetectSptMajorFromConfig()
    {
        try
        {
            var root = App.Config.Paths.SptRoot ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "";

            if (!TryFindSptExeForRoot(root, out var exe)) return "";

            var info = FileVersionInfo.GetVersionInfo(exe);
            var fv = info?.FileVersion ?? "";
            var parts = fv.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            return $"{parts[0]}.{parts[1]}";
        }
        catch
        {
            Console.WriteLine("[BrowseModsPage] Failed to detect SPT major version from config.");
            return "";
        }
    }

    public Task TriggerRefresh(bool showModal = true)
    {
        return StartIndexingAsync(showModal);
    }

    public void SelectSptMajor(string majorTag)
    {
        _pendingSptMajor = majorTag;
        TryApplyPendingSptFilter();
    }

    public async void SearchByAuthor(string authorName)
    {
        if (string.IsNullOrWhiteSpace(authorName)) return;
        SearchBox.Text = "@" + authorName.Trim();
        await PerformSearch(true);
        Focus();
    }

    private void TryApplyPendingSptFilter()
    {
        if (string.IsNullOrWhiteSpace(_pendingSptMajor)) return;
        if (SptFilterBox.Items is null) return;

        var match = SptFilterBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), _pendingSptMajor, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _updatingSptFilter = true;
            SptFilterBox.SelectedItem = match;
            _updatingSptFilter = false;
            _ = PerformSearch(true);
            _pendingSptMajor = null;
        }
    }

    private void ShowModal(string title, string line1 = "", string line2 = "", double pct = -1)
    {
        ModalTitle.Text = title;
        ModalLine1.Text = line1;
        ModalLine2.Text = line2;
        ModalEta.Text = "";
        ModalBar.IsIndeterminate = pct < 0;
        if (pct >= 0) ModalBar.Value = pct;

        _modalStart = DateTime.UtcNow;

        StartTips();

        DimOverlay.IsVisible = true;
        LoadingModal.IsVisible = true;
        MainContent.IsEnabled = false;
        BusyBar.IsVisible = true;
    }

    private void UpdateModal(string title, string line1, double pct = -1, int current = 0, int total = 0)
    {
        ModalTitle.Text = title;
        ModalLine1.Text = line1;

        if (pct >= 0 && double.IsFinite(pct))
        {
            ModalBar.IsIndeterminate = false;
            ModalBar.Value = Math.Clamp(pct, 0, 100);
        }
        else
        {
            ModalBar.IsIndeterminate = true;
        }

        if (current > 0 && total > 0) ModalLine2.Text = $"Page {current} / {total}";
        else ModalLine2.Text = "";

        var elapsed = DateTime.UtcNow - _modalStart;
        if (double.IsFinite(pct) && pct >= 0.5 && pct <= 99.9 && elapsed.TotalSeconds >= 0)
        {
            var denom = Math.Max(0.001, pct);
            var fractionRemaining = (100.0 - Math.Clamp(pct, 0.5, 99.9)) / denom;
            var estSeconds = Math.Clamp(elapsed.TotalSeconds * fractionRemaining, 1, 86400 * 7);
            ModalEta.Text = $"~{(int)Math.Ceiling(estSeconds)}s remaining";
        }
        else
        {
            ModalEta.Text = "";
        }
    }

    private void UpdatePagingUi()
    {
        PageInfo.Text = _lastPage > 0 ? $"{_page} / {_lastPage}" : "— / —";
        PrevBtn.IsEnabled = !_isIndexing && !_isSearching && _page > 1;
        NextBtn.IsEnabled = !_isIndexing && !_isSearching && _page < Math.Max(1, _lastPage);
    }

    private void ShowSearchOverlay(bool soft = false)
    {
        _isSearching = true;
        BusyBar.IsVisible = true;

        if (soft) return;

        try
        {
            SearchOverlayTip.Text = LoadingTips[_rng.Next(LoadingTips.Length)];
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BrowseModsPage] Failed to pick random loading tip: " + ex);
        }

        SearchOverlay.IsVisible = true;
        DragShield.IsVisible = true;
        SearchBox.IsEnabled = false;
        ClearBtn.IsEnabled = false;
        RefreshBtn.IsEnabled = false;
        CategoryBox.IsEnabled = false;
        SptFilterBox.IsEnabled = false;
        SortBox.IsEnabled = false;
        PageSizeBox.IsEnabled = false;
        FeaturedChk.IsEnabled = false;
        AdsChk.IsEnabled = false;
        AiChk.IsEnabled = false;
        HideInstalledChk.IsEnabled = false;

        UpdatePagingUi();
        Cursor = new Cursor(StandardCursorType.Wait);
    }

    private void HideSearchOverlay(bool soft = false)
    {
        _isSearching = false;
        BusyBar.IsVisible = false;

        if (soft) return;

        SearchOverlay.IsVisible = false;
        DragShield.IsVisible = false;
        SearchBox.IsEnabled = true;
        ClearBtn.IsEnabled = true;
        RefreshBtn.IsEnabled = true;
        CategoryBox.IsEnabled = true;
        SptFilterBox.IsEnabled = true;
        SortBox.IsEnabled = true;
        PageSizeBox.IsEnabled = true;
        FeaturedChk.IsEnabled = true;
        AdsChk.IsEnabled = true;
        AiChk.IsEnabled = true;
        HideInstalledChk.IsEnabled = true;

        UpdatePagingUi();
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void HideModal()
    {
        StopTips();

        LoadingModal.IsVisible = false;
        DimOverlay.IsVisible = false;
        MainContent.IsEnabled = true;
        BusyBar.IsVisible = false;
    }

    private void StartTips()
    {
        _tipsTimer.Tick -= OnTipTick;
        _tipsTimer.Tick += OnTipTick;
        SetRandomTip();
        _tipsTimer.Start();
    }

    private void StopTips()
    {
        _tipsTimer.Stop();
        _tipsTimer.Tick -= OnTipTick;
    }

    private void OnTipTick(object? s, EventArgs e)
    {
        SetRandomTip();
    }

    private void SetRandomTip()
    {
        if (LoadingTips.Length == 0) return;
        var tip = LoadingTips[_rng.Next(LoadingTips.Length)];
        ModalTip.Text = tip;
    }

    private async void OnCancelIndexing(object? s, RoutedEventArgs e)
    {
        lock (_indexGate)
        {
            _indexCts?.Cancel();
        }

        await Task.Delay(50);
        StopTips();
        HideModal();
        SearchStatusText.Text = "Indexing cancelled.";
    }

    private async Task LoadCategoriesThenSearch()
    {
        try
        {
            var cachedItems = await FetchCategoriesFromCache();
            if (cachedItems.Count > 0) SetCategories(cachedItems);
            else _ = RefreshCategoriesInBackgroundAsync();

            await LoadAllSptFiltersAsync();
            _pendingSptMajor ??= DetectSptMajorFromConfig();

            SptFilterBox.SelectionChanged += async (_, __) =>
            {
                if (!_updatingSptFilter) await PerformSearch(true);
            };

            TryApplyPendingSptFilter();
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Categories Failed", "Couldn't load categories.");
            Console.WriteLine("[BrowseModsPage] Categories failed to load: " + ex);
        }

        await StartIndexingAsync(true);
    }

    private async Task<List<UiCategory>> FetchCategoriesFromCache()
    {
        var cats = await CacheDb.GetCategoriesAsync();

        var items = new List<UiCategory> { new() { Title = "All categories", Slug = "", ColorClass = "" } };
        items.AddRange(cats.Select(c =>
        {
            var title = string.IsNullOrWhiteSpace(c.title)
                ? string.IsNullOrWhiteSpace(c.slug) ? "(Uncategorized)" : c.slug
                : c.title;
            return new UiCategory { Title = title, Slug = c.slug, ColorClass = "" };
        }).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase));
        return items;
    }

    private async Task<List<UiCategory>?> FetchCategoriesFromApi(CancellationToken ct)
    {
        try
        {
            var cats = await ForgeClient.GetCategoriesAsync(ct);
            if (cats is null || cats.Count == 0) return null;

            var items = new List<UiCategory> { new() { Title = "All categories", Slug = "", ColorClass = "" } };
            items.AddRange(cats
                .Select(c => new UiCategory
                {
                    Title = string.IsNullOrWhiteSpace(c.title) ? string.IsNullOrWhiteSpace(c.slug) ? "(Uncategorized)" : c.slug : c.title,
                    Slug = c.slug ?? "",
                    ColorClass = c.color_class ?? ""
                })
                .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase));

            return items;
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshCategoriesInBackgroundAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var fresh = await FetchCategoriesFromApi(cts.Token);
            if (fresh is null || fresh.Count == 0) return;

            var current = CategoryBox.ItemsSource as IEnumerable<UiCategory>;
            var same = current != null &&
                       current.Count() == fresh.Count &&
                       current.Zip(fresh).All(x => string.Equals(x.First.Title, x.Second.Title, StringComparison.Ordinal)
                                                   && string.Equals(x.First.Slug, x.Second.Slug, StringComparison.OrdinalIgnoreCase));
            if (!same) SetCategories(fresh);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BrowseModsPage] Failed to refresh categories in background: " + ex);
        }
    }

    private void SetCategories(List<UiCategory> items)
    {
        var prevSlug = (CategoryBox.SelectedItem as UiCategory)?.Slug ?? "";
        CategoryBox.ItemsSource = items;
        var match = items.FirstOrDefault(i => string.Equals(i.Slug, prevSlug, StringComparison.OrdinalIgnoreCase))
                    ?? items.FirstOrDefault()
                    ?? new UiCategory { Title = "All categories", Slug = "" };
        CategoryBox.SelectedItem = match;
    }

    private HttpClient BuildApiClient(int timeoutSeconds = 12)
    {
        var h = new HttpClient();
        h.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 30));
        h.DefaultRequestHeaders.Accept.Clear();
        h.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(App.Config.Forge.Token))
            h.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + App.Config.Forge.Token);
        return h;
    }

    private async Task LoadAllSptFiltersAsync()
    {
        var (majors, fulls) = await CacheDb.GetAllSptTagsAsync();

        var semverDesc = Comparer<string>.Create((a, b) => SemverUtil.CompareTagsDesc(a, b));

        var byMajor = fulls
            .GroupBy(s =>
            {
                var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : "";
            }, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x, semverDesc).ToList(), StringComparer.OrdinalIgnoreCase);

        var items = new List<ComboBoxItem>
        {
            new() { Content = "All SPT", Tag = "" }
        };

        foreach (var maj in majors.OrderBy(x => x, semverDesc))
        {
            if (maj.StartsWith("0") || maj.Contains("3.12")) continue;
            items.Add(new ComboBoxItem { Content = $"SPT {maj}", Tag = maj });

            if (byMajor.TryGetValue(maj, out var patchList))
                foreach (var f in patchList)
                    items.Add(new ComboBoxItem { Content = $"SPT {f}", Tag = f });
        }

        var knownMajors = new HashSet<string>(majors, StringComparer.OrdinalIgnoreCase);
        items.AddRange(from kv in byMajor where !knownMajors.Contains(kv.Key) from f in kv.Value select new ComboBoxItem { Content = $"SPT {f}", Tag = f });

        _updatingSptFilter = true;

        try
        {
            var keepTag = (SptFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            SptFilterBox.ItemsSource = items;

            ComboBoxItem? keep = null;

            if (!string.IsNullOrWhiteSpace(keepTag))
            {
                keep = items.FirstOrDefault(i =>
                    string.Equals((string?)i.Tag ?? "", keepTag, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var detected = DetectSptMajorFromConfig();
                if (!string.IsNullOrWhiteSpace(detected))
                    keep = items.FirstOrDefault(i =>
                               string.Equals(i.Tag?.ToString(), detected, StringComparison.OrdinalIgnoreCase))
                           ?? items.FirstOrDefault(i =>
                               (i.Tag?.ToString() ?? "").StartsWith(detected + ".", StringComparison.OrdinalIgnoreCase));
            }

            SptFilterBox.SelectedItem = keep ?? items[0];
        }
        finally
        {
            _updatingSptFilter = false;
        }
    }

    private void SelectSort(string sort)
    {
        foreach (var item in SortBox.Items)
            if (item is ComboBoxItem c && (string?)c.Tag == sort)
            {
                SortBox.SelectedItem = c;
                return;
            }

        SortBox.SelectedIndex = 0;
    }

    private void SelectPageSize(int size)
    {
        foreach (var item in PageSizeBox.Items)
            if (item is ComboBoxItem c && int.TryParse(c.Content?.ToString(), out var v) && v == size)
            {
                PageSizeBox.SelectedItem = c;
                return;
            }

        PageSizeBox.SelectedIndex = 1;
    }

    private void SaveUiPrefs()
    {
        var sortTag = (SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "recent";
        var ps = int.TryParse((PageSizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var v) ? v : 12;
        App.Config.UI.SearchSort = sortTag;
        App.Config.UI.SearchPageSize = ps;
        App.SaveConfig();
    }

    private static string ApiSortFromUi(string ui)
    {
        return ui switch
        {
            "recent" => "recent",
            "newest" => "newest",
            "downloads" => "downloads",
            _ => "recent"
        };
    }

    private void OnClear(object? s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
        _ = PerformSearch(true);
        BlurSearch();
    }

    private void OnSearchKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _debounce.Stop();
            _ = PerformSearch(true);
            BlurSearch();
            e.Handled = true;
        }
    }

    private void OnRootPointerPressed(object? s, PointerPressedEventArgs e)
    {
        if (SearchBox.IsFocused)
        {
            var pos = e.GetPosition(SearchBox);
            var inside = new Rect(SearchBox.Bounds.Size).Contains(pos);
            if (!inside) BlurSearch();
        }
    }

    private void BlurSearch()
    {
        Focus();
    }

    private async Task StartIndexingAsync(bool showModal)
    {
        App.CancelWarmCache();
        CancellationToken ct;
        int runId;
        lock (_indexGate)
        {
            _indexCts?.Cancel();
            _indexCts = new CancellationTokenSource();
            runId = ++_indexRunId;
            ct = _indexCts!.Token;
        }

        RefreshBtn.IsEnabled = false;

        _isIndexing = true;
        _page = 1;
        _lastPage = 0;
        UpdatePagingUi();
        SearchStatusText.Text = "Refreshing cache...";
        if (showModal) ShowModal("Refreshing", "Syncing with Forge...");

        void OnForgeStatus(string msg)
        {
            try
            {
                Dispatcher.UIThread.Post(() => { UpdateModal("Refreshing", msg); });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to update Forge status UI: " + ex);
            }
        }

        ForgeClient.StatusMessage += OnForgeStatus;

        try
        {
            var progress = new Progress<(string phase, int current, int total)>(p =>
            {
                var pct = p.total <= 0 ? -1 : p.current * 100.0 / Math.Max(1, p.total);
                UpdateModal("Refreshing", $"{p.phase}...", pct, p.current, p.total);
            });

            await CacheDb.RefreshModsIncrementalAsync(progress, ct);
            if (runId != _indexRunId || ct.IsCancellationRequested) return;

            HideModal();
            _isIndexing = false;
            UpdatePagingUi();
            SearchStatusText.Text = "Up to date.";

            await LoadAllSptFiltersAsync();
            await PerformSearch(true);
        }
        catch (OperationCanceledException)
        {
            HideModal();
            _isIndexing = false;
            UpdatePagingUi();
            SearchStatusText.Text = "Indexing cancelled.";
        }
        catch (Exception ex)
        {
            HideModal();
            _isIndexing = false;
            UpdatePagingUi();
            Notifications.Current.ShowError("Refresh Failed", "Couldn't refresh mod cache or Forge data.");
            Console.WriteLine("[BrowseModsPage] Refresh failed: " + ex);
        }
        finally
        {
            ForgeClient.StatusMessage -= OnForgeStatus;
            RefreshBtn.IsEnabled = true;

            UpdatePagingUi();
        }
    }
    
    private static async Task FlushUiAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private async Task PerformSearch(bool resetPage, bool softOverlay = false)
    {
        if (resetPage && !_ignoreNextReset)
            _page = 1;

        _ignoreNextReset = false;
        var myRunId = ++_searchRunId;

        ShowSearchOverlay(softOverlay);

        await FlushUiAsync();

        var raw = (SearchBox.Text ?? "").Trim();
        var isAuthorQuery = raw.StartsWith("@");
        var author = isAuthorQuery ? raw.TrimStart('@').Trim() : "";
        var query = isAuthorQuery ? "" : raw;

        var catSlug = (CategoryBox.SelectedItem as UiCategory)?.Slug ?? "";
        var sptTag = (SptFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var sortUi = (SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "recent";
        var sortKey = ApiSortFromUi(sortUi);

        var pageSize = int.TryParse((PageSizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var v) ? v : 12;

        var hideFeatured = FeaturedChk?.IsChecked ?? false;
        var hideAds = AdsChk?.IsChecked ?? false;
        var hideAi = AiChk?.IsChecked ?? false;
        var hideInstalled = HideInstalledChk?.IsChecked ?? false;

        _lastPage = Math.Max(0, _lastPage);
        UpdatePagingUi();

        async Task<List<SearchResultRow>> BuildRowsAsync(IEnumerable<ForgeClient.ModSummary> mods)
        {
            var rows = new List<SearchResultRow>();

            foreach (var m in mods)
            {
                if (m.guid == "net.drexira.modmanager") continue;

                var row = new SearchResultRow
                {
                    ModId = m.id,
                    Guid = m.guid ?? "",
                    Name = m.name ?? "",
                    Teaser = m.teaser ?? "",
                    OwnerNames = BuildOwnerNames(m).ToList(),
                    Thumbnail = string.IsNullOrWhiteSpace(m.thumbnail)
                        ? $"https://placehold.co/165x165/171717/EEE.png?text={Uri.EscapeDataString(m.name ?? "")}&font=source-sans-pro"
                        : ForgeClient.ResolveImageUrl(m.thumbnail),
                    Slug = m.slug ?? "",
                    ModPageUrl = m.detail_url ?? "",
                    Downloads = m.downloads
                };

                var catText = m.category?.name;
                if (string.IsNullOrWhiteSpace(catText)) catText = m.category?.title;
                if (string.IsNullOrWhiteSpace(catText)) catText = m.category?.slug;
                row.Category = string.IsNullOrWhiteSpace(catText) ? "(Uncategorized)" : catText;

                var versions = await CacheDb.GetVersionsAsync(m.id);
                row.Versions = versions;

                row.IsInstalled = App.Db.HasRealInstall(row.Name);

                var displays = new List<SearchResultRow.VersionDisplay>();
                foreach (var ver in versions)
                {
                    var spt = SemverUtil.NormalizeToThreeParts(ver.SptVersionConstraint);
                    displays.Add(new SearchResultRow.VersionDisplay
                    {
                        Model = ver,
                        Label = string.IsNullOrWhiteSpace(spt)
                            ? $"v{ver.Version ?? "n/a"}"
                            : $"v{ver.Version ?? "n/a"} • SPT {spt}",
                        SptNormalized = spt
                    });
                }

                ApplySptPriority(displays, sptTag);
                row.VersionsDisplay = displays;

                var latest = versions.FirstOrDefault();
                row.LatestVersionText = latest?.Version is string lv && !string.IsNullOrWhiteSpace(lv) ? $"Latest v{lv}" : "Latest v—";
                row.SptConstraintText = SemverUtil.NormalizeToThreeParts(latest?.SptVersionConstraint) is string txt && !string.IsNullOrWhiteSpace(txt) ? txt : "-";

                var latestModSPTVersion = SemverUtil.NormalizeToThreeParts(latest?.SptVersionConstraint);
                var latestSPTVersion = App.Cache.GetLatestSPTVersion();
                if (string.Equals(latestModSPTVersion, latestSPTVersion, StringComparison.OrdinalIgnoreCase))
                    row.IsLatestVersion = true;

                if (m.source_code_links is { Length: > 0 })
                    foreach (var s in m.source_code_links)
                        if (s is { url: { } u } && !string.IsNullOrWhiteSpace(u))
                            row.SourceButtons.Add(new SearchResultRow.SourceButton
                            {
                                Url = u,
                                Label = string.IsNullOrWhiteSpace(s.label) ? "Source" : s.label!.Trim()
                            });

                rows.Add(row);
            }

            return rows;
        }

        try
        {
            var targetStart = (_page - 1) * pageSize;
            var needCount = targetStart + pageSize;

            var accVisible = new List<SearchResultRow>();
            var hiddenOnSeenPages = 0;

            var rawPage = 1;
            var rawLastPage = 1;
            _totalMatches = 0;

            while (accVisible.Count < needCount && rawPage <= rawLastPage)
            {
                var res = await CacheDb.QueryModsAsync(
                    new CacheDb.Query
                    {
                        Text = query,
                        Author = author,
                        CategorySlug = catSlug,
                        SptConstraint = sptTag,
                        Sort = sortKey,
                        Page = rawPage,
                        PageSize = pageSize
                    },
                    hideFeatured,
                    hideAds,
                    hideAi);

                if (rawPage == 1)
                {
                    _totalMatches = res.total;
                    rawLastPage = Math.Max(1, (int)Math.Ceiling(_totalMatches / (double)pageSize));
                    _lastPage = rawLastPage;
                    UpdatePagingUi();
                }

                var built = await BuildRowsAsync(res.items);

                var considered = built;
                if (hideInstalled)
                {
                    var before = built.Count;
                    considered = built.Where(r => !r.IsInstalled).ToList();
                    hiddenOnSeenPages += before - considered.Count;
                }

                accVisible.AddRange(considered);
                rawPage++;
            }

            var visibleForPage = accVisible.Skip(targetStart).Take(pageSize).ToList();

            ResultsList.ItemsSource = visibleForPage;
            PageInfo.Text = $"{_page} / {_lastPage}";
            LoadedModsLabel.Text = "Loaded Mods: " + _totalMatches.ToString("N0", CultureInfo.InvariantCulture);

            if (visibleForPage.Count == 0)
                SearchStatusText.Text = hideInstalled
                    ? (hiddenOnSeenPages > 0 ? "No visible results (installed hidden)." : "No results")
                    : "No results";
            else
                SearchStatusText.Text = hideInstalled
                    ? $"Showing {visibleForPage.Count} of {_totalMatches:N0} (installed hidden)"
                    : $"Showing {visibleForPage.Count} of {_totalMatches:N0}";

            ScrollResultsToTop();
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Search Failed", "Couldn't complete search query.");
            Console.WriteLine("[BrowseModsPage] Search failed: " + ex);
            UpdatePagingUi();
        }
        finally
        {
            if (myRunId == _searchRunId)
                HideSearchOverlay(softOverlay);

            UpdatePagingUi();
        }
    }

    private void OnUninstallSelected(object? sender, RoutedEventArgs e)
    {
        var row =
            (sender as Control)?.DataContext as SearchResultRow
            ?? ResultsList.SelectedItem as SearchResultRow;

        if (row is null || string.IsNullOrWhiteSpace(row.Name)) return;

        App.Db.Uninstall(row.Name);
        App.NotifyInstallsChanged();
        row.IsQueued = false;
        row.IsInstalled = false;

        var list = ResultsList.ItemsSource as IList<SearchResultRow>;
        var total = list?.Count ?? 0;
        SearchStatusText.Text = total == 0 ? "No results" : $"Showing {total:N0}";

        Notifications.Current.ShowSuccess("Mod Uninstalled", $"{row.Name} has been removed successfully.");
        Console.WriteLine("[BrowseModsPage] Uninstalled mod: " + row.Name);
    }

    private async Task<List<ForgeClient.MissingDep>> ResolveMissingDependenciesAsync(int modId, int versionId)
    {
        var result = new List<ForgeClient.MissingDep>();
        if (modId <= 0 || versionId <= 0) return result;

        try
        {
            var deps = await ForgeClient.GetDependenciesForVersionAsync(modId, versionId, App.ShutdownToken);

            foreach (var d in deps)
            {
                var childModId = d.mod_id;
                var name = (d.mod_name ?? "").Trim();
                var guid = (d.mod_guid ?? "").Trim();

                if (childModId <= 0 && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(guid))
                {
                    var cached = await CacheDb.TryGetModByIdAsync(d.id);
                    if (cached is { } c)
                    {
                        childModId = c.id;
                        name = c.name ?? name;
                        guid = c.guid ?? guid;
                    }
                }

                if (childModId > 0 && (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(guid)))
                {
                    var cached = await CacheDb.TryGetModByIdAsync(childModId);
                    if (cached is { } c)
                    {
                        if (string.IsNullOrWhiteSpace(name)) name = c.name ?? name;
                        if (string.IsNullOrWhiteSpace(guid)) guid = c.guid ?? guid;
                    }
                }

                var installKey = !string.IsNullOrWhiteSpace(name) ? name : guid;
                var alreadyInstalled = !string.IsNullOrWhiteSpace(installKey) && App.Db.HasRealInstall(installKey);

                if (alreadyInstalled) continue;

                if (childModId > 0 || !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(guid))
                    result.Add(new ForgeClient.MissingDep
                    {
                        ModId = childModId,
                        Name = name,
                        Guid = guid,
                        VersionConstraint = d.version_constraint,
                        IsOptional = d.is_optional
                    });
            }
        }
        catch (HttpRequestException ex)
        {
            Notifications.Current.ShowError("Dependency Fetch Failed",
                ex.Message.Contains("401") || ex.Message.Contains("403")
                    ? "Forge rejected dependency request (invalid or expired token)."
                    : "Failed to fetch mod dependencies from Forge.");
            Console.WriteLine("[BrowseModsPage] Dependency fetch failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Dependency Fetch Failed", "Couldn't retrieve dependencies for mod.");
            Console.WriteLine("[BrowseModsPage] Dependency fetch failed: " + ex.Message);
        }

        return result;
    }

    private async Task<List<(int ModId, string? Name, string? Guid)>> QueueDependenciesThenModAsync(
        string modName,
        ForgeClient.ModVersion selectedVersion,
        List<ForgeClient.MissingDep> missing)
    {
        var enqueued = new List<(int ModId, string? Name, string? Guid)>();
        var required = missing.Where(d => !d.IsOptional).ToList();

        foreach (var dep in required)
        {
            var depKey = string.IsNullOrWhiteSpace(dep.Name) ? dep.Guid ?? "" : dep.Name;

            if (!string.IsNullOrWhiteSpace(depKey) && App.Db.HasRealInstall(depKey)) continue;

            try
            {
                var lookupKey = !string.IsNullOrWhiteSpace(dep.Name) ? dep.Name : dep.Guid ?? "";
                var best = await CacheDb.GetLatestVersionForModNameAsync(lookupKey);
                if (best is null || string.IsNullOrWhiteSpace(best.Link)) continue;

                App.Queue.EnqueueRemote(string.IsNullOrWhiteSpace(dep.Name) ? dep.Guid ?? "Dependency" : dep.Name,
                    best.Link!, best.Version ?? "Custom Install", dep.Guid ?? "");

                enqueued.Add((dep.ModId, dep.Name, dep.Guid));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to enqueue dependency: " + ex);
            }
        }

        App.Queue.EnqueueRemote(modName, selectedVersion.Link!, selectedVersion.Version ?? "Custom Install", "");
        enqueued.Add((0, modName, null));

        return enqueued;
    }

    private void OnAuthorsWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            var dx = (e.Delta.Y != 0 ? -e.Delta.Y : -e.Delta.X) * 40;
            var extentX = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
            var newX = Math.Clamp(sv.Offset.X + dx, 0, extentX);
            sv.Offset = new Vector(newX, sv.Offset.Y);
            e.Handled = true;
        }
    }

    private static void ApplySptPriority(List<SearchResultRow.VersionDisplay> list, string selectedTag)
    {
        if (list is null || list.Count == 0 || string.IsNullOrWhiteSpace(selectedTag)) return;

        static string MajorAB(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var p = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return p.Length >= 2 ? $"{p[0]}.{p[1]}" : "";
        }

        int Score(SearchResultRow.VersionDisplay vd)
        {
            var spt = vd.SptNormalized ?? "";
            if (string.Equals(spt, selectedTag, StringComparison.OrdinalIgnoreCase)) return 2;
            var selMaj = MajorAB(selectedTag);
            var sptMaj = MajorAB(spt);
            if (!string.IsNullOrWhiteSpace(selMaj) &&
                string.Equals(selMaj, sptMaj, StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        static int CompareSemverDesc(string? a, string? b)
        {
            var sa = a ?? "";
            var sb = b ?? "";
            var okA = SemverUtil.TryParseStrict(sa, out var sva);
            var okB = SemverUtil.TryParseStrict(sb, out var svb);
            if (okA && okB) return svb.CompareSortOrderTo(sva);
            return string.Compare(sb, sa, StringComparison.OrdinalIgnoreCase);
        }

        list.Sort((x, y) =>
        {
            var sx = Score(x);
            var sy = Score(y);
            if (sx != sy) return sy.CompareTo(sx);
            return CompareSemverDesc(x?.Model?.Version, y?.Model?.Version);
        });
    }

    private async Task EnsureSourcesFromApiAsync(List<SearchResultRow> rows)
    {
        if (string.IsNullOrWhiteSpace(App.Config.Forge.Token)) return;
        var needs = rows.Where(r => !(r.SourceButtons?.Count > 0)).Take(4).ToList();
        if (needs.Count == 0) return;

        foreach (var r in needs)
        {
            try
            {
                var mod = await ForgeClient.GetModAsync(r.ModId,
                    false,
                    false,
                    false,
                    false,
                    true,
                    App.ShutdownToken);

                var links = mod?.source_code_links;
                if (links is { Length: > 0 })
                    foreach (var s in links)
                    {
                        if (string.IsNullOrWhiteSpace(s.url)) continue;
                        r.SourceButtons.Add(new SearchResultRow.SourceButton
                        {
                            Url = s.url,
                            Label = string.IsNullOrWhiteSpace(s.label) ? "Source" : s.label!.Trim()
                        });
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to fetch or attach source links: " + ex);
            }

            await Task.Delay(250);
        }
    }

    private void ScrollResultsToTop()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (ResultsScroll != null)
                {
                    ResultsScroll.ScrollToHome();
                    ResultsScroll.Offset = new Vector(0, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to scroll results to top: " + ex);
            }
        }, DispatcherPriority.Background);
    }

    private void OnOpenSource(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string url || string.IsNullOrWhiteSpace(url)) return;

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Failed", "Couldn't open mod source link.");
            Console.WriteLine("[BrowseModsPage] Couldn't open source link: " + ex.Message);
        }
    }

    private void OnOpenModPage(object? sender, RoutedEventArgs e)
    {
        var url = "";
        if (sender is Button b && b.Tag is SearchResultRow row) url = row.ModPageUrl ?? "";
        else if (sender is MenuItem mi && mi.Tag is SearchResultRow row2) url = row2.ModPageUrl ?? "";

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Open Failed", "Couldn't open mod page in browser.");
                Console.WriteLine("[BrowseModsPage] Couldn't open mod page: " + ex.Message);
            }
        }
        else
        {
            Notifications.Current.ShowWarning("Missing Link", "No Forge page URL found for this mod.");
            Console.WriteLine("[BrowseModsPage] Missing mod page URL.");
        }
    }

    private async void OnCopyLink(object? sender, RoutedEventArgs e)
    {
        var url = (sender as Control)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            Notifications.Current.ShowWarning("Missing Link", "No link to copy.");
            Console.WriteLine("[BrowseModsPage] No link to copy.");
            return;
        }

        try
        {
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard is not null)
            {
                await tl.Clipboard.SetTextAsync(url);
                Notifications.Current.ShowSuccess("Copied", "Mod link copied to clipboard.");
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Copy Failed", "Couldn't copy mod link to clipboard.");
            Console.WriteLine("[BrowseModsPage] Failed to copy mod link: " + ex.Message);
        }
    }

    private async void OnCopyName(object? sender, RoutedEventArgs e)
    {
        var name = (sender as Control)?.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard != null)
            {
                await tl.Clipboard.SetTextAsync(name);
                Notifications.Current.ShowSuccess("Copied", "Mod name copied to clipboard.");
                Console.WriteLine($"[BrowseModsPage] Copied mod name: {name}");
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Copy Failed", "Couldn't copy mod name to clipboard.");
            Console.WriteLine("[BrowseModsPage] Failed to copy mod name: " + ex.Message);
        }
    }

    private async void OnCopyGuid(object? sender, RoutedEventArgs e)
    {
        var guid = (sender as Control)?.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(guid)) return;

        try
        {
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard != null)
            {
                await tl.Clipboard.SetTextAsync(guid);
                Notifications.Current.ShowSuccess("Copied", "Mod GUID copied to clipboard.");
                Console.WriteLine($"[BrowseModsPage] Copied mod GUID: {guid}");
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Copy Failed", "Couldn't copy GUID to clipboard.");
            Console.WriteLine("[BrowseModsPage] Failed to copy GUID: " + ex.Message);
        }
    }

    private void OnOpenThumb(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var url = mi.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Failed", "Couldn't open thumbnail image.");
            Console.WriteLine("[BrowseModsPage] Failed to open image: " + ex.Message);
        }
    }

    private void OnResultsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Just to prevent selection ui ugliness
    }

    private async void OnOwnerPillClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string name && !string.IsNullOrWhiteSpace(name))
        {
            SearchBox.Text = "@" + name;
            await PerformSearch(true);
        }
    }

    private async void OnInstallSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not SearchResultRow.VersionDisplay vd)
        {
            Notifications.Current.ShowWarning("Missing Version", "Select a version before installing.");
            Console.WriteLine($"[BrowseModsPage] No version selected for install.");
            return;
        }

        var model = vd.Model;
        if (string.IsNullOrWhiteSpace(model.Link))
        {
            Notifications.Current.ShowWarning("Missing Link", "This version has no download link available.");
            Console.WriteLine("[BrowseModsPage] No download link for version.");
            return;
        }

        var detectedAB = App.GetDetectedSptAB();
        var verSpt = vd.SptNormalized ?? "";

        static string MajorAB(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var p = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return p.Length >= 2 ? $"{p[0]}.{p[1]}" : "";
        }

        if (!string.IsNullOrWhiteSpace(detectedAB))
        {
            var selTag = (SptFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            var needFullMatch = !string.IsNullOrWhiteSpace(selTag) && selTag.Count(c => c == '.') >= 2;
            var ok =
                (!needFullMatch && string.Equals(MajorAB(verSpt), detectedAB, StringComparison.OrdinalIgnoreCase)) ||
                (needFullMatch && string.Equals(verSpt, selTag, StringComparison.OrdinalIgnoreCase));

            if (!ok)
            {
                var exp = string.IsNullOrWhiteSpace(selTag) ? detectedAB : selTag;
                Notifications.Current.ShowWarning("SPT Version Mismatch",
                    $"Version targets SPT {(string.IsNullOrWhiteSpace(verSpt) ? "—" : verSpt)}, expected {exp}.");
                Console.WriteLine($"[BrowseModsPage] SPT version mismatch (targets {verSpt}, expected {exp}).");
                return;
            }
        }

        var rowCtx = (btn.Parent as Panel)?.DataContext as SearchResultRow ??
                     ResultsList.SelectedItem as SearchResultRow ??
                     new SearchResultRow { Name = "Mod", Guid = "" };

        var row = btn.DataContext as SearchResultRow ?? ResultsList.SelectedItem as SearchResultRow;
        if (row is null) return;

        Notifications.Current.ShowSuccess("Queued", $"{rowCtx.Name} added to download queue.");
        Console.WriteLine("[BrowseModsPage] Queued download: " + rowCtx.Name);
        MarkRowsQueued(new[] { (rowCtx.ModId, rowCtx.Name, rowCtx.Guid) });

        List<ForgeClient.MissingDep> rawMissing = new();
        if (!row.IsInstalled)
            try
            {
                rawMissing = await ResolveMissingDependenciesAsync(rowCtx.ModId, model.Id);
            }
            catch
            {
                Console.WriteLine("[BrowseModsPage] Failed to resolve missing dependencies.");
                rawMissing = new List<ForgeClient.MissingDep>();
            }

        var missing = rawMissing.Where(d => !IsInstalledByNameOrGuid(d.Name, d.Guid)).ToList();
        var owner = TopLevel.GetTopLevel(this) as Window ??
                    this.FindAncestorOfType<Window>() ??
                    (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (missing is { Count: > 0 } && !row.IsInstalled && owner is not null)
        {
            var choice = await DependenciesDialog.ShowAsync(owner!, rowCtx.Name, missing);
            if (choice == DependenciesDialog.InstallChoice.Cancel) return;

            if (choice == DependenciesDialog.InstallChoice.InstallWithDeps)
            {
                var enq = await QueueDependenciesThenModAsync(rowCtx.Name, model, missing);
                MarkRowsQueued(enq);
            }
            else
            {
                App.Queue.EnqueueRemote(rowCtx.Name, model.Link!, model.Version ?? "Custom Install", rowCtx.Guid ?? "");
                MarkRowsQueued(new[] { (rowCtx.ModId, rowCtx.Name, rowCtx.Guid) });
            }
        }
        else
        {
            App.Queue.EnqueueRemote(rowCtx.Name, model.Link!, model.Version ?? "Custom Install", rowCtx.Guid ?? "");
            MarkRowsQueued(new[] { (rowCtx.ModId, rowCtx.Name, rowCtx.Guid) });
        }
    }

    private void OnInstallsChanged()
    {
        if (ResultsList?.ItemsSource is not IEnumerable<SearchResultRow> rows) return;
        var list = rows.ToList();
        foreach (var r in list)
        {
            var installed = IsInstalledByNameOrGuid(r.Name, r.Guid);
            if (installed)
            {
                r.IsInstalled = true;
                r.IsQueued = false;
            }
            else
            {
                r.IsInstalled = false;
            }
        }
    }

    private static bool IsInstalledByNameOrGuid(string? name, string? guid)
    {
        var g = (guid ?? "").Trim();
        var n = (name ?? "").Trim();

        try
        {
            if (!string.IsNullOrWhiteSpace(g) && App.Db.HasRealInstall(g)) return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BrowseModsPage] Install check by GUID failed: " + ex);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(n) && App.Db.HasRealInstall(n)) return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BrowseModsPage] Install check by Name failed: " + ex);
        }

        return false;
    }

    private void MarkRowsQueued(IEnumerable<(int ModId, string? Name, string? Guid)> items)
    {
        if (ResultsList?.ItemsSource is not IEnumerable<SearchResultRow> rows) return;

        var list = rows.ToList();
        foreach (var it in items)
        {
            var row = list.FirstOrDefault(r =>
                (it.ModId > 0 && r.ModId == it.ModId) ||
                (!string.IsNullOrWhiteSpace(it.Guid) && !string.IsNullOrWhiteSpace(r.Guid) &&
                 string.Equals(it.Guid, r.Guid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(it.Name) && !string.IsNullOrWhiteSpace(r.Name) &&
                 string.Equals(it.Name, r.Name, StringComparison.OrdinalIgnoreCase)));

            if (row != null)
            {
                row.IsInstalled = false;
                row.IsQueued = true;
            }
        }
    }

    private void MarkRowsInstalled(IEnumerable<(int ModId, string? Name, string? Guid)> items)
    {
        if (ResultsList?.ItemsSource is not IEnumerable<SearchResultRow> rows) return;

        var list = rows.ToList();
        foreach (var it in items)
        {
            var row = list.FirstOrDefault(r =>
                (it.ModId > 0 && r.ModId == it.ModId) ||
                (!string.IsNullOrWhiteSpace(it.Guid) && !string.IsNullOrWhiteSpace(r.Guid) &&
                 string.Equals(it.Guid, r.Guid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(it.Name) && !string.IsNullOrWhiteSpace(r.Name) &&
                 string.Equals(it.Name, r.Name, StringComparison.OrdinalIgnoreCase)));

            if (row != null)
            {
                row.IsQueued = false;
                row.IsInstalled = true;
            }
        }
    }

    private static async Task<ForgeClient.ModVersion?> GetBestVersionForDepAsync(ForgeClient.MissingDep dep)
    {
        if (!string.IsNullOrWhiteSpace(dep.Name))
        {
            var v = await CacheDb.GetLatestVersionForModNameAsync(dep.Name);
            if (v is not null) return v;
        }

        if (!string.IsNullOrWhiteSpace(dep.Guid))
            try
            {
                var v = await App.Cache.GetLatestVersionForModGuidAsync(dep.Guid);
                if (v is not null) return v;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to get latest version for dependency (GUID): " + ex);
            }

        if (dep.ModId > 0)
            try
            {
                await App.Cache.EnsureVersionsCachedAsync(dep.ModId);
                var list = App.Cache.GetVersionsForMod(dep.ModId);
                var best = list
                    .OrderByDescending(x => x.PublishedAt ?? DateTimeOffset.MinValue)
                    .ThenByDescending(x => x.Downloads)
                    .FirstOrDefault();
                if (best is not null) return best;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BrowseModsPage] Failed to get latest version for dependency (ModId): " + ex);
            }

        return null;
    }

    private static IEnumerable<string> BuildOwnerNames(ForgeClient.ModSummary m)
    {
        var owners = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.owner?.name)) owners.Add(m.owner!.name);
        if (m.authors is { Length: > 0 })
            foreach (var a in m.authors)
                if (!string.IsNullOrWhiteSpace(a?.name))
                    owners.Add(a!.name);
        return owners.Distinct();
    }

    private sealed class ApiListResponse<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public List<T>? Data { get; set; }
    }

    private sealed class ApiSingleResponse<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public T? Data { get; set; }
    }

    private sealed class CategoryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("slug")] public string? Slug { get; set; }
    }

    private sealed class SourceLinkDto
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
    }

    private sealed class ModDetailsDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }

        [JsonPropertyName("source_code_links")]
        public List<SourceLinkDto>? SourceCodeLinks { get; set; }
    }
}
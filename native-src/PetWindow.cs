using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CodexPetPlugin;

public sealed class PetWindow : Window
{
    // All coordinates and proportions below come from the original widget.
    private const double Base = 375, WhaleFraction = .5945;
    private const string BubblePath = "M 827 248 A 373 232 0 1 0 81 246 A 373 232 0 0 0 301 465 A 57 32 10 0 0 413 484 A 373 232 0 0 0 827 248 Z";
    private readonly bool preview;
    private readonly bool idlePreview;
    private readonly PetSettings settings;
    private readonly CodexClient client = new();
    private readonly QuoteCatalog quotes = new();
    private readonly SessionActivityWatcher activity = new();
    private readonly DispatcherTimer activityTimer = new();
    private readonly QuotaDropTracker dropTracker = new();
    private readonly Queue<QuotaDrop> notices = new();
    private CancellationTokenSource? alertRun;
    private bool weeklyDisplay;
    private bool initialized;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer taskbarTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private double? bottomGap, taskbarTarget;
    private int taskbarClearSamples;
    private CancellationTokenSource? closeDelay;
    private readonly DispatcherTimer countdown = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly ScaleTransform breathe = new(1, 1), squeeze = new(1, 1), flip = new(1, 1), textFlip = new(1, 1);
    private readonly RotateTransform ahoge = new(0, 407, 71);
    private readonly Image skinBody = new() { Width = 610, Height = 610 };
    private readonly Image skinHair = new() { Width = 610, Height = 610 };
    private readonly Dictionary<string, BitmapImage> skinImages = new();
    private ComboBox skinPicker = null!;
    private readonly Canvas root = new() { Width = Base, Height = Base };
    private readonly Canvas body = new() { Width = Base, Height = Base, RenderTransformOrigin = new Point(.5, 1) };
    private readonly Canvas bubble = new() { Width = Base, Height = Base * 700 / 1026, Visibility = Visibility.Collapsed };
    private readonly StackPanel words = new() { RenderTransformOrigin = new Point(.5, .5) };
    private readonly TextBlock label = Text("", 66), amount = Text("", 128, true), hint = Text("", 56);
    private readonly List<FrameworkElement> bubbleShapes = new();
    private readonly Button menuButton;
    private readonly Popup menu = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Top };
    private readonly MediaPlayer pressAudio = new(), releaseAudio = new();
    private QuotaSnapshot? data;
    private string? error;
    private bool busy, dragging, randomActive, bubbleOpen;
    private Point? mouseDown;
    private DateTimeOffset lastRequest;
    private readonly SolidColorBrush Ink = ThemeBrush("#536ba9"), Outline = ThemeBrush("#203170"), Hint = ThemeBrush("#9fb0d9");
    private readonly SolidColorBrush BubbleFill = ThemeBrush("#FFFFFF"), MenuFill = ThemeBrush("#F2FFFFFF"), MenuStroke = ThemeBrush("#59203170"), ButtonFill = ThemeBrush("#D9203170");

    public PetWindow(bool preview, bool idlePreview = false)
    {
        this.preview = preview; this.idlePreview = idlePreview;
        settings = preview ? new() { LayoutVersion = 4, Skin = "whale" } : PetSettings.Load();
        bottomGap = settings.BottomGap;
        Title = "额度伙伴 · 插件浮窗"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        ShowActivated = false; Topmost = settings.Topmost;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        Width = Height = 250 * settings.Scale;
        root.RenderTransform = flip; root.RenderTransformOrigin = new Point(.5, .5);
        body.RenderTransform = squeeze; root.Children.Add(body);
        Content = new Viewbox { Child = root, Stretch = Stretch.Uniform };

        double whaleSize = Base * WhaleFraction;
        var pet = new Grid { Width = whaleSize, Height = whaleSize, RenderTransformOrigin = new Point(.5, 1), RenderTransform = breathe, Cursor = Cursors.Hand };
        var source = new Canvas { Width = 610, Height = 610 };
        source.Children.Add(skinBody);
        var hairLayer = new Canvas { Width = 610, Height = 610, RenderTransform = ahoge, IsHitTestVisible = false };
        hairLayer.Children.Add(skinHair); source.Children.Add(hairLayer);
        ApplySkin(settings.Skin);
        pet.Children.Add(new Viewbox { Child = source });
        Canvas.SetLeft(pet, Base - whaleSize); Canvas.SetTop(pet, Base - whaleSize); body.Children.Add(pet);
        pet.MouseLeftButtonDown += OnDown; pet.MouseMove += OnMove; pet.MouseLeftButtonUp += OnUp;
        pet.LostMouseCapture += (_, _) => ReleasePress();
        pet.MouseEnter += (_, _) => menuButton!.Opacity = 1;
        pet.MouseLeave += (_, _) => { if (!menu.IsOpen && !IsMouseOver) menuButton!.Opacity = 0; };

        var bubbleSvg = new Canvas { Width = 1026, Height = 700 };
        var shape = new System.Windows.Shapes.Path { Data = Geometry.Parse(BubblePath), Fill = BubbleFill, Stroke = Outline, StrokeThickness = 18, StrokeLineJoin = PenLineJoin.Round };
        bubbleSvg.Children.Add(shape); bubbleShapes.Add(shape);
        foreach (var (x, y, rx, ry) in new[] { (352d, 561d, 37.5, 26d), (442d, 646d, 24.5, 18d) })
        {
            // SVG ellipse strokes are centered on their contours.
            var ellipse = new Ellipse { Width = 2 * rx + 18, Height = 2 * ry + 18, Fill = BubbleFill, Stroke = Outline, StrokeThickness = 18 };
            Canvas.SetLeft(ellipse, x - rx - 9); Canvas.SetTop(ellipse, y - ry - 9);
            bubbleSvg.Children.Add(ellipse); bubbleShapes.Add(ellipse);
        }
        bubble.Children.Add(new Viewbox { Width = Base, Height = Base * 700 / 1026, Child = bubbleSvg });
        label.FontWeight = FontWeights.SemiBold;
        hint.Foreground = Hint; hint.Margin = new Thickness(0, Base * 9 / 1026, 0, 0);
        words.RenderTransform = textFlip;
        words.Children.Add(label); words.Children.Add(amount); words.Children.Add(hint); bubble.Children.Add(words);
        body.Children.Add(bubble);
        bubble.MouseLeftButtonUp += (_, e) => { e.Handled = true; BubbleClick(); };

        menuButton = new Button { Width = 26, Height = 26, Content = "☰", FontSize = 17, Background = ButtonFill, Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = Cursors.Hand, Opacity = 0 };
        var menuBorder = new FrameworkElementFactory(typeof(Border));
        menuBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        menuBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var menuContent = new FrameworkElementFactory(typeof(ContentPresenter));
        menuContent.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        menuContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        menuBorder.AppendChild(menuContent); menuButton.Template = new ControlTemplate(typeof(Button)) { VisualTree = menuBorder };
        // Menu size stays 26 px even when the widget's scale changes.
        Canvas.SetLeft(menuButton, Base - 30); Canvas.SetTop(menuButton, Base * .4055 + 4);
        root.Children.Add(menuButton); menuButton.Click += (_, _) => menu.IsOpen = !menu.IsOpen;
        menuButton.MouseEnter += (_, _) => menuButton.Opacity = 1;
        MouseLeave += (_, _) => { if (!menu.IsOpen && !dragging) menuButton.Opacity = 0; };
        menu.PlacementTarget = menuButton; menu.Child = BuildMenu();
        menu.Opened += (_, _) => menuButton.Opacity = 1;
        menu.Closed += (_, _) => { if (!IsMouseOver) menuButton.Opacity = 0; };
        MouseRightButtonUp += (_, e) => { menu.IsOpen = true; e.Handled = true; };

        Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            PlaceWindow(); UpdateMenuSize(); ApplyMirror(false); SetAnimation();
            if (preview)
            {
                var now = DateTimeOffset.Now;
                data = new(new(76, now.AddHours(3).AddMinutes(12)), new(74, now.AddDays(5)), 2, null, now);
                if (!idlePreview) ShowQuota(false);
            }
            else { if (bottomGap is null) RememberBottomAnchor(); ApplyTaskbar(); taskbarTimer.Start(); activity.Enabled = true; poll.Start(); countdown.Start(); await RefreshAsync(true); }
        };
        taskbarTimer.Tick += (_, _) => ApplyTaskbar();
        client.QuotaChanged += snapshot => Dispatcher.InvokeAsync(() =>
        {
            if (lifetime.IsCancellationRequested || !IsVisible) return;
            activityTimer.Stop(); lastRequest = DateTimeOffset.Now;
            AcceptSnapshot(snapshot);
        });
        activity.Activity += () => Dispatcher.InvokeAsync(() =>
        {
            if (preview || !IsVisible || lifetime.IsCancellationRequested || activityTimer.IsEnabled) return;
            activityTimer.Interval = TimeSpan.FromSeconds(Math.Max(.75, 3 - (DateTimeOffset.Now - lastRequest).TotalSeconds));
            activityTimer.Start();
        });
        activityTimer.Tick += async (_, _) => { activityTimer.Stop(); if (IsVisible) await RefreshAsync(); };
        poll.Tick += async (_, _) => { if (IsVisible) await RefreshAsync(); };
        countdown.Tick += (_, _) => { if (bubble.Visibility == Visibility.Visible && !randomActive) Render(); };
        Closed += (_, _) =>
        {
            poll.Stop(); taskbarTimer.Stop(); closeDelay?.Cancel(); countdown.Stop(); activityTimer.Stop(); activity.Dispose(); StopAlerts(); menu.IsOpen = false;
            lifetime.Cancel(); client.Dispose(); pressAudio.Close(); releaseAudio.Close();
            if (!preview) { settings.Left = Left; settings.Top = bottomGap is { } gap ? TaskbarAvoidance.Read(this).BaselineBottom - Height - gap : Top; settings.BottomGap = bottomGap; settings.Save(); }
        };
    }

    public void ShowPet()
    {
        if (!IsVisible) Show();
        activity.Enabled = !preview; poll.Start(); taskbarTimer.Start();
        Opacity = 1; _ = RefreshAsync(true);
    }
    public void HidePet()
    {
        activity.Enabled = false; activityTimer.Stop(); poll.Stop(); taskbarTimer.Stop();
        StopAlerts(); HideBubble(true); dropTracker.Reset(); client.Stop(); Hide();
    }
    public void RefreshNow() { if (IsVisible) _ = RefreshAsync(true); }
    internal void PreparePreview(string stage)
    {
        if (!preview) return;
        ShowQuota(false);
        if (stage == "reset") { settings.QuoteChance = 0; BubbleClick(false); }
        if (stage == "quote") { settings.QuoteChance = 1; BubbleClick(false); }
    }

    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static SolidColorBrush ThemeBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock Text(string text, double sourceSize, bool bold = false) => new()
    {
        Text = text, FontSize = Base * sourceSize / 1026, Foreground = Brush("#536ba9"),
        FontWeight = bold ? FontWeights.ExtraBold : FontWeights.Normal,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        LineHeight = Base * sourceSize / 1026 * (bold ? 1.05 : 1.15),
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap
    };
    private void ApplySkin(string id)
    {
        var skin = PetSkins.Find(id);
        if (!File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", skin.BodyFile)) ||
            !File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", skin.HairFile))) skin = PetSkins.All[0];
        BitmapImage Load(string file)
        {
            if (skinImages.TryGetValue(file, out var cached)) return cached;
            var bitmap = new BitmapImage(); bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 610;
            bitmap.UriSource = new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", file));
            bitmap.EndInit(); bitmap.Freeze(); skinImages[file] = bitmap; return bitmap;
        }
        skinBody.Source = Load(skin.BodyFile); skinHair.Source = Load(skin.HairFile);
        skinBody.Clip = skin.ExcludeHairFromBody
            ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(skin.BodyClip ?? new Rect(0, 0, 610, 610)), new RectangleGeometry(skin.HairClip))
            : skin.BodyClip is { } clip ? new RectangleGeometry(clip) : null;
        skinHair.Clip = new RectangleGeometry(skin.HairClip);
        ahoge.CenterX = skin.HairPivot.X; ahoge.CenterY = skin.HairPivot.Y;
        Ink.Color = (Color)ColorConverter.ConvertFromString(skin.Theme.Ink);
        Outline.Color = (Color)ColorConverter.ConvertFromString(skin.Theme.Outline);
        Hint.Color = (Color)ColorConverter.ConvertFromString(skin.Theme.Hint);
        BubbleFill.Color = (Color)ColorConverter.ConvertFromString(skin.Theme.Fill);
        label.Foreground = amount.Foreground = Ink; hint.Foreground = Hint;
        MenuFill.Color = Color.FromArgb(242, BubbleFill.Color.R, BubbleFill.Color.G, BubbleFill.Color.B);
        MenuStroke.Color = Color.FromArgb(89, Outline.Color.R, Outline.Color.G, Outline.Color.B);
        ButtonFill.Color = Color.FromArgb(217, Outline.Color.R, Outline.Color.G, Outline.Color.B);
        settings.Skin = skin.Id;
    }
    internal void PrepareSkin(string id) => ApplySkin(id);
    private void SetLines(string a, string b, string c)
    {
        label.Text = a; amount.Text = b; hint.Text = c;
        label.Visibility = a.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        amount.Visibility = b.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        hint.Visibility = c.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        words.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(words, Base * .4425 - words.DesiredSize.Width / 2);
        Canvas.SetTop(words, bubble.Height * .38 - words.DesiredSize.Height / 2);
    }
    private void Render()
    {
        if (randomActive) return;
        string detail = error is not null ? "离线 · 点击重试" : data is null ? busy ? "查询中…" : "点击重试" : weeklyDisplay ? $"5小时剩余 {QuotaText.Percent(data.FiveHour.Remaining)}" : $"每周剩余 {QuotaText.Percent(data.Weekly.Remaining)}";
        SetLines(weeklyDisplay ? "每周剩余额度" : "5小时剩余额度", QuotaText.Percent(weeklyDisplay ? data?.Weekly.Remaining : data?.FiveHour.Remaining), detail);
    }
    private async Task RefreshAsync(bool manual = false)
    {
        if (busy || preview || lifetime.IsCancellationRequested || (!manual && DateTimeOffset.Now - lastRequest < TimeSpan.FromSeconds(3))) return;
        busy = true; lastRequest = DateTimeOffset.Now;
        try { AcceptSnapshot(await client.ReadAsync(lifetime.Token)); }
        catch (OperationCanceledException) { return; }
        catch { error = "查询失败"; }
        finally { busy = false; if (!lifetime.IsCancellationRequested) Render(); }
    }
    private void AcceptSnapshot(QuotaSnapshot snapshot)
    {
        var old = data; data = snapshot; error = null;
        if (!IsVisible) { dropTracker.Reset(); return; }
        var changes = dropTracker.Observe(snapshot);
        // Discard queued reminders from a reset/replenished window.
        if (old is not null && notices.Count != 0)
        {
            var valid = notices.Where(n => n.Kind == QuotaKind.FiveHour
                ? old.FiveHour.ResetsAt == snapshot.FiveHour.ResetsAt && !(snapshot.FiveHour.Remaining > old.FiveHour.Remaining)
                : old.Weekly.ResetsAt == snapshot.Weekly.ResetsAt && !(snapshot.Weekly.Remaining > old.Weekly.Remaining)).ToArray();
            notices.Clear(); foreach (var n in valid) notices.Enqueue(n);
        }
        Render();
        if (!settings.BubbleOn) return;
        foreach (var change in changes) notices.Enqueue(change);
        if (alertRun is null && notices.Count != 0) _ = ShowAlertsAsync();
    }
    private void StopAlerts()
    {
        var running = alertRun; alertRun = null; running?.Cancel(); notices.Clear();
    }
    private async Task ShowAlertsAsync()
    {
        var running = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); alertRun = running;
        try
        {
            while (notices.Count != 0 && IsVisible && settings.BubbleOn)
            {
                running.Token.ThrowIfCancellationRequested();
                if (dragging || mouseDown.HasValue || menu.IsOpen) { await Task.Delay(200, running.Token); continue; }
                var notice = notices.Dequeue();
                ShowQuota(true, true, true, notice.Kind == QuotaKind.Weekly);
                // One distinct animation per point, with the original five-second dwell.
                await Task.Delay(5500, running.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (alertRun == running) alertRun = null; running.Dispose(); }
    }
    private void ShowQuota(bool animate = true, bool replay = false, bool automatic = false, bool weekly = false)
    {
        if (!settings.BubbleOn) return;
        if (!automatic) StopAlerts();
        weeklyDisplay = weekly;
        amount.TextWrapping = TextWrapping.NoWrap; amount.MaxWidth = double.PositiveInfinity;
        amount.FontWeight = FontWeights.ExtraBold; amount.LineHeight = Base * 128 / 1026 * 1.05;
        randomActive = false; amount.FontSize = Base * 128 / 1026; Render(); OpenBubble(animate, replay);
    }
    private void OpenBubble(bool animate = true, bool replay = false)
    {
        bool wasOpen = bubbleOpen; bubbleOpen = true;
        bubble.Visibility = Visibility.Visible;
        bubble.BeginAnimation(OpacityProperty, null); bubble.Opacity = 1;
        if (animate && (!wasOpen || replay) && settings.Animate && SystemParameters.ClientAreaAnimation)
        {
            // Original stagger: lower dot, upper dot, then main thought cloud.
            for (int i = 0; i < bubbleShapes.Count; i++)
            {
                var element = bubbleShapes[i];
                double delay = i == 0 ? .26 : i == 1 ? .13 : 0;
                element.Opacity = 0;
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.2)) { BeginTime = TimeSpan.FromSeconds(delay) });
                Rect bounds = element is System.Windows.Shapes.Path path ? path.Data.Bounds : new Rect(0, 0, element.Width, element.Height);
                var grow = new ScaleTransform(.7, .7, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                element.RenderTransform = grow;
                foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                    grow.BeginAnimation(axis, new DoubleAnimation(.7, 1, TimeSpan.FromSeconds(.2)) { BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            words.Opacity = 0;
            words.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.16)) { BeginTime = TimeSpan.FromSeconds(.36) });
        }
        else
        {
            foreach (var element in bubbleShapes) { element.BeginAnimation(OpacityProperty, null); element.Opacity = 1; element.RenderTransform = Transform.Identity; }
            words.BeginAnimation(OpacityProperty, null); words.Opacity = 1;
        }
        CancelClose(); if (!preview) ScheduleClose();
    }
    private void CancelClose()
    {
        var pending = closeDelay; closeDelay = null; pending?.Cancel();
    }
    private async void ScheduleClose()
    {
        CancelClose();
        var pending = new CancellationTokenSource(); closeDelay = pending;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), pending.Token);
            if (closeDelay == pending) HideBubble(false);
        }
        catch (OperationCanceledException) { }
        finally { pending.Dispose(); }
    }
    private void HideBubble(bool immediate)
    {
        CancelClose(); randomActive = false; bubbleOpen = false;
        if (immediate || !settings.Animate || preview) { bubble.Visibility = Visibility.Collapsed; return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(.2));
        fade.Completed += (_, _) => { if (closeDelay is null) bubble.Visibility = Visibility.Collapsed; };
        bubble.BeginAnimation(OpacityProperty, fade);
    }
    private void BubbleClick(bool animate = true)
    {
        StopAlerts();
        if (!bubbleOpen) { ShowQuota(animate); return; }
        if (randomActive) { HideBubble(false); return; }
        randomActive = true;
        string? quote = quotes.Pick(settings.QuoteChance, Random.Shared.NextDouble());
        if (quote is not null)
        {
            amount.FontSize = Base * (quote.Length <= 9 ? 128 : 66) / 1026;
            amount.FontWeight = quote.Length <= 9 ? FontWeights.ExtraBold : FontWeights.SemiBold;
            amount.TextWrapping = TextWrapping.Wrap; amount.MaxWidth = Base * 560 / 1026;
            amount.LineHeight = amount.FontSize * 1.15;
            SetLines("", quote, "");
        }
        else
        {
            amount.FontSize = Base * 128 / 1026; amount.LineHeight = amount.FontSize * 1.05;
            amount.FontWeight = FontWeights.ExtraBold; amount.MaxWidth = double.PositiveInfinity; amount.TextWrapping = TextWrapping.NoWrap;
            SetLines("剩余重置次数", data?.ResetCount is { } count ? $"{count} 次" : "--", "");
        }
        OpenBubble(animate);
    }
    private void RecenterWords()
    {
        words.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(words, Base * .4425 - words.DesiredSize.Width / 2);
        Canvas.SetTop(words, bubble.Height * .38 - words.DesiredSize.Height / 2);
    }
    private static string ResetDate(QuotaWindow? window) => window?.ResetsAt is { } t ? t.ToLocalTime().ToString("MM/dd HH:mm") : "--";
    private static string ShortReset(QuotaWindow? window)
    {
        if (window?.ResetsAt is not { } t) return "--";
        var left = t - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero) return "等待更新";
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}小时{left.Minutes}分" : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}分钟";
    }
    private void PlaceWindow()
    {
        Rect work = SystemParameters.WorkArea;
        Left = settings.Left ?? work.Right - Width; Top = settings.Top ?? work.Bottom - Height;
        KeepVisible();
    }
    private void KeepVisible()
    {
        Rect work = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
    }
    private void RememberBottomAnchor()
    {
        if (preview) return;
        var bar = TaskbarAvoidance.Read(this);
        bottomGap = TaskbarAvoidance.ShouldAnchor(Top + Height, bar.AvailableBottom, bar.BaselineBottom)
            ? Math.Max(0, bar.AvailableBottom - Top - Height) : null;
        settings.BottomGap = bottomGap; taskbarTarget = null;
    }
    private void ApplyTaskbar()
    {
        if (preview || !IsVisible || dragging || mouseDown.HasValue || bottomGap is not { } gap) return;
        MoveForTaskbar(TaskbarAvoidance.Read(this), gap);
    }
    private void MoveForTaskbar(TaskbarBounds bar, double gap)
    {
        double target = Math.Max(bar.ScreenTop, bar.AvailableBottom - Height - gap);
        taskbarClearSamples = TaskbarAvoidance.CanFall(bar.AvailableBottom, bar.BaselineBottom) ? Math.Min(2, taskbarClearSamples + 1) : 0;
        double from = Top;
        // Hold above a retracting bar until it is completely hidden for two samples.
        if (target > from + .5 && taskbarClearSamples < 2) return;
        if (taskbarTarget is { } old && Math.Abs(old - target) < .5) return;
        BeginAnimation(TopProperty, null); Top = target; taskbarTarget = target;
        if (!settings.Animate || !SystemParameters.ClientAreaAnimation) return;
        if (target > from + .5)
        {
            // Constant acceleration from rest, with one short animation per retraction.
            var fall = new DoubleAnimation(from, target, TimeSpan.FromSeconds(Math.Clamp(Math.Sqrt(2 * (target - from) / 1800), .12, .45)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            Timeline.SetDesiredFrameRate(fall, 30);
            BeginAnimation(TopProperty, fall);
        }
        else if (target < from - .5)
        {
            // Upward displacement is immediate; only the body briefly compresses.
            squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.9, 1, TimeSpan.FromMilliseconds(110)));
        }
    }
    private void UpdateMenuSize()
    {
        double unit = Base / Width;
        menuButton.Width = menuButton.Height = 26 * unit;
        menuButton.FontSize = 17 * unit;
        Canvas.SetLeft(menuButton, Base - 30 * unit);
        Canvas.SetTop(menuButton, Base * .4055 + 4 * unit);
    }
    private void Snap()
    {
        Rect work = SystemParameters.WorkArea;
        double centerX = Left + Width / 2, centerY = Top + Height / 2;
        if (centerX < work.Left + work.Width / 4) { Left = work.Left; settings.Mirror = true; }
        else if (centerX > work.Left + work.Width * 3 / 4) { Left = work.Right - Width; settings.Mirror = false; }
        if (centerY < work.Top + work.Height / 4) Top = work.Top;
        else if (centerY > work.Top + work.Height * 3 / 4) Top = work.Bottom - Height;
        ApplyMirror(true);
    }
    private void ApplyMirror(bool animate)
    {
        double value = settings.Mirror ? -1 : 1;
        flip.BeginAnimation(ScaleTransform.ScaleXProperty, animate ? new DoubleAnimation(value, TimeSpan.FromSeconds(.3)) : null);
        flip.ScaleX = value; textFlip.ScaleX = value;
    }
    private void SetAnimation()
    {
        bool active = settings.Animate && SystemParameters.ClientAreaAnimation && !preview;
        DoubleAnimation? Breath(double amount) => active && settings.BreatheAmplitude > 0 ? new DoubleAnimation(1, 1 + amount * settings.BreatheAmplitude, TimeSpan.FromSeconds(settings.BreatheSeconds / 2))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } } : null;
        breathe.BeginAnimation(ScaleTransform.ScaleXProperty, Breath(.02)); breathe.BeginAnimation(ScaleTransform.ScaleYProperty, Breath(.045));
        if (!active || settings.AhogeAmplitude == 0) { ahoge.BeginAnimation(RotateTransform.AngleProperty, null); return; }
        var sway = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(settings.AhogeSeconds), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (fraction, angle) in new[] { (0d, 0d), (.18, -7d), (.38, 2.5), (.58, 5.5), (.8, -1.5), (1d, 0d) })
            sway.KeyFrames.Add(new SplineDoubleKeyFrame(angle * settings.AhogeAmplitude, KeyTime.FromPercent(fraction), new KeySpline(.42, 0, .58, 1)));
        ahoge.BeginAnimation(RotateTransform.AngleProperty, sway);
    }
    private void Play(bool down)
    {
        if (!settings.Sound || settings.Volume == 0 || preview) return;
        string file = settings.SoundSet == "fx1" ? down ? "D1.mp3" : "D2.mp3" : down ? "Ya1.mp3" : "Ya2.mp3";
        try { var player = down ? pressAudio : releaseAudio; player.Open(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", file))); player.Volume = settings.Volume; player.Play(); } catch { }
    }
    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        double currentTop = Top; BeginAnimation(TopProperty, null); Top = currentTop;
        taskbarTarget = null;
        mouseDown = e.GetPosition(this); dragging = false; ((UIElement)sender).CaptureMouse();
        squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.05, TimeSpan.FromSeconds(.22)));
        squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.88, TimeSpan.FromSeconds(.22)));
        Play(true); e.Handled = true;
    }
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (mouseDown is not { } origin || e.LeftButton != MouseButtonState.Pressed || dragging || (e.GetPosition(this) - origin).Length < 3) return;
        dragging = true; bottomGap = null; ((UIElement)sender).ReleaseMouseCapture();
        try { DragMove(); } catch (InvalidOperationException) { }
        mouseDown = null; ReleasePress(); KeepVisible(); Snap(); RememberBottomAnchor(); ApplyTaskbar();
        settings.Left = Left; settings.Top = Top; settings.Save();
    }
    private async void OnUp(object sender, MouseButtonEventArgs e)
    {
        bool clicked = mouseDown.HasValue && !dragging; mouseDown = null;
        ((UIElement)sender).ReleaseMouseCapture(); ReleasePress();
        if (clicked) { if (bottomGap is null) RememberBottomAnchor(); ApplyTaskbar(); BubbleClick(); Play(false); if (bubbleOpen && !randomActive) await RefreshAsync(true); }
        e.Handled = true;
    }
    private void ReleasePress()
    {
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            squeeze.BeginAnimation(property, new DoubleAnimation(1, TimeSpan.FromSeconds(.22)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .5 } });
    }
    private Border BuildMenu()
    {
        var panel = new StackPanel { MinWidth = 196 };
        skinPicker = new ComboBox { ItemsSource = PetSkins.All, DisplayMemberPath = nameof(PetSkin.Name), SelectedItem = PetSkins.Find(settings.Skin), FontSize = 12, MinWidth = 110 };
        skinPicker.SelectionChanged += (_, _) =>
        {
            if (skinPicker.SelectedItem is not PetSkin skin) return;
            ApplySkin(skin.Id);
            if (!preview) settings.Save();
        };
        var skinRow = new DockPanel { Margin = new Thickness(0, 5, 0, 8) };
        skinRow.Children.Add(new TextBlock { Text = "皮肤", Width = 57, FontSize = 12, Foreground = Outline });
        skinRow.Children.Add(skinPicker); panel.Children.Add(skinRow);
        void SliderRow(string title, double lo, double hi, double value, double step, Action<double> save, Func<double, string> format)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var name = new TextBlock { Text = title, FontSize = 12, Foreground = Outline, VerticalAlignment = VerticalAlignment.Center, Width = 57 };
            var number = new TextBlock { Text = format(value), FontSize = 12, Foreground = Outline, Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(name, Dock.Left); DockPanel.SetDock(number, Dock.Right); row.Children.Add(name); row.Children.Add(number);
            var slider = new Slider { Minimum = lo, Maximum = hi, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, MinWidth = 88, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, _) => { save(slider.Value); number.Text = format(slider.Value); settings.Save(); };
            row.Children.Add(slider); panel.Children.Add(row);
        }
        SliderRow("大小", .6, 2.5, settings.Scale, .1, v =>
        {
            double right = Left + Width, bottom = Top + Height;
            settings.Scale = v; Width = Height = 250 * v;
            if (!settings.Mirror) Left = right - Width; Top = bottom - Height; KeepVisible(); UpdateMenuSize(); RememberBottomAnchor(); ApplyTaskbar();
        }, v => (Math.Round((v - .6) / .1) + 1).ToString("0"));
        SliderRow("呼吸速度", .1, 6, settings.BreatheSeconds, .1, v => { settings.BreatheSeconds = v; SetAnimation(); }, v => $"{v:0.0}s");
        SliderRow("呼吸幅度", 0, 4, settings.BreatheAmplitude, .05, v => { settings.BreatheAmplitude = v; SetAnimation(); }, v => $"{v * 100:0}%");
        SliderRow("呆毛速度", .1, 6, settings.AhogeSeconds, .1, v => { settings.AhogeSeconds = v; SetAnimation(); }, v => $"{v:0.0}s");
        SliderRow("呆毛幅度", 0, 4, settings.AhogeAmplitude, .05, v => { settings.AhogeAmplitude = v; SetAnimation(); }, v => $"{v * 100:0}%");
        var sounds = new ComboBox { ItemsSource = new[] { "小黄鸭", "音效1" }, SelectedIndex = settings.SoundSet == "fx1" ? 1 : 0, FontSize = 12 };
        sounds.SelectionChanged += (_, _) => { settings.SoundSet = sounds.SelectedIndex == 1 ? "fx1" : "duck"; settings.Save(); };
        var soundRow = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        soundRow.Children.Add(new TextBlock { Text = "音效", Width = 57, FontSize = 12, Foreground = Outline }); soundRow.Children.Add(sounds); panel.Children.Add(soundRow);
        SliderRow("音量", 0, 1, settings.Volume, .05, v => settings.Volume = v, v => $"{v * 100:0}%");
        var bubbles = new CheckBox { Content = "气泡", IsChecked = settings.BubbleOn, Foreground = Outline, FontSize = 12, Margin = new Thickness(0, 5, 0, 5) };
        bubbles.Click += (_, _) => { settings.BubbleOn = bubbles.IsChecked == true; if (!settings.BubbleOn) { StopAlerts(); HideBubble(true); } settings.Save(); }; panel.Children.Add(bubbles);
        var onTop = new CheckBox { Content = "置顶显示", IsChecked = settings.Topmost, Foreground = Outline, FontSize = 12, Margin = new Thickness(0, 5, 0, 5) };
        onTop.Click += (_, _) => { settings.Topmost = onTop.IsChecked == true; Topmost = settings.Topmost; settings.Save(); }; panel.Children.Add(onTop);
        SliderRow("语录概率", 0, 1, settings.QuoteChance, .05, v => settings.QuoteChance = v, v => $"{v * 100:0}%");
        void MenuAction(string title, Action action)
        {
            var button = new Button { Content = title, FontSize = 12, Foreground = Outline, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4) };
            button.Click += (_, _) => { menu.IsOpen = false; action(); }; panel.Children.Add(button);
        }
        MenuAction("编辑语录", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(quotes.FilePath) { UseShellExecute = true }));
        MenuAction("隐藏桌宠", HidePet);
        MenuAction("重置时间", () =>
        {
            StopAlerts(); randomActive = true; amount.FontSize = Base * 104 / 1026;
            amount.LineHeight = amount.FontSize * 1.05; amount.TextWrapping = TextWrapping.NoWrap; amount.MaxWidth = double.PositiveInfinity;
            SetLines("下次重置", ShortReset(data?.FiveHour), "每周 " + ResetDate(data?.Weekly)); OpenBubble();
        });
        panel.Children.Add(new Separator());
        var quit = new Button { Content = "退出", FontSize = 12, Foreground = Outline, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4) };
        quit.Click += (_, _) => Close(); panel.Children.Add(quit);
        return new Border { Child = panel, CornerRadius = new CornerRadius(10), Background = MenuFill, BorderBrush = MenuStroke, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10) };
    }

    internal async Task VerifyUiAsync(string output)
    {
        var results = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); results.Add("PASS " + name); }
        HideBubble(true);
        var speedSliders = ((StackPanel)((Border)menu.Child).Child).Children.OfType<DockPanel>()
            .Where(row => row.Children.OfType<TextBlock>().Any(t => t.Text is "呼吸速度" or "呆毛速度"))
            .SelectMany(row => row.Children.OfType<Slider>()).ToArray();
        Check(speedSliders.Length == 2 && speedSliders.All(s => s.Minimum == .1 && s.Maximum == 6), "Breathing and ahoge speed controls both span 0.1 to 6 seconds");
        Check(bubble.Visibility == Visibility.Collapsed && root.Children.Count == 2, "Idle contains only whale and hidden hover menu; no quota card");
        Check(Width == 200 && Height == 200 && Math.Abs(Width * WhaleFraction - 118.9) < .001, "Compact default: 119px whale; original proportions preserved");
        var oldSkinSource = skinBody.Source;
        skinPicker.SelectedItem = PetSkins.Find("mutsumi");
        Check(settings.Skin == "mutsumi" && skinBody.Source != oldSkinSource && skinBody.Clip is RectangleGeometry,
            "Settings skin selector immediately replaces body and separates the new ahoge");
        Check(skinHair.Source == skinBody.Source && ahoge.CenterY == PetSkins.Find("mutsumi").HairPivot.Y && Width == 200 && !bubbleOpen,
            "Mutsumi skin uses matching artwork layers without changing size or opening quota bubble");
        Check(System.Text.Json.JsonSerializer.Deserialize<PetSettings>(System.Text.Json.JsonSerializer.Serialize(settings))?.Skin == "mutsumi",
            "Selected skin survives settings serialization");
        Check(Outline.Color == (Color)ColorConverter.ConvertFromString("#304B3A") && BubbleFill.Color == (Color)ColorConverter.ConvertFromString("#F5F8EF") &&
              bubbleShapes.Cast<Shape>().All(s => ReferenceEquals(s.Stroke, Outline) && ReferenceEquals(s.Fill, BubbleFill)) && ReferenceEquals(amount.Foreground, Ink),
            "Mutsumi skin recolors cloud, both small bubbles, text and menu using the same green theme");
        var mintSource = skinBody.Source;
        skinPicker.SelectedItem = PetSkins.Find("dragon");
        Check(settings.Skin == "dragon" && skinBody.Source != mintSource && skinBody.Clip is CombinedGeometry && ReferenceEquals(skinBody.Source, skinHair.Source),
            "White dragon is a third distinct skin; its horns stay on the static body while only the central ahoge moves");
        Check(Outline.Color == (Color)ColorConverter.ConvertFromString("#534264") && BubbleFill.Color == (Color)ColorConverter.ConvertFromString("#F8F3FC") &&
              ReferenceEquals(amount.Foreground, Ink) && Width == 200,
            "White dragon changes bubble and text to lavender without resizing the pet");
        skinPicker.SelectedItem = PetSkins.Find("whale");
        Check(settings.Skin == "whale" && skinBody.Source == oldSkinSource && skinBody.Clip is null,
            "Original skin restores immediately from cache");
        Check(Outline.Color == (Color)ColorConverter.ConvertFromString("#203170") && BubbleFill.Color == Colors.White,
            "Switching back restores original blue and white bubble theme");
        Render(); Check(bubble.Visibility == Visibility.Collapsed, "Background data rendering never opens bubble");
        ShowQuota(false); Check(label.Text == "5小时剩余额度" && amount.Text == "76%" && hint.Text == "每周剩余 74%", "First click shows exactly three short quota lines");
        Check(bubbleShapes.Count == 3 && bubble.Visibility == Visibility.Visible, "Original cloud geometry and two thought dots");
        settings.QuoteChance = 0; BubbleClick(); Check(randomActive && amount.Text == "2 次", "Second click normally shows remaining reset count");
        BubbleClick(); Check(bubble.Visibility == Visibility.Collapsed, "Third click closes bubble");
        BubbleClick(false); Check(amount.Text == "76%" && !randomActive, "The same click cycle restarts with quota");
        settings.QuoteChance = 1; BubbleClick(false);
        Check(randomActive && quotes.Read().Contains(amount.Text), "Second click can show an original editable hidden quote");
        BubbleClick(); Check(!bubbleOpen, "Third click also closes hidden quotes");
        ShowQuota(false); ScheduleClose(); await Task.Delay(6000);
        Check(bubble.Visibility == Visibility.Collapsed, "Bubble auto closes after five seconds");
        double savedLeft = Left, savedTop = Top;
        ShowPet(); Render();
        Check(IsVisible && Left == savedLeft && Top == savedTop, "Showing or refreshing the independent pet never relocates it");
        Check(TaskbarAvoidance.ComputeBottom(0, 900, 900, new[] { (899d, 947d) }) == 898, "Hidden taskbar keeps a two-pixel mouse activation strip clear");
        Check(TaskbarAvoidance.ComputeBottom(0, 900, 900, new[] { (852d, 900d) }) == 850 &&
              TaskbarAvoidance.ComputeBottom(0, 900, 900, new[] { (875d, 923d) }) == 850,
            "Rising taskbar reserves full height immediately at both bottom corners");
        Check(!TaskbarAvoidance.CanFall(850, 900) && !TaskbarAvoidance.CanFall(873, 900) && TaskbarAvoidance.CanFall(898, 900),
            "Free fall waits until taskbar is completely hidden");
        Check(TaskbarAvoidance.ComputeBottom(0, 900, 852, new[] { (852d, 900d) }) == 850,
            "Always visible taskbar is not counted twice");
        Top = 698; taskbarTarget = null; taskbarClearSamples = 0;
        MoveForTaskbar(new(0, 900, 850), 0);
        Check(Top == 650 && !HasAnimatedProperties, "Taskbar pushes pet upward immediately without a window movement animation");
        MoveForTaskbar(new(0, 900, 850), 0);
        Check(Top == 650, "Pet stays up throughout taskbar retraction");
        MoveForTaskbar(new(0, 900, 898), 0);
        Check(Top == 650, "First fully hidden sample does not drop the pet prematurely");
        MoveForTaskbar(new(0, 900, 898), 0);
        Check((double)GetAnimationBaseValue(TopProperty) == 698 && HasAnimatedProperties,
            "Second fully hidden sample starts one accelerating fall");
        await Task.Delay(400);
        Check(Math.Abs(Top - 698) < .1 && Left == savedLeft, "Fall lands exactly at original height without horizontal movement");
        BeginAnimation(TopProperty, null); Top = savedTop; taskbarTarget = null; taskbarClearSamples = 0;
        StopAlerts(); HideBubble(true); dropTracker.Reset();
        // Keep this diagnostic independent of physical pointer state during its awaits.
        dragging = false; mouseDown = null; menu.IsOpen = false;
        var baseline = data! with { FiveHour = new QuotaWindow(76, data!.FiveHour.ResetsAt) };
        AcceptSnapshot(baseline);
        Check(bubble.Visibility == Visibility.Collapsed, "Initial sample does not trigger an automatic reminder");
        var lower = baseline with { FiveHour = baseline.FiveHour with { Remaining = 75 } };
        AcceptSnapshot(lower);
        var active = alertRun;
        Check(active is not null && bubble.Visibility == Visibility.Visible && amount.Text == "75%", "One percentage point drop opens an automatic quota bubble");
        AcceptSnapshot(lower);
        Check(alertRun == active && notices.Count == 0, "Duplicate push and poll values never repeat a reminder");
        AcceptSnapshot(lower with { FiveHour = lower.FiveHour with { Remaining = 73 } });
        Check(notices.Count == 2 && alertRun == active, "Two further percentage points queue distinct bubbles");
        if (SystemParameters.ClientAreaAnimation)
            Check(bubbleShapes.All(s => (double)s.GetAnimationBaseValue(OpacityProperty) == 0 && s.RenderTransform is ScaleTransform grow && grow.HasAnimatedProperties) &&
                  (double)words.GetAnimationBaseValue(OpacityProperty) == 0,
                "Sequential bubbles start hidden and grow from 70 percent before text fades in");
        ShowQuota(false);
        Check(alertRun is null && notices.Count == 0, "Manual interaction cancels pending automatic reminders");
        File.WriteAllLines(output, results);
    }
}

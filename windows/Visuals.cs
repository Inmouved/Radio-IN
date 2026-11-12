using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace RadioIN;

public sealed class MarqueeText : UserControl
{
    public static readonly DependencyProperty MotionModeProperty = DependencyProperty.Register("MotionMode",typeof(int),typeof(MarqueeText),new PropertyMetadata(0,(d,_) => ((MarqueeText)d).UpdateText()));
    public int MotionMode { get => (int)GetValue(MotionModeProperty); set => SetValue(MotionModeProperty,value); }
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MarqueeText), new PropertyMetadata("", (d, _) => ((MarqueeText)d).UpdateText()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    readonly TextBlock label = new() { TextWrapping = TextWrapping.NoWrap };
    readonly Canvas viewport = new() { ClipToBounds = true };
    readonly TranslateTransform offset = new();
    public bool IsScrolling { get; private set; }
    public double ScrollOffset => offset.X;
    public MarqueeText()
    {
        Focusable = false; ClipToBounds = true;
        viewport.Children.Add(label); Content = viewport; label.RenderTransform = offset;
        foreach (var property in new[] { TextBlock.FontSizeProperty, TextBlock.FontFamilyProperty, TextBlock.FontWeightProperty, TextBlock.ForegroundProperty })
            label.SetBinding(property, new Binding(property.Name) { Source = this });
        Loaded += (_, _) => UpdateText(); SizeChanged += (_, _) => UpdateText(); IsVisibleChanged += (_, _) => UpdateText();
        Unloaded += (_, _) => { offset.BeginAnimation(TranslateTransform.XProperty, null); IsScrolling = false; };
    }
    void UpdateText()
    {
        label.Text = Text ?? ""; System.Windows.Automation.AutomationProperties.SetName(this, label.Text);
        offset.BeginAnimation(TranslateTransform.XProperty, null); offset.X = 0; IsScrolling = false;
        label.Width = double.NaN; label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = label.DesiredSize.Width; label.Width = textWidth; viewport.Height = label.DesiredSize.Height;
        double distance = textWidth - ActualWidth;
        if (!IsLoaded || !IsVisible || ActualWidth < 1 || distance <= 1 || MotionMode == 2) return;
        double travel = Math.Max(1.5, distance / (MotionMode == 1 ? 15.0 : 30.0));
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + travel))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + travel))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + travel * 2))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5 + travel * 2))));
        IsScrolling = true; offset.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}

public partial class MainWindow
{
    bool listeningAnimated;
    int artworkTransition;
    Duration dissolve => new(TimeSpan.FromMilliseconds(preferences.AnimationMode == 2 ? 0 : preferences.AnimationMode == 1 ? 400 : 800));
    void InitializeVisuals()
    {
        CoverPlay.SetBinding(Button.ContentProperty, new Binding("Content") { Source = Play });
        CoverVolume.SetBinding(Slider.ValueProperty, new Binding("Value") { Source = Volume, Mode = BindingMode.TwoWay });
        CoverPlay.Click += TogglePlay;
        CoverStage.SizeChanged += (_, _) => { if (CoverStage.ActualWidth > 0) CoverStage.Height = CoverStage.ActualWidth; };
        CoverHud.LostKeyboardFocus += (_, _) => { if (!IsMouseOver) SetCoverControls(false); };
        IsVisibleChanged += (_, _) => UpdateListeningAnimation();
        Closing += (_, _) => UpdateListeningAnimation(false);
        if (Environment.GetCommandLineArgs().Contains("--beta-test")) Loaded += async (_, _) => await TestBeta();
        if (Environment.GetCommandLineArgs().Contains("--scroll-test")) Loaded += async (_, _) => await TestScrolling();
        if (Environment.GetCommandLineArgs().Contains("--volume-test")) Loaded += async (_, _) => await TestVolume();
    }
    async Task TestVolume()
    {
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); results.Add("PASS: " + name); }
        try {
            await WaitForStartup();
            foreach (int mode in new[] { 0, 2, 3 }) {
                ChangeMode(mode); await Task.Delay(350);
                if (mode == 3) { SetCoverControls(true); await Task.Delay(250); }
                UpdateLayout();
                var slider = mode == 3 ? CoverVolume : Volume; slider.ApplyTemplate(); slider.UpdateLayout();
                var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
                double usable = track.ActualWidth - track.Thumb.ActualWidth;
                Check(slider.IsMoveToPointEnabled && usable > 0, modes[mode] + " : clic à la position activé");
                foreach (double ratio in new[] { 0d, .2, .5, .8, 1d }) {
                    slider.UpdateLayout();
                    double value = track.ValueFromPoint(new Point(track.Thumb.ActualWidth / 2 + usable * ratio, track.ActualHeight / 2));
                    Check(Math.Abs(value - ratio) < .001, modes[mode] + " : position " + (int)(ratio * 100) + " % exacte");
                    slider.SetCurrentValue(Slider.ValueProperty, value);
                    Check(Math.Abs(Volume.Value - ratio) < .001 && Math.Abs(CoverVolume.Value - ratio) < .001, modes[mode] + " : volume principal et pochette synchronisés à " + (int)(ratio * 100) + " %");
                }
                slider.SetCurrentValue(Slider.ValueProperty, .5);
                Slider.DecreaseSmall.Execute(null, slider);
                Check(Math.Abs(Volume.Value - .49) < .001, modes[mode] + " : clavier par pas de 1 %");
                Slider.IncreaseLarge.Execute(null, slider);
                Check(Math.Abs(Volume.Value - .59) < .001, modes[mode] + " : grand pas limité à 10 %");
                slider.SetCurrentValue(Slider.ValueProperty, .5);
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(usable * .2, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                Check(Math.Abs(Volume.Value - .7) < .001, modes[mode] + " : glissement du curseur vers 70 %");
                Check(!CanStartWindowDrag(this, track.Thumb), modes[mode] + " : volume exclu du déplacement de fenêtre");
            }
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "volume-test.txt"), results); Close();
    }
    void SetCoverControls(bool visible)
    {
        CoverHud.IsHitTestVisible = visible;
        if (preferences.AnimationMode == 2) { CoverHud.BeginAnimation(UIElement.OpacityProperty,null); CoverHud.Opacity = visible ? 1 : 0; return; }
        var fade = new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => UpdateListeningAnimation(); CoverHud.BeginAnimation(UIElement.OpacityProperty, fade);
    }
    void TransitionArtwork(ImageSource? next)
    {
        int version = ++artworkTransition;
        next = next ?? Icon;
        var previous = Art.Source;
        PreviousArt.Source = PreviousCoverArt.Source = PreviousBackdrop.ImageSource = previous;
        Art.Source = CoverArt.Source = Backdrop.ImageSource = next;
        if (preferences.AnimationMode == 2) { foreach (var element in new UIElement[] { PreviousArt,PreviousCoverArt,PreviousBackdropLayer }) element.BeginAnimation(UIElement.OpacityProperty,null); PreviousArt.Source = PreviousCoverArt.Source = PreviousBackdrop.ImageSource = null; return; }
        var fade = new DoubleAnimation(1, 0, dissolve) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if (version == artworkTransition) PreviousArt.Source = PreviousCoverArt.Source = PreviousBackdrop.ImageSource = null; };
        PreviousArt.Opacity = PreviousCoverArt.Opacity = PreviousBackdropLayer.Opacity = 0;
        if (previous != null) {
            PreviousArt.BeginAnimation(UIElement.OpacityProperty, fade);
            PreviousCoverArt.BeginAnimation(UIElement.OpacityProperty, fade);
            PreviousBackdropLayer.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
    void TransitionColors(Color start, Color end, Color accent)
    {
        var old = TintLayer.Background as LinearGradientBrush;
        var gradient = new LinearGradientBrush(start, end, 45);
        var oldAccent = (Play.Background as SolidColorBrush)?.Color ?? accent;
        var button = new SolidColorBrush(accent);
        TintLayer.Background = gradient; Play.Background = button;
        CoverPlay.Background = button;
        if (InlineNotice != null) InlineNotice.Background = button;
        if (!IsLoaded || preferences.AnimationMode == 2) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        gradient.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(old?.GradientStops[0].Color ?? start, start, dissolve) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        gradient.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(old?.GradientStops[1].Color ?? end, end, dissolve) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        button.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(oldAccent, accent, dissolve) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    void UpdateListeningAnimation(bool? force = null)
    {
        bool active = preferences.AnimationMode != 2 && !muted && (force ?? (playing && audio?.State == 3 && !retryAt.HasValue && Volume.Value > 0 && IsVisible && (ArtRow.IsVisible || displayMode == 3 && CoverHud.Opacity > 0)));
        if (active == listeningAnimated) return;
        listeningAnimated = active; ListeningAnimation.Opacity = active ? .85 : .35;
        CoverPlay.BeginAnimation(UIElement.OpacityProperty, null);
        if (active && preferences.AnimationMode == 0) CoverPlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var bars = new[] { PulseOne, PulseTwo, PulseThree };
        for (int i = 0; i < bars.Length; i++) {
            var scale = (ScaleTransform)bars[i].RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleY = .3;
            if (active) scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(preferences.AnimationMode == 1 ? .4 : .25 + .1 * i, preferences.AnimationMode == 1 ? .7 : 1 - .1 * i, TimeSpan.FromMilliseconds(preferences.AnimationMode == 1 ? 900 + i * 130 : 420 + i * 130)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        }
    }
    static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindVisual<T>(child); if (nested != null) return nested;
        }
        return null;
    }
    async Task TestScrolling()
    {
        var results = new List<string>();
        void Require(bool condition, string name) { results.Add(name + "=" + condition); if (!condition) throw new InvalidOperationException(name); }
        async Task TestList(ListBox list, string name) {
            list.ApplyTemplate(); UpdateLayout();
            var viewer = FindVisual<ScrollViewer>(list) ?? throw new InvalidOperationException("Missing scroll viewer");
            viewer.ApplyTemplate(); UpdateLayout();
            var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            bar.ApplyTemplate(); UpdateLayout();
            var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
            results.Add($"{name} styles list={list.Style != null}/{DependencyPropertyHelper.GetValueSource(list, Control.TemplateProperty).BaseValueSource}; viewer={viewer.Style != null}/{DependencyPropertyHelper.GetValueSource(viewer, Control.TemplateProperty).BaseValueSource}; bar={bar.Style != null}/{DependencyPropertyHelper.GetValueSource(bar, Control.TemplateProperty).BaseValueSource}; requestedWidth={bar.Width}; min={bar.MinWidth}");
            results.Add("Application keys=" + string.Join(",", Application.Current.Resources.Keys.Cast<object>().Select(k => k.ToString())));
            results.Add($"{name} scrollbar visible={bar.IsVisible}; width={bar.ActualWidth}; range={viewer.ScrollableHeight}");
            Require(bar.IsVisible && Math.Abs(bar.ActualWidth - 12) < .1, name + " custom 12px scrollbar");
            Require(track != null && track.Thumb != null && viewer.ScrollableHeight > 0, name + " overflow and thumb");
            viewer.ScrollToTop(); await Task.Delay(80); UpdateLayout();
            viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
            await Task.Delay(80); UpdateLayout(); Require(viewer.VerticalOffset > 0, name + " mouse wheel");
            viewer.ScrollToTop(); await Task.Delay(80); UpdateLayout();
            ScrollBar.PageDownCommand.Execute(null, bar); await Task.Delay(80); UpdateLayout(); Require(viewer.VerticalOffset > 0, name + " track page down");
            viewer.ScrollToTop(); await Task.Delay(80); UpdateLayout();
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 70) { RoutedEvent = Thumb.DragDeltaEvent });
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 70, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Task.Delay(80); UpdateLayout(); Require(viewer.VerticalOffset > 0, name + " thumb drag");
            list.Focus(); list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), Environment.TickCount, Key.End) { RoutedEvent = Keyboard.KeyDownEvent });
            await Task.Delay(80); UpdateLayout(); Require(list.SelectedIndex == list.Items.Count - 1, name + " keyboard End");
            Require(viewer.VerticalOffset >= viewer.ScrollableHeight - .1, name + " last item reachable");
            var original = list.ItemsSource;
            list.ItemsSource = new[] { new Track("Court", "Test", "") }; await Task.Delay(80); UpdateLayout();
            Require(!bar.IsVisible, name + " hides without overflow");
            list.ItemsSource = original; await Task.Delay(80); UpdateLayout();
        }
        try {
            await WaitForStartup(); await Refresh(); ChangeMode(2);
            var deadline = DateTime.UtcNow.AddSeconds(20); while (loading && DateTime.UtcNow < deadline) await Task.Delay(100);
            Require(!loading && Items.Items.Count > 0, "Catalogue loaded");
            results.Add($"Large layout before scrolling: Width={Width}; ActualWidth={ActualWidth}; mode={displayMode}");
            Require(ActualWidth >= 850, "Large mode width");
            await TestList(LiveHistory, "History"); await TestList(Items, "Requests");
            Search.Text = "Dredg"; await Task.Delay(600); deadline = DateTime.UtcNow.AddSeconds(20); while (loading && DateTime.UtcNow < deadline) await Task.Delay(100);
            Require(Items.Items.Count > 0 && Items.Items.Cast<Track>().All(t => t.Detail == "Dredg"), "Search after styling");
            Require(!Search.GetRectFromCharacterIndex(0).IsEmpty, "Search text rendered");
            Search.Text = ""; await Task.Delay(600); while (loading && DateTime.UtcNow < deadline) await Task.Delay(100);
            results.Add($"Large layout after scrolling: Width={Width}; ActualWidth={ActualWidth}; mode={displayMode}");
            FindVisual<ScrollViewer>(LiveHistory)?.ScrollToTop(); FindVisual<ScrollViewer>(Items)?.ScrollToTop(); UpdateLayout();
            var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
            preview.Render((Visual)Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
            using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "scroll-preview.png"))) encoder.Save(output);
            ChangeMode(0); ShowHistory(this, new RoutedEventArgs()); await TestList(Items, "Compact history");
            for (int i = 0; i < designs.Length; i++) { ApplyDesign(i); UpdateLayout(); Require(FindVisual<ScrollViewer>(Items) != null, "Design " + designs[i]); }
            ChangeMode(3); UpdateLayout(); Require(CoverStage.IsVisible, "Cover mode");
            ChangeMode(1); UpdateLayout(); Require(ReducedSong.IsVisible, "Reduced mode");
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "scroll-test.txt"), results); Close();
    }
    async Task TestBeta()
    {
        var results = new List<string>();
        try {
            await WaitForStartup(); await Refresh();
            ChangeMode(3); UpdateLayout(); SetCoverControls(true); await Task.Delay(350);
            results.Add($"Cover square={CoverStage.ActualWidth}x{CoverStage.ActualHeight}; controls={CoverHud.Opacity}; requests={Drawer.Visibility}");
            SetCoverControls(false); await Task.Delay(300); results.Add("Cover idle=" + CoverHud.Opacity);
            ChangeMode(0); SongTitle.Text = "Un titre volontairement très long pour vérifier le défilement fluide et la fin du morceau sans troncature"; UpdateLayout(); await Task.Delay(2200);
            results.Add($"Long title scrolling={SongTitle.IsScrolling}; offset={SongTitle.ScrollOffset}");
            SongTitle.Text = "Court"; UpdateLayout(); results.Add($"Short title scrolling={SongTitle.IsScrolling}");
            var image = new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            image.WritePixels(new Int32Rect(0,0,2,2), new byte[] { 20,80,200,255,20,80,200,255,20,80,200,255,20,80,200,255 }, 8, 0);
            var previous = Art.Source; TransitionArtwork(image); await Task.Delay(250); results.Add($"Crossfade old={PreviousArt.Source != null}; opacity={PreviousArt.Opacity}");
            await Task.Delay(800); results.Add($"Crossfade released={PreviousArt.Source == null}; cover synced={CoverArt.Source == Art.Source}");
            TransitionArtwork(previous); ApplyDesign(2); await Task.Delay(200); results.Add("Colour animation=" + ((LinearGradientBrush)TintLayer.Background).GradientStops[0].HasAnimatedProperties); await Task.Delay(900);
            TogglePlay(this, new RoutedEventArgs()); await Task.Delay(10000); CheckAudio(); UpdateListeningAnimation();
            results.Add($"Audio state={audio?.State}; time={audio?.Time}; animation={listeningAnimated}");
            Volume.Value = 0; UpdateListeningAnimation(); results.Add("Muted animation=" + listeningAnimated); Volume.Value = .35;
            TogglePlay(this, new RoutedEventArgs()); UpdateListeningAnimation(); results.Add("Stopped animation=" + listeningAnimated);
            ChangeMode(3); SavePreferences(); results.Add("Saved cover mode=" + preferences.Mode); ChangeMode(0);
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "beta-test.txt"), results); Close();
    }
}

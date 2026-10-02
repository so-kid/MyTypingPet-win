using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyTypingPet;

public partial class PetWindow : Window
{
    public static readonly string[] SizeNames = { "小", "中", "大", "特大" };
    static readonly double[] SizeWidths = { 160, 240, 320, 440 };

    const double BounceRatio = 0.08; // 跳ねる高さ (画像の高さに対する割合)

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    readonly Settings _settings;
    readonly BitmapSource[] _images = new BitmapSource[3];
    readonly Dictionary<KeyTrigger, BitmapSource> _patternImages = new();
    static readonly TimeSpan HandHoldTime = TimeSpan.FromMilliseconds(300); // 手を上げたまま待つ時間
    readonly DispatcherTimer _idleTimer = new(); // 待機の絵に戻すまでのタイマー
    RawInput? _input;
    GamepadReader? _pad;
    bool _nextLeft = true;
    double _bounceHeight;

    /// <summary>ペットが右クリックされたとき (メニュー表示は App 側で行う)。</summary>
    public event Action? MenuRequested;

    public PetWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;

        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            PetImage.Source = _images[(int)Pose.Idle];
        };

        ReloadImages();
        ApplySize();
        Topmost = settings.Topmost;

        if (settings.Left is double left && settings.Top is double top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
        }
        else
        {
            ResetPosition();
        }

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonUp += (_, _) => MenuRequested?.Invoke();
        Closed += (_, _) =>
        {
            _input?.Dispose();
            _pad?.Dispose();
        };
    }

    IntPtr Handle => new WindowInteropHelper(this).Handle;

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Alt+Tab に出さない
        SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        SetLocked(_settings.Locked);

        _input = new RawInput(HwndSource.FromHwnd(Handle));
        _input.KeyPressed += OnKeyPressed;
        _input.ModifierTapped += ShowPatternIfAny;
        _input.MouseClicked += ShowPatternIfAny; // クリックは登録したものにだけ反応する

        _pad = new GamepadReader(Dispatcher);
        _pad.PadPressed += OnKeyPressed; // パッドのボタンはキーと同じく手を動かす
    }

    /// <summary>パターン登録ダイアログがパッドの入力を受け取るために使う。</summary>
    public GamepadReader? Pad => _pad;

    void OnKeyPressed(KeyTrigger key)
    {
        _idleTimer.Stop();

        // 修飾キー単独のパターンは離したときに判定する (Ctrl+S の Ctrl で発動させないため)
        if (!key.IsModifierOnly && FindKeyPattern(key) is BitmapSource pattern)
        {
            ShowPattern(pattern);
            return;
        }

        PetImage.Source = _images[(int)(_nextLeft ? Pose.Left : Pose.Right)];
        _nextLeft = !_nextLeft;
        _idleTimer.Interval = HandHoldTime;
        _idleTimer.Start();
        PlayBounce();
    }

    /// <summary>パターンの絵を設定の秒数だけ出す。0 秒なら次の入力まで出しっぱなし。</summary>
    void ShowPattern(BitmapSource pattern)
    {
        _idleTimer.Stop();
        PetImage.Source = pattern;
        if (_settings.PatternSeconds > 0)
        {
            _idleTimer.Interval = TimeSpan.FromSeconds(_settings.PatternSeconds);
            _idleTimer.Start();
        }
        PlayBounce();
    }

    /// <summary>特定のキーのパターンを優先し、なければ「修飾キー + 任意のキー」を探す。</summary>
    BitmapSource? FindKeyPattern(KeyTrigger key)
    {
        if (_patternImages.TryGetValue(key, out var exact))
            return exact;
        if (key.Mods != Mods.None && _patternImages.TryGetValue(key with { VKey = KeyTrigger.AnyKey }, out var any))
            return any;
        return null;
    }

    void ShowPatternIfAny(KeyTrigger key)
    {
        if (_patternImages.TryGetValue(key, out var pattern))
            ShowPattern(pattern);
    }

    void PlayBounce()
    {
        var fall = new DoubleAnimation(-_bounceHeight, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        Bounce.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, fall);
    }

    void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove(); // 離すまで戻ってこない
        SavePosition();
    }

    public void SavePosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    public void ResetPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height;
    }

    static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50
        && top >= SystemParameters.VirtualScreenTop - 50
        && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
        && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    public void ApplySize()
    {
        int index = Math.Clamp(_settings.SizeIndex, 0, SizeWidths.Length - 1);
        var idle = _images[(int)Pose.Idle];
        double width = SizeWidths[index];
        double imageHeight = width * idle.PixelHeight / idle.PixelWidth;
        _bounceHeight = imageHeight * BounceRatio;

        // 足元の位置を保ったままサイズを変える
        double bottom = Top + Height;
        PetImage.Width = width;
        PetImage.Height = imageHeight;
        Width = width;
        Height = imageHeight + _bounceHeight;
        if (!double.IsNaN(bottom))
            Top = bottom - Height;
    }

    public void SetLocked(bool locked)
    {
        if (Handle == IntPtr.Zero)
            return;
        int style = GetWindowLong(Handle, GWL_EXSTYLE);
        style = locked ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, style);
    }

    public void ReloadImages()
    {
        _images[(int)Pose.Idle] = LoadImage(_settings.IdleImage, Pose.Idle);
        _images[(int)Pose.Left] = LoadImage(_settings.LeftImage, Pose.Left);
        _images[(int)Pose.Right] = LoadImage(_settings.RightImage, Pose.Right);

        _patternImages.Clear();
        foreach (var p in _settings.Patterns)
        {
            if (TryLoadFile(p.Image) is BitmapSource bmp)
                _patternImages[p.Trigger] = bmp;
        }

        PetImage.Source = _images[(int)Pose.Idle];
    }

    static BitmapSource LoadImage(string? path, Pose pose) => TryLoadFile(path) ?? DefaultArt.Render(pose);

    static BitmapSource? TryLoadFile(string? path)
    {
        if (path != null && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // ファイルをロックしない
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                App.Log($"画像を読み込めませんでした: {path}", ex);
            }
        }
        return null;
    }
}

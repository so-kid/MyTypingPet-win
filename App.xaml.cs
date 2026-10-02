using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace MyTypingPet;

public partial class App : Application
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "MyTypingPet";

    Mutex? _singleInstance;
    Settings _settings = null!;
    PetWindow _pet = null!;
    WinForms.NotifyIcon _tray = null!;
    WinForms.ContextMenuStrip _menu = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "MyTypingPet.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log("予期しないエラー", args.Exception);
            MessageBox.Show(args.Exception.Message, "MyTypingPet", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _settings = Settings.Load();
        _pet = new PetWindow(_settings);
        _menu = BuildMenu();
        _pet.MenuRequested += () => _menu.Show(WinForms.Cursor.Position);

        _tray = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "MyTypingPet",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
                _pet.Activate();
        };

        _pet.Show();
    }

    WinForms.ContextMenuStrip BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        var image = new WinForms.ToolStripMenuItem("画像を変更");
        image.DropDownItems.Add("待機...", null, (_, _) => PickImage(Pose.Idle));
        image.DropDownItems.Add("左手...", null, (_, _) => PickImage(Pose.Left));
        image.DropDownItems.Add("右手...", null, (_, _) => PickImage(Pose.Right));
        image.DropDownItems.Add(new WinForms.ToolStripSeparator());
        image.DropDownItems.Add("既定の絵に戻す", null, (_, _) =>
        {
            ImageFiles.DeleteCurrent(_settings.IdleImage);
            ImageFiles.DeleteCurrent(_settings.LeftImage);
            ImageFiles.DeleteCurrent(_settings.RightImage);
            _settings.IdleImage = _settings.LeftImage = _settings.RightImage = null;
            ApplyImages();
        });

        // 空だと ▶ が出ないので、開くときに作り直す項目には仮の項目を入れておく
        var patterns = new WinForms.ToolStripMenuItem("パターン");
        patterns.DropDownItems.Add("(読み込み中)");
        patterns.DropDownOpening += (_, _) => FillPatternMenu(patterns);

        var patternTime = new WinForms.ToolStripMenuItem("パターンの表示時間");
        foreach (double seconds in Settings.PatternSecondsChoices)
        {
            double s = seconds;
            var item = new WinForms.ToolStripMenuItem(s > 0 ? $"{s} 秒" : "次の入力まで", null, (_, _) =>
            {
                _settings.PatternSeconds = s;
                _settings.Save();
            })
            {
                Tag = s,
            };
            patternTime.DropDownItems.Add(item);
        }

        var presets = new WinForms.ToolStripMenuItem("プリセット");
        presets.DropDownItems.Add("(読み込み中)");
        presets.DropDownOpening += (_, _) => FillPresetMenu(presets);

        var size = new WinForms.ToolStripMenuItem("サイズ");
        for (int i = 0; i < PetWindow.SizeNames.Length; i++)
        {
            int index = i;
            size.DropDownItems.Add(PetWindow.SizeNames[i], null, (_, _) =>
            {
                _settings.SizeIndex = index;
                _pet.ApplySize();
                _pet.SavePosition();
            });
        }

        var topmost = new WinForms.ToolStripMenuItem("常に最前面", null, (_, _) =>
        {
            _settings.Topmost = !_settings.Topmost;
            _pet.Topmost = _settings.Topmost;
            _settings.Save();
        });

        var locked = new WinForms.ToolStripMenuItem("位置ロック (クリックを透過)", null, (_, _) =>
        {
            _settings.Locked = !_settings.Locked;
            _pet.SetLocked(_settings.Locked);
            _settings.Save();
        });

        var reset = new WinForms.ToolStripMenuItem("位置リセット", null, (_, _) =>
        {
            _pet.ResetPosition();
            _pet.SavePosition();
        });

        var autoStart = new WinForms.ToolStripMenuItem("PC起動時に実行", null, (_, _) => SetAutoStart(!IsAutoStart()));

        menu.Opening += (_, _) =>
        {
            for (int i = 0; i < size.DropDownItems.Count; i++)
                ((WinForms.ToolStripMenuItem)size.DropDownItems[i]).Checked = i == _settings.SizeIndex;
            foreach (WinForms.ToolStripMenuItem item in patternTime.DropDownItems)
                item.Checked = (double)item.Tag! == _settings.PatternSeconds;
            topmost.Checked = _settings.Topmost;
            locked.Checked = _settings.Locked;
            autoStart.Checked = IsAutoStart();
        };

        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            image, patterns, patternTime, presets, size, topmost, locked, reset, autoStart,
            new WinForms.ToolStripSeparator(),
        });
        menu.Items.Add("終了", null, (_, _) => Quit());
        return menu;
    }

    void FillPatternMenu(WinForms.ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var add = new WinForms.ToolStripMenuItem($"パターンを追加... ({_settings.Patterns.Count}/{Settings.MaxPatterns})", null, (_, _) => AddPattern())
        {
            Enabled = _settings.Patterns.Count < Settings.MaxPatterns,
        };
        parent.DropDownItems.Add(add);
        if (_settings.Patterns.Count > 0)
            parent.DropDownItems.Add(new WinForms.ToolStripSeparator());

        foreach (var pattern in _settings.Patterns)
        {
            var p = pattern;
            var item = new WinForms.ToolStripMenuItem(p.Trigger.ToString(), LoadThumbnail(p.Image));
            item.DropDownItems.Add("キーを変更...", null, (_, _) =>
            {
                if (AskTrigger(except: p) is KeyTrigger trigger)
                {
                    p.Trigger = trigger;
                    SavePatterns();
                }
            });
            item.DropDownItems.Add("画像を変更...", null, (_, _) =>
            {
                if (AskImagePath($"「{p.Trigger}」の画像を選択") is string path)
                {
                    string? old = p.Image;
                    p.Image = ImageFiles.CopyToCurrent(path, "pattern");
                    ImageFiles.DeleteCurrent(old);
                    SavePatterns();
                }
            });
            item.DropDownItems.Add("削除", null, (_, _) =>
            {
                _settings.Patterns.Remove(p);
                ImageFiles.DeleteCurrent(p.Image);
                SavePatterns();
            });
            parent.DropDownItems.Add(item);
        }
    }

    void AddPattern()
    {
        if (AskTrigger(except: null) is not KeyTrigger trigger)
            return;
        if (AskImagePath($"「{trigger}」で表示する画像を選択") is not string path)
            return;

        _settings.Patterns.Add(new Pattern
        {
            Trigger = trigger,
            Image = ImageFiles.CopyToCurrent(path, "pattern"),
        });
        SavePatterns();
    }

    void FillPresetMenu(WinForms.ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var save = new WinForms.ToolStripMenuItem($"現在の設定を保存... ({_settings.Presets.Count}/{Settings.MaxPresets})", null, (_, _) => SavePreset())
        {
            Enabled = _settings.Presets.Count < Settings.MaxPresets,
        };
        parent.DropDownItems.Add(save);
        if (_settings.Presets.Count > 0)
            parent.DropDownItems.Add(new WinForms.ToolStripSeparator());

        foreach (var preset in _settings.Presets)
        {
            var p = preset;
            var item = new WinForms.ToolStripMenuItem(p.Name, LoadThumbnail(p.IdleImage))
            {
                ToolTipText = $"パターン {p.Patterns.Count} 個",
            };
            item.DropDownItems.Add("適用", null, (_, _) =>
            {
                if (!Confirm($"「{p.Name}」を適用します。\n今の画像とパターンは置き換わります。"))
                    return;
                p.ApplyTo(_settings);
                ApplyImages();
            });
            item.DropDownItems.Add("今の設定で上書き", null, (_, _) =>
            {
                if (!Confirm($"「{p.Name}」を今の画像とパターンで上書きします。"))
                    return;
                p.CaptureFrom(_settings);
                _settings.Save();
            });
            item.DropDownItems.Add("名前を変更...", null, (_, _) =>
            {
                if (AskName("新しい名前", p.Name) is string name)
                {
                    p.Name = name;
                    _settings.Save();
                }
            });
            item.DropDownItems.Add("削除", null, (_, _) =>
            {
                if (!Confirm($"「{p.Name}」を削除します。"))
                    return;
                _settings.Presets.Remove(p);
                p.DeleteFiles();
                _settings.Save();
            });
            parent.DropDownItems.Add(item);
        }
    }

    void SavePreset()
    {
        if (AskName("保存するプリセットの名前", $"プリセット {_settings.Presets.Count + 1}") is not string name)
            return;
        var preset = new Preset { Name = name };
        preset.CaptureFrom(_settings);
        _settings.Presets.Add(preset);
        _settings.Save();
    }

    static string? AskName(string prompt, string initial)
    {
        var dialog = new NameDialog(prompt, initial);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    static bool Confirm(string message) =>
        MessageBox.Show(message, "MyTypingPet", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    KeyTrigger? AskTrigger(Pattern? except)
    {
        var dialog = new TriggerDialog(trigger =>
        {
            foreach (var p in _settings.Patterns)
            {
                if (p != except && p.Trigger == trigger)
                    return $"「{trigger}」は別のパターンで使っています。";
            }
            return null;
        }, _pet.Pad);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    void SavePatterns()
    {
        _settings.Save();
        _pet.ReloadImages();
    }

    static System.Drawing.Image? LoadThumbnail(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path))
                return null;
            // FromFile はファイルをロックするのでメモリ経由で読む
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var full = System.Drawing.Image.FromStream(stream);
            const int h = 20;
            int w = Math.Max(1, full.Width * h / Math.Max(1, full.Height));
            return new System.Drawing.Bitmap(full, Math.Min(w, 48), h);
        }
        catch (Exception ex)
        {
            Log($"サムネイルを作れませんでした: {path}", ex);
            return null;
        }
    }

    static string? AskImagePath(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "画像 (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    void PickImage(Pose pose)
    {
        if (AskImagePath($"{PoseName(pose)}の画像を選択 (推奨 800x500, 透過PNG)") is not string path)
            return;
        string dest = ImageFiles.CopyToCurrent(path, pose.ToString().ToLowerInvariant());

        string? old;
        switch (pose)
        {
            case Pose.Idle: old = _settings.IdleImage; _settings.IdleImage = dest; break;
            case Pose.Left: old = _settings.LeftImage; _settings.LeftImage = dest; break;
            default: old = _settings.RightImage; _settings.RightImage = dest; break;
        }
        ImageFiles.DeleteCurrent(old);
        ApplyImages();
    }

    static string PoseName(Pose pose) => pose switch
    {
        Pose.Left => "左手",
        Pose.Right => "右手",
        _ => "待機",
    };

    void ApplyImages()
    {
        _pet.ReloadImages();
        _pet.ApplySize(); // 待機画像の縦横比が変わるかもしれない
        _pet.SavePosition();
    }

    static bool IsAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) != null;
    }

    static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    static System.Drawing.Icon CreateTrayIcon()
    {
        // 既定の絵から顔のあたりを切り出してアイコンにする
        var face = new CroppedBitmap(DefaultArt.Render(Pose.Idle), new Int32Rect(200, 50, 400, 400));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(face));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var full = new System.Drawing.Bitmap(stream);
        using var small = new System.Drawing.Bitmap(full, 32, 32);
        return System.Drawing.Icon.FromHandle(small.GetHicon());
    }

    void Quit()
    {
        _pet.SavePosition();
        _tray.Visible = false;
        _tray.Dispose();
        _pet.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public static void Log(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.AppendAllText(Path.Combine(Settings.Dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch
        {
            // ログが書けなくても本体は止めない
        }
    }
}

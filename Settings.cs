using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MyTypingPet;

public sealed class Settings
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTypingPet");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public double? Left { get; set; }
    public double? Top { get; set; }
    public int SizeIndex { get; set; } = 1;
    public bool Topmost { get; set; } = true;
    public bool Locked { get; set; }

    // null のときは既定の絵を使う
    public string? IdleImage { get; set; }
    public string? LeftImage { get; set; }
    public string? RightImage { get; set; }

    public const int MaxPatterns = 10;

    /// <summary>パターンの絵を出しておく秒数。0 なら次の入力まで。</summary>
    public double PatternSeconds { get; set; } = 1.5;
    public static readonly double[] PatternSecondsChoices = { 1, 1.5, 2, 3, 0 };

    public List<Pattern> Patterns { get; set; } = new();

    public const int MaxPresets = 10;
    public List<Preset> Presets { get; set; } = new();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex)
        {
            App.Log("settings.json の読み込みに失敗したので初期設定で起動します", ex);
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

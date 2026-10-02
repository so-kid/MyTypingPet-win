using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace MyTypingPet;

/// <summary>待機・左手・右手の画像とパターンの一式。画像は専用フォルダにコピーして持つ。</summary>
public sealed class Preset
{
    public const int MaxNameLength = 16;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string? IdleImage { get; set; }
    public string? LeftImage { get; set; }
    public string? RightImage { get; set; }
    public List<Pattern> Patterns { get; set; } = new();

    [JsonIgnore]
    string FolderPath => Path.Combine(ImageFiles.PresetsDir, Id);

    /// <summary>今の画像とパターンをこのプリセットに写す (前の中身は捨てる)。</summary>
    public void CaptureFrom(Settings settings)
    {
        DeleteFiles();
        IdleImage = CopyIn(settings.IdleImage, "idle");
        LeftImage = CopyIn(settings.LeftImage, "left");
        RightImage = CopyIn(settings.RightImage, "right");
        Patterns = settings.Patterns
            .Select((p, i) => new Pattern { Trigger = p.Trigger, Image = CopyIn(p.Image, $"pattern{i + 1}") })
            .ToList();
    }

    /// <summary>このプリセットの画像とパターンを今の設定にする。今まで使っていた画像は消す。</summary>
    public void ApplyTo(Settings settings)
    {
        var oldImages = new List<string?> { settings.IdleImage, settings.LeftImage, settings.RightImage };
        oldImages.AddRange(settings.Patterns.Select(p => p.Image));

        settings.IdleImage = CopyOut(IdleImage, "idle");
        settings.LeftImage = CopyOut(LeftImage, "left");
        settings.RightImage = CopyOut(RightImage, "right");
        settings.Patterns = Patterns
            .Select(p => new Pattern { Trigger = p.Trigger, Image = CopyOut(p.Image, "pattern") })
            .ToList();

        foreach (var path in oldImages)
            ImageFiles.DeleteCurrent(path);
    }

    public void DeleteFiles()
    {
        if (Directory.Exists(FolderPath))
            Directory.Delete(FolderPath, recursive: true);
    }

    // 画像が未設定 (既定の絵) や行方不明のときは null のまま持つ
    string? CopyIn(string? source, string name) =>
        source != null && File.Exists(source) ? ImageFiles.CopyTo(source, FolderPath, name) : null;

    static string? CopyOut(string? source, string prefix) =>
        source != null && File.Exists(source) ? ImageFiles.CopyToCurrent(source, prefix) : null;
}

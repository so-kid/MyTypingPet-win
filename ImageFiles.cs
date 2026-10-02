using System;
using System.IO;

namespace MyTypingPet;

/// <summary>
/// AppData にコピーした画像の置き場所。
/// images\ 直下は今の設定が使う画像、images\presets\&lt;ID&gt;\ はプリセットが持つ画像。
/// 今の設定とプリセットが同じファイルを共有しないようにして、片方の変更がもう片方を壊さないようにする。
/// </summary>
public static class ImageFiles
{
    public static readonly string Dir = Path.Combine(Settings.Dir, "images");
    public static readonly string PresetsDir = Path.Combine(Dir, "presets");

    /// <summary>今の設定用に、重ならない名前でコピーする (元ファイルを動かされても困らないように)。</summary>
    public static string CopyToCurrent(string source, string prefix) =>
        CopyTo(source, Dir, $"{prefix}-{Guid.NewGuid():N}");

    public static string CopyTo(string source, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, name + Path.GetExtension(source));
        File.Copy(source, dest, overwrite: true);
        return dest;
    }

    /// <summary>今の設定用の画像を消す。プリセットの画像や AppData の外のファイルには触らない。</summary>
    public static void DeleteCurrent(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(PresetsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            File.Delete(full);
        }
        catch (IOException ex)
        {
            App.Log($"古い画像を消せませんでした: {full}", ex);
        }
    }
}

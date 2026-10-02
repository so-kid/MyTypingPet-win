using System;

namespace MyTypingPet;

/// <summary>
/// PlayStation のコントローラーを USB 接続したときの入力レポートからボタンを読む。
/// DualSense (PS5) と DualShock 4 (PS4) はボタンの並びが同じで、位置が 3 バイトずれているだけ。
/// どちらもレポート 0x01 (64 バイト)。Bluetooth は形式が違うので未対応。
/// </summary>
public static class Gamepad
{
    public const uint VendorId = 0x054C;

    /// <summary>対応している製品と、ボタンが始まるバイト位置・最後のバイトで使うビット。</summary>
    public sealed record Model(string Name, uint ProductId, int ButtonOffset, byte SystemMask);

    public static readonly Model[] Models =
    {
        new("DualSense", 0x0CE6, 8, 0x07),     // PS タッチパッド ミュート
        new("DualShock 4", 0x09CC, 5, 0x03),   // 後期型。PS タッチパッド (上位ビットはカウンター)
        new("DualShock 4", 0x05C4, 5, 0x03),   // 初期型
    };

    /// <summary>ボタンの番号 (= ビット位置) と表示名。DualShock 4 も同じ番号を使う。</summary>
    public static readonly string[] ButtonNames =
    {
        "×", "○", "□", "△",
        "十字↑", "十字→", "十字↓", "十字←",
        "L1", "R1", "L2", "R2",
        "Create/Share", "Options", "L3", "R3",
        "PS", "タッチパッド", "ミュート",
    };

    // 十字キー (ハットスイッチ 0〜7 = ↑ から時計回り) → ボタンのビット
    const uint Up = 1 << 4, Right = 1 << 5, Down = 1 << 6, Left = 1 << 7;
    static readonly uint[] Hat = { Up, Up | Right, Right, Down | Right, Down, Down | Left, Left, Up | Left };

    /// <summary>押されているボタンをビットの集合として返す。その機種の USB レポートでなければ false。</summary>
    public static bool TryReadButtons(Model model, ReadOnlySpan<byte> report, out uint buttons)
    {
        buttons = 0;
        if (report.Length < 64 || report[0] != 0x01)
            return false;

        int at = model.ButtonOffset;
        byte face = report[at], shoulder = report[at + 1], system = report[at + 2];

        if ((face & 0x20) != 0) buttons |= 1 << 0; // ×
        if ((face & 0x40) != 0) buttons |= 1 << 1; // ○
        if ((face & 0x10) != 0) buttons |= 1 << 2; // □
        if ((face & 0x80) != 0) buttons |= 1 << 3; // △

        int hat = face & 0x0F;
        if (hat < Hat.Length)
            buttons |= Hat[hat];

        buttons |= (uint)shoulder << 8;                  // L1 R1 L2 R2 Create/Share Options L3 R3 の順でそのまま並んでいる
        buttons |= (uint)(system & model.SystemMask) << 16; // PS タッチパッド (ミュート)
        return true;
    }
}

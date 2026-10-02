using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace MyTypingPet;

[Flags]
public enum Mods
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

/// <summary>
/// パターンの発動条件。通常は「修飾キー + 普通のキー」。VKey の値で次の特別な意味を持つ:
/// <list type="bullet">
/// <item>修飾キー: そのキーだけを押して離した (Mods は None)</item>
/// <item>マウスボタン (VK_LBUTTON など): 修飾キー + クリック</item>
/// <item><see cref="AnyKey"/>: 修飾キー + 何かのキー (Mods は必ず 1 つ以上)</item>
/// <item><see cref="PadBase"/> 以降: DualSense / DualShock 4 のボタン (Mods は None)</item>
/// </list>
/// </summary>
public readonly record struct KeyTrigger(Mods Mods, ushort VKey)
{
    public const ushort AnyKey = 0;
    public const ushort PadBase = 0x1000; // 仮想キーコード (0x00〜0xFF) と重ならない範囲
    public const ushort VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;
    const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    public bool IsModifierOnly => ModOf(VKey) != Mods.None;

    public bool IsMouse => VKey is VK_LBUTTON or VK_RBUTTON or VK_MBUTTON or VK_XBUTTON1 or VK_XBUTTON2;

    public static KeyTrigger Pad(int button) => new(Mods.None, (ushort)(PadBase + button));

    /// <summary>左右の区別をなくす (LShift → Shift など)。</summary>
    public static ushort Normalize(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => VK_SHIFT,
        0xA2 or 0xA3 => VK_CONTROL,
        0xA4 or 0xA5 => VK_MENU,
        VK_RWIN => VK_LWIN,
        _ => vk,
    };

    public static Mods ModOf(ushort vk) => Normalize(vk) switch
    {
        VK_SHIFT => Mods.Shift,
        VK_CONTROL => Mods.Ctrl,
        VK_MENU => Mods.Alt,
        VK_LWIN => Mods.Win,
        _ => Mods.None,
    };

    public override string ToString() => string.Join(" + ", ModNames(Mods, KeyName(VKey)));

    /// <summary>修飾キーだけの表示 ("Ctrl + Shift")。</summary>
    public static string ModsText(Mods mods)
    {
        var parts = ModNames(mods, "");
        parts.RemoveAt(parts.Count - 1);
        return string.Join(" + ", parts);
    }

    static List<string> ModNames(Mods mods, string last)
    {
        var parts = new List<string>();
        if (mods.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(Mods.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Mods.Win)) parts.Add("Win");
        parts.Add(last);
        return parts;
    }

    static string KeyName(ushort vk)
    {
        switch (ModOf(vk))
        {
            case Mods.Shift: return "Shift";
            case Mods.Ctrl: return "Ctrl";
            case Mods.Alt: return "Alt";
            case Mods.Win: return "Win";
        }

        switch (vk)
        {
            case AnyKey: return "任意のキー";
            case VK_LBUTTON: return "左クリック";
            case VK_RBUTTON: return "右クリック";
            case VK_MBUTTON: return "中クリック";
            case VK_XBUTTON1: return "サイドボタン (戻る)";
            case VK_XBUTTON2: return "サイドボタン (進む)";
        }

        if (vk >= PadBase && vk - PadBase < Gamepad.ButtonNames.Length)
            return "パッド " + Gamepad.ButtonNames[vk - PadBase];

        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
            Key.Return => "Enter",
            Key.Back => "BackSpace",
            Key.Capital => "CapsLock",
            Key.Escape => "Esc",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            Key.Space => "Space",
            Key.None => $"0x{vk:X2}",
            _ => key.ToString(),
        };
    }
}

public sealed class Pattern
{
    public Mods Mods { get; set; }
    public ushort VKey { get; set; }
    public string? Image { get; set; }

    [JsonIgnore]
    public KeyTrigger Trigger
    {
        get => new(Mods, VKey);
        set
        {
            Mods = value.Mods;
            VKey = value.VKey;
        }
    }
}

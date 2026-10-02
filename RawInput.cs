using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MyTypingPet;

/// <summary>
/// Raw Input でシステム全体のキー押下とマウスボタンを検知する (パッドは <see cref="GamepadReader"/>)。
/// どのキー・ボタンかはオートリピートの判定とパターンとの照合にしか使わず、保存も送信もしない。
/// </summary>
public sealed class RawInput : IDisposable
{
    const int WM_INPUT = 0x00FF;
    const uint RID_INPUT = 0x10000003;
    const uint RIM_TYPEMOUSE = 0;
    const uint RIM_TYPEKEYBOARD = 1;
    const uint RIDEV_REMOVE = 0x00000001;
    const uint RIDEV_INPUTSINK = 0x00000100; // 非アクティブでも受け取る
    const ushort RI_KEY_BREAK = 0x0001;
    const ushort UsageMouse = 0x02, UsageKeyboard = 0x06;

    // RAWMOUSE.usButtonFlags の「押した」ビットと、対応する仮想キーコード
    static readonly (ushort Flag, ushort VKey)[] MouseButtons =
    {
        (0x0001, KeyTrigger.VK_LBUTTON),
        (0x0004, KeyTrigger.VK_RBUTTON),
        (0x0010, KeyTrigger.VK_MBUTTON),
        (0x0040, KeyTrigger.VK_XBUTTON1),
        (0x0100, KeyTrigger.VK_XBUTTON2),
    };

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    readonly HwndSource _source;
    // マウス移動でも毎回呼ばれるので、バッファは使い回す
    IntPtr _buffer = Marshal.AllocHGlobal(128);
    int _bufferSize = 128;
    ushort _lastDown; // オートリピートは最後に押したキーでしか起きないので、それだけ覚えれば足りる
    ushort _soloModifier; // 単独で押されている修飾キー。他のキーやクリックがあったら 0 に戻す

    /// <summary>キーが押された (修飾キーを含む)。</summary>
    public event Action<KeyTrigger>? KeyPressed;

    /// <summary>修飾キーが他のキーと組み合わされずに押して離された。</summary>
    public event Action<KeyTrigger>? ModifierTapped;

    /// <summary>マウスボタンが押された。</summary>
    public event Action<KeyTrigger>? MouseClicked;

    public RawInput(HwndSource source)
    {
        _source = source;
        Register(RIDEV_INPUTSINK, source.Handle);
        _source.AddHook(WndProc);
    }

    static void Register(uint flags, IntPtr target)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = UsageKeyboard, Flags = flags, Target = target },
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = UsageMouse, Flags = flags, Target = target },
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT)
            return IntPtr.Zero;

        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0)
            return IntPtr.Zero;
        if (size > _bufferSize)
        {
            _buffer = Marshal.ReAllocHGlobal(_buffer, (IntPtr)size);
            _bufferSize = (int)size;
        }
        if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, headerSize) != size)
            return IntPtr.Zero;

        IntPtr body = _buffer + (int)headerSize;
        switch (Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer).Type)
        {
            case RIM_TYPEKEYBOARD:
                var kb = Marshal.PtrToStructure<RAWKEYBOARD>(body);
                ushort vk = KeyTrigger.Normalize(kb.VKey);
                if ((kb.Flags & RI_KEY_BREAK) != 0)
                    OnKeyUp(vk);
                else
                    OnKeyDown(vk);
                break;

            case RIM_TYPEMOUSE:
                ushort flags = Marshal.PtrToStructure<RAWMOUSE>(body).ButtonFlags;
                foreach (var (flag, button) in MouseButtons)
                {
                    if ((flags & flag) != 0)
                        OnMouseDown(button);
                }
                break;
        }
        // handled は立てない (DefWindowProc に後始末させる)
        return IntPtr.Zero;
    }

    void OnKeyDown(ushort vk)
    {
        if (vk == _lastDown)
            return;
        _lastDown = vk;

        Mods self = KeyTrigger.ModOf(vk);
        Mods others = HeldMods() & ~self;
        _soloModifier = self != Mods.None && others == Mods.None ? vk : (ushort)0;
        KeyPressed?.Invoke(new KeyTrigger(others, vk));
    }

    void OnKeyUp(ushort vk)
    {
        if (vk == _lastDown)
            _lastDown = 0;
        if (vk == _soloModifier)
        {
            _soloModifier = 0;
            ModifierTapped?.Invoke(new KeyTrigger(Mods.None, vk));
        }
    }

    void OnMouseDown(ushort button)
    {
        _soloModifier = 0; // Ctrl + クリックの後に Ctrl 単独パターンが出ないように
        MouseClicked?.Invoke(new KeyTrigger(HeldMods(), button));
    }

    /// <summary>
    /// 修飾キーの状態は自前で数えず OS に聞く。
    /// Win+L などで離した通知を取りこぼしても押しっぱなし扱いにならないように。
    /// </summary>
    static Mods HeldMods()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        var mods = Mods.None;
        if (Down(0x11)) mods |= Mods.Ctrl;
        if (Down(0x10)) mods |= Mods.Shift;
        if (Down(0x12)) mods |= Mods.Alt;
        if (Down(0x5B) || Down(0x5C)) mods |= Mods.Win;
        return mods;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        try { Register(RIDEV_REMOVE, IntPtr.Zero); } catch (Win32Exception) { }
        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }
}

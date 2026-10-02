using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace MyTypingPet;

/// <summary>
/// USB 接続の DualSense / DualShock 4 を HID デバイスとして直接開き、入力レポートを読み続ける。
/// ウィンドウメッセージ (Raw Input) を経由しないので、管理者権限で動くゲームが前面にあっても届く。
/// 共有モードで開くので、ゲームや Steam が同時に読んでも入力を奪わない。
/// </summary>
public sealed class GamepadReader : IDisposable
{
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint RIM_TYPEHID = 2;
    const uint RIDI_DEVICENAME = 0x20000007;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("user32.dll")]
    static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? list, ref uint count, uint size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder? data, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("hid.dll")]
    static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

    [DllImport("hid.dll")]
    static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    readonly Dispatcher _dispatcher;
    readonly CancellationTokenSource _cts = new();

    /// <summary>ボタンが押された。UI スレッドで呼ばれる。</summary>
    public event Action<KeyTrigger>? PadPressed;

    public GamepadReader(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>つながっていれば読み続け、抜かれたら次に挿されるまで待つ。</summary>
    async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (FindDevice() is var (path, model))
                    await ReadDeviceAsync(path, model, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 抜かれた、またはゲームが独占して開いている。しばらくして試し直す
            }
            catch (Exception ex)
            {
                App.Log("ゲームパッドの読み取りで予期しないエラー", ex);
            }

            try { await Task.Delay(RetryInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task ReadDeviceAsync(string path, Gamepad.Model model, CancellationToken ct)
    {
        using var handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"{model.Name} を開けませんでした (エラー {Marshal.GetLastWin32Error()})");

        int length = InputReportLength(handle);
        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 0, isAsync: true);
        var report = new byte[length];
        uint before = 0;

        while (true)
        {
            int read = await stream.ReadAsync(report.AsMemory(0, length), ct);
            if (read == 0)
                return;
            if (!Gamepad.TryReadButtons(model, report.AsSpan(0, read), out uint now))
                continue;

            // 押しっぱなしは無視し、新しく押されたボタンだけ通知する
            uint pressed = now & ~before;
            before = now;
            if (pressed != 0)
                _ = _dispatcher.BeginInvoke(() => Raise(pressed)); // UI スレッドに渡すだけで待たない
        }
    }

    void Raise(uint pressed)
    {
        for (int bit = 0; pressed != 0; bit++, pressed >>= 1)
        {
            if ((pressed & 1) != 0)
                PadPressed?.Invoke(KeyTrigger.Pad(bit));
        }
    }

    /// <summary>1 回の読み取りで渡すバッファの大きさ (USB の DualSense / DualShock 4 は 64 バイト)。</summary>
    static int InputReportLength(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsed))
            return 64;
        try
        {
            return HidP_GetCaps(preparsed, out var caps) == HIDP_STATUS_SUCCESS && caps.InputReportByteLength > 0
                ? caps.InputReportByteLength
                : 64;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>
    /// USB 接続の対応パッドを探し、最初に見つかったもののデバイスパスと機種を返す。
    /// USB は "\\?\HID#VID_054C&amp;PID_0CE6..." の形。Bluetooth は "...VID&amp;0002054C_PID&amp;0CE6..." なので当たらない。
    /// </summary>
    static (string Path, Gamepad.Model Model)? FindDevice()
    {
        uint count = 0;
        uint itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, itemSize);
        var list = new RAWINPUTDEVICELIST[count];
        if (GetRawInputDeviceList(list, ref count, itemSize) == unchecked((uint)-1))
            return null;

        foreach (var device in list)
        {
            if (device.Type != RIM_TYPEHID)
                continue;
            uint size = 0;
            GetRawInputDeviceInfo(device.Device, RIDI_DEVICENAME, null, ref size);
            var name = new StringBuilder((int)size);
            if (GetRawInputDeviceInfo(device.Device, RIDI_DEVICENAME, name, ref size) == unchecked((uint)-1))
                continue;
            foreach (var model in Gamepad.Models)
            {
                string id = $"VID_{Gamepad.VendorId:X4}&PID_{model.ProductId:X4}";
                if (name.ToString().Contains(id, StringComparison.OrdinalIgnoreCase))
                    return (name.ToString(), model);
            }
        }
        return null;
    }

    public void Dispose()
    {
        // 読み取り中の ReadAsync も取り消される。読み取り側がまだトークンを使うので CTS 自体は破棄しない
        _cts.Cancel();
    }
}

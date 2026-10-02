using System;
using System.Windows;
using System.Windows.Input;

namespace MyTypingPet;

/// <summary>パターンのキー・クリック・パッドのボタンを実際に押して登録するダイアログ。</summary>
public partial class TriggerDialog : Window
{
    readonly Func<KeyTrigger, string?> _validate;
    Mods _held;
    Mods _peak;         // 修飾キーを押し始めてから全部離すまでに押されていたもの全部
    bool _otherPressed; // その間に普通のキーやクリックがあったか

    public KeyTrigger? Result { get; private set; }

    /// <param name="validate">登録できないときはその理由を返す。</param>
    /// <param name="input">パッドのボタンを受け取るため。ダイアログが非アクティブでも届く。</param>
    public TriggerDialog(Func<KeyTrigger, string?> validate, GamepadReader? input)
    {
        InitializeComponent();
        _validate = validate;
        Loaded += (_, _) => Activate(); // トレイメニューから開くと前に出てこないことがある
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        if (input != null)
        {
            input.PadPressed += OnPadPressed;
            Closed += (_, _) => input.PadPressed -= OnPadPressed;
        }
    }

    void OnPadPressed(KeyTrigger trigger)
    {
        if (IsLoaded && DialogResult == null)
            Submit(trigger);
    }

    static ushort VirtualKey(KeyEventArgs e)
    {
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,         // Alt 絡み
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        return KeyTrigger.Normalize((ushort)KeyInterop.VirtualKeyFromKey(key));
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; // Tab でフォーカスが動いたり Alt でメニューが開いたりしないように
        if (e.IsRepeat)
            return;

        ushort vk = VirtualKey(e);
        if (vk == 0)
            return;

        Mods mod = KeyTrigger.ModOf(vk);
        if (mod != Mods.None)
        {
            if (_held == Mods.None)
            {
                // 押し始め
                _peak = Mods.None;
                _otherPressed = false;
            }
            _held |= mod;
            _peak |= mod;
            Preview.Text = AnyKeyCheck.IsChecked == true
                ? new KeyTrigger(_peak, KeyTrigger.AnyKey).ToString()
                : KeyTrigger.ModsText(_held) + " + …";
            return;
        }

        _otherPressed = true;
        if (AnyKeyCheck.IsChecked != true)
            Submit(new KeyTrigger(_held, vk));
        else if (_held != Mods.None)
            Submit(new KeyTrigger(_held, KeyTrigger.AnyKey));
        else
            ShowError("「任意のキー」は修飾キー (Ctrl / Shift / Alt / Win) と一緒に押してください。");
    }

    void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        ushort vk = VirtualKey(e);
        Mods mod = KeyTrigger.ModOf(vk);
        if (mod == Mods.None)
            return;

        bool releasedAll = (_held & ~mod) == Mods.None;
        _held &= ~mod;
        if (!releasedAll || _otherPressed || !IsLoaded)
            return;

        // 修飾キーだけを押して全部離した
        if (AnyKeyCheck.IsChecked == true)
            Submit(new KeyTrigger(_peak, KeyTrigger.AnyKey)); // 例: Shift + Win を押して離す → Shift + Win + 任意のキー
        else if (_peak == mod)
            Submit(new KeyTrigger(Mods.None, vk));           // 単独で押して離した
        else
            ShowError($"修飾キー単独のパターンは 1 つずつ登録してください ({KeyTrigger.ModsText(_peak)} は同時に押されていました)。");
    }

    void ClickArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _otherPressed = true; // Ctrl + クリックの後に Ctrl 単独として登録されないように

        ushort button = e.ChangedButton switch
        {
            MouseButton.Left => KeyTrigger.VK_LBUTTON,
            MouseButton.Right => KeyTrigger.VK_RBUTTON,
            MouseButton.Middle => KeyTrigger.VK_MBUTTON,
            MouseButton.XButton1 => KeyTrigger.VK_XBUTTON1,
            MouseButton.XButton2 => KeyTrigger.VK_XBUTTON2,
            _ => 0,
        };
        if (button != 0)
            Submit(new KeyTrigger(CurrentMods(), button));
    }

    /// <summary>ダイアログが前に出る前から押されていた修飾キーも拾えるよう、今の状態を直接見る。</summary>
    static Mods CurrentMods()
    {
        var mods = Mods.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Mods.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Mods.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Mods.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Mods.Win;
        return mods;
    }

    void Submit(KeyTrigger trigger)
    {
        Preview.Text = trigger.ToString();
        if (_validate(trigger) is string error)
        {
            ShowError(error);
            return;
        }
        Result = trigger;
        DialogResult = true;
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

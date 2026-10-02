using System.Windows;
using System.Windows.Controls;

namespace MyTypingPet;

/// <summary>プリセット名を入力するダイアログ。</summary>
public partial class NameDialog : Window
{
    public NameDialog(string prompt, string initial)
    {
        InitializeComponent();
        Prompt.Text = $"{prompt} ({Preset.MaxNameLength} 文字まで)";
        NameBox.MaxLength = Preset.MaxNameLength;
        NameBox.Text = initial;
        NameBox.SelectAll();
        Loaded += (_, _) => Activate(); // トレイメニューから開くと前に出てこないことがある
    }

    public string Result => NameBox.Text.Trim();

    void NameBox_TextChanged(object sender, TextChangedEventArgs e) => OkButton.IsEnabled = Result.Length > 0;

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

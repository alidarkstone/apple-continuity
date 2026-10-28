using System.Windows;

namespace IPhoneMirror;

/// <summary>
/// Transparent window on top of the embedded video. It receives the mouse (a foreign
/// process' child window cannot be layered under WPF content) and shows hints.
/// </summary>
public partial class InputOverlay : Window
{
    public InputOverlay() => InitializeComponent();

    /// <param name="banner">Float at the bottom over the video instead of covering it.</param>
    public void ShowHint(string? text, bool banner = false)
    {
        HintText.Text = text ?? "";
        HintPanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        HintPanel.VerticalAlignment = banner ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
    }
}

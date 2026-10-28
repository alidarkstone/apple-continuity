using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AirPodsWin.Core;

namespace AirPodsWin.UI;

public enum ConnectionState { Disconnected, Connecting, Connected, Failed }

/// <summary>
/// The iOS-style AirPods card: springs up from the bottom of the screen,
/// shows the batteries and offers a one-tap connect.
/// </summary>
public partial class PopupWindow : Window
{
    static readonly TimeSpan IdleDismiss = TimeSpan.FromSeconds(12);
    static readonly TimeSpan ConnectedDismiss = TimeSpan.FromSeconds(3);

    readonly DispatcherTimer _dismissTimer = new();
    ConnectionState _state;
    bool _closing;

    /// <summary>Raised when the user taps "Bağlan". The handler returns whether it worked.</summary>
    public Func<Task<bool>>? ConnectRequested { get; set; }

    public PopupWindow()
    {
        InitializeComponent();
        _dismissTimer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => RestartDismissTimer();
        SourceInitialized += (_, _) => MakeNonActivating();
    }

    public bool IsShowing => IsVisible && !_closing;

    public void ShowCard(string title, ProximityPairingMessage message, ConnectionState state)
    {
        TitleText.Text = title;
        Update(message);
        SetState(state);

        if (IsShowing)
        {
            RestartDismissTimer();
            return;
        }

        PositionAtBottomCenter();
        Show();
        AnimateIn();
        RestartDismissTimer();
    }

    public void Update(ProximityPairingMessage message)
    {
        bool single = message.Model.IsSingleBattery();
        LeftBattery.Percent = single ? message.SingleBattery : message.LeftBattery;
        LeftBattery.Charging = message.LeftCharging;
        LeftBattery.Label = single ? "Pil" : "Sol";
        RightBattery.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        CaseBattery.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        RightBattery.Percent = message.RightBattery;
        RightBattery.Charging = message.RightCharging;
        CaseBattery.Percent = message.CaseBattery;
        CaseBattery.Charging = message.CaseCharging;

        // Pro models have short stems.
        double stem = message.Model.IsPro() ? 30 : 40;
        LeftStem.Height = stem;
        RightStem.Height = stem;
        CaseLed.Fill = new SolidColorBrush(message.CaseCharging || (message.CaseBattery ?? 100) > 20
            ? Color.FromRgb(0x30, 0xD1, 0x58)
            : Color.FromRgb(0xFF, 0x9F, 0x0A));
    }

    public void SetState(ConnectionState state)
    {
        _state = state;
        Spinner.Visibility = state == ConnectionState.Connecting ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsHitTestVisible = state is ConnectionState.Disconnected or ConnectionState.Failed;

        (string text, Color color) = state switch
        {
            ConnectionState.Connecting => ("Bağlanıyor…", Color.FromRgb(0x0A, 0x84, 0xFF)),
            ConnectionState.Connected => ("Bağlandı  ✓", Color.FromRgb(0x30, 0xD1, 0x58)),
            ConnectionState.Failed => ("Tekrar dene", Color.FromRgb(0xFF, 0x45, 0x3A)),
            _ => ("Bağlan", Color.FromRgb(0x0A, 0x84, 0xFF)),
        };
        ConnectText.Text = text;
        AnimateButtonColor(color);

        if (state == ConnectionState.Connecting)
        {
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        if (state == ConnectionState.Connected && IsShowing)
            RestartDismissTimer(ConnectedDismiss);
    }

    async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (ConnectRequested is null || _state == ConnectionState.Connecting)
            return;
        _dismissTimer.Stop();
        SetState(ConnectionState.Connecting);
        bool ok = await ConnectRequested();
        SetState(ok ? ConnectionState.Connected : ConnectionState.Failed);
        if (!ok)
            RestartDismissTimer();
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

    public void Dismiss()
    {
        if (!IsShowing)
            return;
        _closing = true;
        _dismissTimer.Stop();

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(320);
        var slide = new DoubleAnimation(Height + 40, duration) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            Hide();
            _closing = false;
        };
        CardOffset.BeginAnimation(TranslateTransform.YProperty, slide);
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
    }

    void AnimateIn()
    {
        // Overshooting spring like the iOS sheet.
        var spring = new ElasticEase { Oscillations = 1, Springiness = 7, EasingMode = EasingMode.EaseOut };
        var smooth = new CubicEase { EasingMode = EasingMode.EaseOut };

        CardOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(Height + 40, 0, TimeSpan.FromMilliseconds(700)) { EasingFunction = spring });
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = smooth });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = smooth });

        // Pods pop in slightly after the card, then float gently.
        var pop = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(650))
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };
        ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        ArtFloat.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    void AnimateButtonColor(Color color)
    {
        if (ConnectButton.Background is not SolidColorBrush brush || brush.IsFrozen)
        {
            brush = new SolidColorBrush(color);
            ConnectButton.Background = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(250)));
    }

    void RestartDismissTimer(TimeSpan? after = null)
    {
        _dismissTimer.Stop();
        if (_state == ConnectionState.Connecting)
            return;
        _dismissTimer.Interval = after ?? IdleDismiss;
        _dismissTimer.Start();
    }

    void PositionAtBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 8;
    }

    void MakeNonActivating()
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

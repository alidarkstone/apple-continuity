using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AirPodsWin.UI;

/// <summary>iOS-style circular battery gauge with the percentage underneath.</summary>
public sealed class BatteryIndicator : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(int?), typeof(BatteryIndicator),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(
        nameof(Charging), typeof(bool), typeof(BatteryIndicator),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(BatteryIndicator),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public int? Percent { get => (int?)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    static readonly Brush Green = Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));
    static readonly Brush Red = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
    static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7)));
    static readonly Brush Secondary = Frozen(new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9D)));
    static readonly Typeface Font = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    const double RingSize = 34;
    const double Stroke = 3.5;

    protected override Size MeasureOverride(Size availableSize) => new(64, 72);

    protected override void OnRender(DrawingContext dc)
    {
        double cx = RenderSize.Width / 2;
        double cy = RingSize / 2 + 2;
        double r = RingSize / 2 - Stroke / 2;
        var center = new Point(cx, cy);

        dc.DrawEllipse(null, new Pen(Track, Stroke), center, r, r);

        if (Percent is int p && p > 0)
        {
            var brush = p <= 20 && !Charging ? Red : Green;
            var pen = new Pen(brush, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (p >= 100)
            {
                dc.DrawEllipse(null, pen, center, r, r);
            }
            else
            {
                double angle = 360.0 * p / 100.0;
                var start = PointOnCircle(center, r, 0);
                var end = PointOnCircle(center, r, angle);
                var figure = new PathFigure { StartPoint = start, IsClosed = false };
                figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true));
                dc.DrawGeometry(null, pen, new PathGeometry(new[] { figure }));
            }
        }

        if (Charging)
            DrawBolt(dc, center);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var value = new FormattedText(Percent is int v ? $"{v}%" : "—", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, Font, 13, Text, dpi);
        dc.DrawText(value, new Point(cx - value.Width / 2, RingSize + 8));

        var label = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 11, Secondary, dpi);
        dc.DrawText(label, new Point(cx - label.Width / 2, RingSize + 26));
    }

    static Point PointOnCircle(Point c, double r, double degrees)
    {
        double rad = (degrees - 90) * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    static void DrawBolt(DrawingContext dc, Point c)
    {
        var bolt = Geometry.Parse("M 1,-8 L -5,1 L -0.5,1 L -1.5,8 L 5,-1.5 L 0.5,-1.5 Z");
        dc.PushTransform(new TranslateTransform(c.X, c.Y));
        dc.DrawGeometry(Green, null, bolt);
        dc.Pop();
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

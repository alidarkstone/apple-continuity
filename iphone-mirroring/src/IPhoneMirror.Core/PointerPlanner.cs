namespace IPhoneMirror.Core;

public sealed class PointerSettings
{
    /// <summary>HID counts that move the iPhone pointer across the full screen width / height (found by calibration).</summary>
    public int CountsX { get; set; } = 500;
    public int CountsY { get; set; } = 1080;
    /// <summary>Counts per report. A fixed step keeps iOS pointer acceleration constant, which makes moves predictable.</summary>
    public int Step { get; set; } = 8;
    /// <summary>Re-align the pointer in the top-left corner after this many taps to cancel drift.</summary>
    public int HomeEvery { get; set; } = 6;
    public int HomeReports { get; set; } = 40;
}

/// <summary>
/// iOS only accepts relative mouse movement, so the planner keeps an estimate of where the
/// pointer is and turns "tap at (x, y)" into controller commands. The pointer is periodically
/// pushed into the top-left corner, which is the only position we can know for certain.
/// </summary>
public sealed class PointerPlanner
{
    public const int CalibrationCounts = 240;

    readonly PointerSettings _s;
    double _x, _y;
    bool _homed;
    int _sinceHome;

    public PointerPlanner(PointerSettings settings) => _s = settings;

    public (double X, double Y)? Estimate =>
        _homed ? (_x / _s.CountsX, _y / _s.CountsY) : null;

    public void Invalidate() => _homed = false;

    public List<string> Home()
    {
        _x = _y = 0;
        _homed = true;
        _sinceHome = 0;
        return [Protocol.Repeat(-127, -127, _s.HomeReports)];
    }

    public List<string> MoveTo(double nx, double ny)
    {
        var cmds = new List<string>();
        if (!_homed || _sinceHome >= _s.HomeEvery)
            cmds.AddRange(Home());

        int tx = (int)Math.Round(Math.Clamp(nx, 0, 1) * _s.CountsX);
        int ty = (int)Math.Round(Math.Clamp(ny, 0, 1) * _s.CountsY);
        AddAxis(cmds, tx - (int)Math.Round(_x), horizontal: true);
        AddAxis(cmds, ty - (int)Math.Round(_y), horizontal: false);
        _x = tx;
        _y = ty;
        _sinceHome++;
        return cmds;
    }

    /// <summary>Free movement while dragging. Speed varies, so the estimate is no longer trusted afterwards.</summary>
    public List<string> MoveBy(double dnx, double dny)
    {
        int dx = (int)Math.Round(dnx * _s.CountsX), dy = (int)Math.Round(dny * _s.CountsY);
        if (dx == 0 && dy == 0) return [];
        _x += dx;
        _y += dy;
        _sinceHome = _s.HomeEvery;
        return [Protocol.Move(dx, dy)];
    }

    public List<string> StartCalibration()
    {
        var cmds = Home();
        AddAxis(cmds, CalibrationCounts, horizontal: true);
        AddAxis(cmds, CalibrationCounts, horizontal: false);
        return cmds;
    }

    /// <summary>The user clicked where the pointer ended up after <see cref="StartCalibration"/>.</summary>
    public bool FinishCalibration(double seenX, double seenY)
    {
        if (seenX < 0.03 || seenY < 0.03) return false;
        _s.CountsX = (int)Math.Round(CalibrationCounts / seenX);
        _s.CountsY = (int)Math.Round(CalibrationCounts / seenY);
        _x = CalibrationCounts;
        _y = CalibrationCounts;
        _homed = true;
        _sinceHome = 0;
        return true;
    }

    void AddAxis(List<string> cmds, int delta, bool horizontal)
    {
        if (delta == 0) return;
        int sign = Math.Sign(delta), step = Math.Clamp(_s.Step, 1, 127);
        int full = Math.Abs(delta) / step, rest = Math.Abs(delta) % step;
        if (full > 0)
            cmds.Add(horizontal ? Protocol.Repeat(sign * step, 0, full) : Protocol.Repeat(0, sign * step, full));
        if (rest > 0)
            cmds.Add(horizontal ? Protocol.Repeat(sign * rest, 0, 1) : Protocol.Repeat(0, sign * rest, 1));
    }
}

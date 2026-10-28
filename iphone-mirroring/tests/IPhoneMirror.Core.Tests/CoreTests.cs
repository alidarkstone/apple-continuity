using IPhoneMirror.Core;
using Xunit;

namespace IPhoneMirror.Core.Tests;

public class VideoGeometryTests
{
    [Fact]
    public void PortraitVideoIsPillarboxedInWideBox()
    {
        var box = VideoGeometry.Fit(1000, 2000, 2000, 1000);
        Assert.Equal(new Box(750, 0, 500, 1000), box);
    }

    [Fact]
    public void ClicksOnBlackBarsAreIgnored()
    {
        var box = new Box(750, 0, 500, 1000);
        Assert.Null(VideoGeometry.ToScreen(box, 100, 500));
        Assert.Equal((0.5, 0.25), VideoGeometry.ToScreen(box, 1000, 250)!.Value);
    }
}

public class PointerPlannerTests
{
    static (int X, int Y) Sum(IEnumerable<string> cmds)
    {
        int x = 0, y = 0;
        foreach (var c in cmds)
        {
            var p = c.Split(' ');
            int n = p[0] == "r" ? int.Parse(p[3]) : 1;
            x += int.Parse(p[1]) * n;
            y += int.Parse(p[2]) * n;
        }
        return (x, y);
    }

    [Fact]
    public void FirstTapHomesThenMovesWithFixedSteps()
    {
        var s = new PointerSettings { CountsX = 400, CountsY = 800, Step = 8, HomeReports = 40 };
        var cmds = new PointerPlanner(s).MoveTo(0.5, 0.25);

        Assert.Equal("r -127 -127 40", cmds[0]);
        Assert.Equal(new[] { "r 8 0 25", "r 0 8 25" }, cmds.Skip(1));
    }

    [Fact]
    public void LaterTapsMoveRelativelyUntilRehome()
    {
        var s = new PointerSettings { CountsX = 400, CountsY = 800, HomeEvery = 2 };
        var p = new PointerPlanner(s);
        p.MoveTo(0.5, 0.5);
        var second = p.MoveTo(0.25, 0.5);
        Assert.Equal((-100, 0), Sum(second));
        var third = p.MoveTo(0.25, 0.5);
        Assert.StartsWith("r -127 -127", third[0]);
    }

    [Fact]
    public void DragForcesRehomeOnNextTap()
    {
        var p = new PointerPlanner(new PointerSettings { HomeEvery = 10 });
        p.MoveTo(0.5, 0.5);
        Assert.Single(p.MoveBy(0.1, 0));
        Assert.StartsWith("r -127 -127", p.MoveTo(0.5, 0.5)[0]);
    }

    [Fact]
    public void CalibrationDerivesCountsPerScreen()
    {
        var s = new PointerSettings();
        var p = new PointerPlanner(s);
        Assert.Equal((240, 240), Sum(p.StartCalibration().Skip(1)));
        Assert.True(p.FinishCalibration(0.4, 0.2));
        Assert.Equal(600, s.CountsX);
        Assert.Equal(1200, s.CountsY);
        Assert.False(p.FinishCalibration(0.0, 0.5));
    }
}

public class HidKeyboardTests
{
    [Fact]
    public void CtrlCBecomesCommandC()
    {
        var k = new HidKeyboard();
        Assert.Equal("k 8", k.KeyDown(0x1D, false));
        Assert.Equal("k 8 6", k.KeyDown(0x2E, false));
        Assert.Null(k.KeyDown(0x2E, false)); // auto-repeat
        Assert.Equal("k 8", k.KeyUp(0x2E, false));
        Assert.Equal("k 0", k.KeyUp(0x1D, false));
    }

    [Fact]
    public void AltGrIsOptionOnly()
    {
        var k = new HidKeyboard();
        k.KeyDown(0x1D, false);            // fake left Ctrl sent by Windows for AltGr
        Assert.Equal("k 64", k.KeyDown(0x38, true));
        Assert.Equal("k 64 20", k.KeyDown(0x10, false)); // AltGr+Q = @ on Turkish Q
    }

    [Fact]
    public void ArrowKeysUseExtendedTable()
    {
        Assert.Equal((byte)0x52, HidKeyboard.Translate(0x48, true, true).Usage);
        Assert.Equal((byte)0x60, HidKeyboard.Translate(0x48, false, true).Usage);
    }
}

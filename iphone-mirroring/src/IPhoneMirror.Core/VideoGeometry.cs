namespace IPhoneMirror.Core;

public readonly record struct Box(double X, double Y, double Width, double Height);

public static class VideoGeometry
{
    /// <summary>Letterboxed rectangle that a video of the given size occupies inside a box (same as the video sink draws it).</summary>
    public static Box Fit(double videoWidth, double videoHeight, double boxWidth, double boxHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
            return new Box(0, 0, Math.Max(boxWidth, 0), Math.Max(boxHeight, 0));

        double scale = Math.Min(boxWidth / videoWidth, boxHeight / videoHeight);
        double w = videoWidth * scale, h = videoHeight * scale;
        return new Box((boxWidth - w) / 2, (boxHeight - h) / 2, w, h);
    }

    /// <summary>Point inside the box mapped to 0..1 coordinates of the phone screen, or null when it falls on the black bars.</summary>
    public static (double X, double Y)? ToScreen(Box video, double x, double y)
    {
        if (video.Width <= 0 || video.Height <= 0) return null;
        double nx = (x - video.X) / video.Width, ny = (y - video.Y) / video.Height;
        if (nx < 0 || nx > 1 || ny < 0 || ny > 1) return null;
        return (nx, ny);
    }
}

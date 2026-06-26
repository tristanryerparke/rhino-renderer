using System.Drawing;

namespace GeometryRendererPlugin.Display;

public enum MeshDisplayMode
{
    Shaded,
    Wireframe,
}

public sealed class DisplayObjectStyle
{
    public static DisplayObjectStyle Default => new();

    public Color Color { get; set; } = Color.FromArgb(255, 255, 0, 0);
    public int LineWidth { get; set; } = 1;
    public MeshDisplayMode MeshDisplay { get; set; } = MeshDisplayMode.Shaded;
    public bool ShowEdges { get; set; } = true;
    public double SharpEdgeAngleDegrees { get; set; } = 30.0;
    public bool IncludeNakedEdges { get; set; } = true;
    public bool UseVertexColors { get; set; }

    public Color EdgeColor
    {
        get
        {
            var brightness = (Color.R + Color.G + Color.B) / 3.0;
            var multiplier = brightness < 128.0 ? 1.25 : 0.75;
            return Color.FromArgb(
                Color.A,
                ClampToByte(Color.R * multiplier),
                ClampToByte(Color.G * multiplier),
                ClampToByte(Color.B * multiplier));
        }
    }

    public double Transparency => 1.0 - Color.A / 255.0;

    public DisplayObjectStyle Clone()
    {
        return new DisplayObjectStyle
        {
            Color = Color,
            LineWidth = LineWidth,
            MeshDisplay = MeshDisplay,
            ShowEdges = ShowEdges,
            SharpEdgeAngleDegrees = SharpEdgeAngleDegrees,
            IncludeNakedEdges = IncludeNakedEdges,
            UseVertexColors = UseVertexColors,
        };
    }

    private static int ClampToByte(double value)
    {
        if (value < 0.0)
        {
            return 0;
        }

        if (value > 255.0)
        {
            return 255;
        }

        return (int)Math.Round(value);
    }
}

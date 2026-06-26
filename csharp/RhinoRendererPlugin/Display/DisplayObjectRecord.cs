using Rhino.Geometry;

namespace RhinoRendererPlugin.Display;

public sealed class DisplayObjectRecord
{
    public string GroupId { get; set; } = "default";
    public string ObjectId { get; set; } = string.Empty;
    public object Geometry { get; set; } = Point3d.Unset;
    public DisplayObjectStyle Style { get; set; } = DisplayObjectStyle.Default;
    public bool Visible { get; set; } = true;

    public string CacheKey => GroupId + ":" + ObjectId;

    public BoundingBox BoundingBox
    {
        get
        {
            return Geometry switch
            {
                Point3d point when point.IsValid => new BoundingBox(point, point),
                Point point when point.IsValid => point.GetBoundingBox(true),
                GeometryBase geometryBase => geometryBase.GetBoundingBox(true),
                _ => BoundingBox.Empty,
            };
        }
    }
}

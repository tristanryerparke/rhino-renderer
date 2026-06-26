using Rhino.Display;
using Rhino.Geometry;

namespace GeometryRendererPlugin.Display;

public sealed class GeometryDisplayConduit : DisplayConduit
{
    private const double BoundingBoxScale = 1.25;
    private const double BoundingBoxMinPadding = 2.0;
    private readonly DisplayRegistry _registry;
    private readonly Dictionary<string, Mesh[]> _brepMeshCache = new(StringComparer.Ordinal);

    public GeometryDisplayConduit(DisplayRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        foreach (var record in _registry.Enumerate())
        {
            if (!record.Visible || record.Geometry == null)
            {
                continue;
            }

            DrawGeometry(e, record.Geometry, record.Style ?? DisplayObjectStyle.Default, record.CacheKey);
        }
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        foreach (var record in _registry.Enumerate())
        {
            if (!record.Visible || record.Geometry == null)
            {
                continue;
            }

            var box = InflateBoundingBox(record.BoundingBox);
            if (box.IsValid)
            {
                e.IncludeBoundingBox(box);
            }
        }
    }

    protected override void CalculateBoundingBoxZoomExtents(CalculateBoundingBoxEventArgs e)
    {
        CalculateBoundingBox(e);
    }

    public void ClearCaches()
    {
        _brepMeshCache.Clear();
    }

    private void DrawGeometry(DrawEventArgs e, object geometry, DisplayObjectStyle style, string cacheKey)
    {
        switch (geometry)
        {
            case Point3d point:
                e.Display.DrawPoint(point, PointStyle.RoundSimple, Math.Max(2, style.LineWidth + 2), style.Color);
                return;
            case Rhino.Geometry.Point pointGeometry:
                e.Display.DrawPoint(pointGeometry.Location, PointStyle.RoundSimple, Math.Max(2, style.LineWidth + 2), style.Color);
                return;
            case Curve curve:
                e.Display.DrawCurve(curve, style.Color, Math.Max(1, style.LineWidth));
                return;
            case Mesh mesh:
                DrawMesh(e, mesh, style);
                return;
            case Brep brep:
                foreach (var brepMesh in GetBrepMeshes(cacheKey, brep))
                {
                    DrawMesh(e, brepMesh, style);
                }
                return;
        }
    }

    private void DrawMesh(DrawEventArgs e, Mesh mesh, DisplayObjectStyle style)
    {
        if (style.MeshDisplay == MeshDisplayMode.Wireframe)
        {
            if (style.ShowEdges)
            {
                e.Display.DrawMeshWires(mesh, style.EdgeColor, Math.Max(1, style.LineWidth));
            }

            return;
        }

        var material = new DisplayMaterial
        {
            Diffuse = style.Color,
            Transparency = style.Transparency,
        };
        e.Display.DrawMeshShaded(mesh, material);

        if (style.ShowEdges)
        {
            e.Display.DrawMeshWires(mesh, style.EdgeColor, Math.Max(1, style.LineWidth));
        }
    }

    private Mesh[] GetBrepMeshes(string cacheKey, Brep brep)
    {
        var fullCacheKey = cacheKey + ":brep-mesh";
        if (_brepMeshCache.TryGetValue(fullCacheKey, out var cached))
        {
            return cached;
        }

        var meshes = Mesh.CreateFromBrep(brep, MeshingParameters.Default) ?? Array.Empty<Mesh>();
        foreach (var mesh in meshes)
        {
            if (mesh.Normals.Count == 0)
            {
                mesh.Normals.ComputeNormals();
            }

            mesh.Compact();
        }

        _brepMeshCache[fullCacheKey] = meshes;
        return meshes;
    }

    private static BoundingBox InflateBoundingBox(BoundingBox boundingBox)
    {
        if (!boundingBox.IsValid)
        {
            return boundingBox;
        }

        var diagonal = boundingBox.Diagonal;
        var scalePadding = Math.Max((BoundingBoxScale - 1.0) * 0.5, 0.0);
        boundingBox.Inflate(
            Math.Max(diagonal.X * scalePadding, BoundingBoxMinPadding),
            Math.Max(diagonal.Y * scalePadding, BoundingBoxMinPadding),
            Math.Max(diagonal.Z * scalePadding, BoundingBoxMinPadding));
        return boundingBox;
    }
}

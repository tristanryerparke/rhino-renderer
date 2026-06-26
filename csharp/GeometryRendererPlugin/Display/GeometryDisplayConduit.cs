using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace GeometryRendererPlugin.Display;

public sealed class GeometryDisplayConduit : DisplayConduit
{
    private const double BoundingBoxScale = 1.25;
    private const double BoundingBoxMinPadding = 2.0;
    private readonly DisplayRegistry _registry;
    private readonly Dictionary<string, Mesh[]> _brepMeshCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mesh> _shadedMeshCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Line>> _sharpEdgeCache = new(StringComparer.Ordinal);

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
        _shadedMeshCache.Clear();
        _sharpEdgeCache.Clear();
    }

    private void DrawGeometry(DrawEventArgs e, object geometry, DisplayObjectStyle style, string cacheKey)
    {
        switch (geometry)
        {
            case Point3d point:
                e.Display.DrawPoint(point, PointStyle.RoundSimple, Math.Max(1, style.PointSize), style.Color);
                return;
            case Rhino.Geometry.Point pointGeometry:
                e.Display.DrawPoint(pointGeometry.Location, PointStyle.RoundSimple, Math.Max(1, style.PointSize), style.Color);
                return;
            case Curve curve:
                e.Display.DrawCurve(curve, style.Color, Math.Max(1, style.LineWidth));
                return;
            case Mesh mesh:
                DrawMesh(e, mesh, style, cacheKey);
                return;
            case Brep brep:
                var index = 0;
                foreach (var brepMesh in GetBrepMeshes(cacheKey, brep))
                {
                    DrawMesh(e, brepMesh, style, cacheKey + ":brep-part:" + index);
                    index += 1;
                }
                return;
        }
    }

    private void DrawMesh(DrawEventArgs e, Mesh mesh, DisplayObjectStyle style, string cacheKey)
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
        var shadedMesh = GetCachedShadedMesh(cacheKey, mesh, style.SharpEdgeAngleDegrees);
        e.Display.DrawMeshShaded(shadedMesh, material);

        if (style.ShowEdges)
        {
            var lines = GetCachedSharpEdges(cacheKey, mesh, style.SharpEdgeAngleDegrees, style.IncludeNakedEdges);
            if (lines.Count > 0)
            {
                e.Display.DrawLines(lines, style.EdgeColor, Math.Max(1, style.LineWidth));
            }
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

    private Mesh GetCachedShadedMesh(string cacheKey, Mesh mesh, double sharpEdgeAngleDegrees)
    {
        var fullCacheKey = cacheKey + ":shaded:" + sharpEdgeAngleDegrees;
        if (_shadedMeshCache.TryGetValue(fullCacheKey, out var cached))
        {
            return cached;
        }

        var shadedMesh = mesh.DuplicateMesh();
        try
        {
            shadedMesh.Unweld(RhinoMath.ToRadians(sharpEdgeAngleDegrees), true);
            shadedMesh.RebuildNormals();
            shadedMesh.Compact();
        }
        catch
        {
            shadedMesh = mesh;
        }

        _shadedMeshCache[fullCacheKey] = shadedMesh;
        return shadedMesh;
    }

    private List<Line> GetCachedSharpEdges(string cacheKey, Mesh mesh, double sharpEdgeAngleDegrees, bool includeNakedEdges)
    {
        var fullCacheKey = cacheKey + ":edges:" + sharpEdgeAngleDegrees + ":" + includeNakedEdges;
        if (_sharpEdgeCache.TryGetValue(fullCacheKey, out var cached))
        {
            return cached;
        }

        if (mesh.FaceNormals.Count != mesh.Faces.Count)
        {
            mesh.FaceNormals.ComputeFaceNormals();
        }

        var lines = new List<Line>();
        var edges = mesh.TopologyEdges;
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex += 1)
        {
            var connectedFaces = edges.GetConnectedFaces(edgeIndex);
            if (connectedFaces == null || connectedFaces.Length == 0)
            {
                continue;
            }

            var shouldDraw = false;
            if (connectedFaces.Length <= 1)
            {
                shouldDraw = includeNakedEdges;
            }
            else
            {
                var normalA = mesh.FaceNormals[connectedFaces[0]];
                var normalB = mesh.FaceNormals[connectedFaces[1]];
                double dot = (normalA.X * normalB.X) + (normalA.Y * normalB.Y) + (normalA.Z * normalB.Z);
                dot = Math.Max(-1.0, Math.Min(1.0, dot));
                var angleDegrees = RhinoMath.ToDegrees(Math.Acos(dot));
                shouldDraw = angleDegrees >= sharpEdgeAngleDegrees;
            }

            if (shouldDraw)
            {
                lines.Add(edges.EdgeLine(edgeIndex));
            }
        }

        _sharpEdgeCache[fullCacheKey] = lines;
        return lines;
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

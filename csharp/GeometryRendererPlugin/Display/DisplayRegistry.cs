using Rhino;
using Rhino.Geometry;

namespace GeometryRendererPlugin.Display;

public sealed class DisplayRegistry
{
    private readonly RhinoDoc _document;
    private readonly Dictionary<string, Dictionary<string, DisplayObjectRecord>> _groups =
        new(StringComparer.Ordinal);
    private readonly GeometryDisplayConduit _conduit;

    public DisplayRegistry(RhinoDoc document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _conduit = new GeometryDisplayConduit(this);
    }

    public RhinoDoc Document => _document;
    public GeometryDisplayConduit Conduit => _conduit;

    public event EventHandler? Changed;

    public DisplayRegistrySnapshot Snapshot()
    {
        var all = Enumerate().ToList();
        return new DisplayRegistrySnapshot
        {
            TotalCount = all.Count,
            VisibleCount = all.Count(record => record.Visible),
            GroupCount = _groups.Count,
        };
    }

    public IEnumerable<DisplayObjectRecord> Enumerate()
    {
        foreach (var group in _groups.Values)
        {
            foreach (var record in group.Values)
            {
                yield return record;
            }
        }
    }

    public DisplayObjectRecord Upsert(string objectId, object geometry, DisplayObjectStyle? style = null, string? groupId = null, bool visible = true)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            objectId = Guid.NewGuid().ToString("N");
        }

        var normalizedObjectId = objectId.Trim();
        var normalizedGroupId = NormalizeGroupId(groupId);
        if (!_groups.TryGetValue(normalizedGroupId, out var group))
        {
            group = new Dictionary<string, DisplayObjectRecord>(StringComparer.Ordinal);
            _groups[normalizedGroupId] = group;
        }

        var record = new DisplayObjectRecord
        {
            GroupId = normalizedGroupId,
            ObjectId = normalizedObjectId,
            Geometry = DuplicateGeometry(geometry),
            Style = style?.Clone() ?? DisplayObjectStyle.Default,
            Visible = visible,
        };
        group[normalizedObjectId] = record;
        NotifyChanged();
        return record;
    }

    public int SetAllVisible(bool visible, string? groupId = null)
    {
        var count = 0;
        foreach (var record in RecordsForGroup(groupId))
        {
            record.Visible = visible;
            count += 1;
        }

        NotifyChanged();
        return count;
    }

    public bool SetVisible(string objectId, bool visible, string? groupId = null)
    {
        var record = FindRecord(objectId, groupId);
        if (record == null)
        {
            return false;
        }

        record.Visible = visible;
        NotifyChanged();
        return true;
    }

    public int DeleteAll(string? groupId = null)
    {
        int count;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            count = Enumerate().Count();
            _groups.Clear();
        }
        else
        {
            var normalizedGroupId = NormalizeGroupId(groupId);
            count = _groups.TryGetValue(normalizedGroupId, out var group) ? group.Count : 0;
            _groups.Remove(normalizedGroupId);
        }

        Conduit.ClearCaches();
        NotifyChanged();
        return count;
    }

    public bool Delete(string objectId, string? groupId = null)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        var normalizedObjectId = objectId.Trim();
        var removed = false;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            foreach (var group in _groups.Values)
            {
                removed = group.Remove(normalizedObjectId) || removed;
            }
        }
        else
        {
            var normalizedGroupId = NormalizeGroupId(groupId);
            if (_groups.TryGetValue(normalizedGroupId, out var group))
            {
                removed = group.Remove(normalizedObjectId);
            }
        }

        if (removed)
        {
            Conduit.ClearCaches();
            NotifyChanged();
        }

        return removed;
    }

    public bool HasVisibleGeometry()
    {
        return Enumerate().Any(record => record.Visible && record.Geometry != null);
    }

    private DisplayObjectRecord? FindRecord(string objectId, string? groupId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        var normalizedObjectId = objectId.Trim();
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            var normalizedGroupId = NormalizeGroupId(groupId);
            return _groups.TryGetValue(normalizedGroupId, out var group) && group.TryGetValue(normalizedObjectId, out var record)
                ? record
                : null;
        }

        foreach (var group in _groups.Values)
        {
            if (group.TryGetValue(normalizedObjectId, out var record))
            {
                return record;
            }
        }

        return null;
    }

    private IEnumerable<DisplayObjectRecord> RecordsForGroup(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return Enumerate().ToList();
        }

        var normalizedGroupId = NormalizeGroupId(groupId);
        return _groups.TryGetValue(normalizedGroupId, out var group)
            ? group.Values.ToList()
            : Array.Empty<DisplayObjectRecord>();
    }

    private static string NormalizeGroupId(string? groupId)
    {
        return string.IsNullOrWhiteSpace(groupId) ? "default" : groupId.Trim();
    }

    private static object DuplicateGeometry(object geometry)
    {
        return geometry switch
        {
            Point3d point => point,
            Point point => point.Duplicate(),
            Mesh mesh => PrepareMesh(mesh.DuplicateMesh()),
            Curve curve => curve.DuplicateCurve(),
            Brep brep => brep.DuplicateBrep(),
            GeometryBase geometryBase => geometryBase.Duplicate(),
            _ => throw new ArgumentException("Unsupported geometry type: " + geometry.GetType().FullName, nameof(geometry)),
        };
    }

    private static Mesh PrepareMesh(Mesh mesh)
    {
        if (mesh.Normals.Count == 0)
        {
            mesh.Normals.ComputeNormals();
        }

        mesh.Compact();
        return mesh;
    }

    private void NotifyChanged()
    {
        Conduit.Enabled = HasVisibleGeometry();
        _document.Views.Redraw();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

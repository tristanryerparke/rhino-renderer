# geometry-renderer

A Rhino 8 C# plugin that exposes a small localhost web API for rendering transient geometry in a custom display conduit.

External Python code can send Rhino geometry serialized by `rhino3dm.Encode()`, set geometry-specific display options, and later hide, show, delete, or clear that geometry without adding permanent objects to the Rhino document.

## Features

- Rhino 8 `.rhp` C# plugin
- custom `DisplayConduit` for transient geometry
- per-document display registry
- barebones Eto side panel with global show/hide/clear controls
- localhost JSON API
- health endpoint for process discovery
- Python sample client using `rhino3dm`

## Web API

The plugin listens on:

```text
http://127.0.0.1:17891
```

### Health

```bash
curl http://127.0.0.1:17891/health
```

Example response:

```json
{
  "ok": true,
  "plugin": "GeometryRendererPlugin",
  "base_url": "http://127.0.0.1:17891",
  "active_document_serial": 1234
}
```

### Send geometry

```http
POST /geometry
Content-Type: application/json
```

```json
{
  "object_id": "toolpath-1",
  "group_id": "job-1",
  "geometry_type": "mesh",
  "geometry": { "...": "rhino3dm Encode() payload" },
  "color": "#3b82f6",
  "mesh_settings": {
    "opacity": 0.65,
    "display": "shaded",
    "edge_width": 2,
    "show_edges": false,
    "sharp_edge_angle_degrees": 15,
    "include_naked_edges": true
  }
}
```

Supported geometry is whatever RhinoCommon can decode from the `rhino3dm` JSON payload via `Rhino.Runtime.CommonObject.FromJSON()`, including meshes, curves, breps, and points.

`color` is common to every geometry type. Other settings are geometry-specific:

- Mesh/Brep: `mesh_settings` with `opacity`, `display`, `edge_width`, `show_edges` (default `false`), `sharp_edge_angle_degrees`, `include_naked_edges`
- Curve: `curve_settings` with `line_width`
- Point: `point_settings` with `point_size`

### Visibility and cleanup

```http
POST /hide   { "object_id": "toolpath-1" }
POST /show   { "object_id": "toolpath-1" }
POST /delete { "object_id": "toolpath-1" }
POST /clear  { "group_id": "job-1" }
```

Omit `object_id` for `/hide` or `/show` to affect all objects, optionally scoped by `group_id`.

## Python example

```bash
uv sync
uv run python examples/send_geometry.py
```

Or use the client directly:

```python
import rhino3dm
from python.geometry_renderer_client import GeometryRendererClient

mesh = rhino3dm.Mesh()
# build mesh...

client = GeometryRendererClient()
print(client.health())
client.send_geometry(
    mesh,
    object_id="mesh-1",
    group_id="demo",
    geometry_type="mesh",
    color="#22c55e",
    mesh_settings={
        "opacity": 0.5,
        "display": "shaded",
    },
)
client.hide(object_id="mesh-1")
client.show(object_id="mesh-1")
client.clear(group_id="demo")
```

## Build

From the repo root:

```bash
dotnet build csharp/GeometryRendererPlugin/GeometryRendererPlugin.csproj
```

The plugin output is:

```text
csharp/GeometryRendererPlugin/bin/Debug/net8.0/GeometryRendererPlugin.rhp
```

Load that `.rhp` in Rhino, then run the command:

```text
GeometryRenderer
```

The panel is optional; the web API starts when the plugin loads.

## Notes

- The display conduit is transient; it does not write geometry into the Rhino document.
- All API mutations are marshalled onto Rhino's UI thread before touching `RhinoDoc` or display state.
- This repo intentionally avoids RhinoCode and Redis; communication is direct Python/program → C# plugin over localhost HTTP.

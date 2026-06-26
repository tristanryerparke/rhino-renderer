using System.Drawing;
using System.Net;
using System.Text;
using System.Text.Json;
using Rhino;
using Rhino.Geometry;
using GeometryRendererPlugin.Display;

namespace GeometryRendererPlugin.Services;

public sealed class GeometryRendererWebServer : IDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:17891/";

    private readonly GeometryRendererPlugin _plugin;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenTask;

    public GeometryRendererWebServer(GeometryRendererPlugin plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _listener.Prefixes.Add(DefaultBaseUrl);
    }

    public void Start()
    {
        if (_listener.IsListening)
        {
            return;
        }

        _listener.Start();
        _listenTask = Task.Run(() => ListenAsync(_cancellation.Token));
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
        _cancellation.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        AddCorsHeaders(context.Response);

        try
        {
            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            var path = (context.Request.Url?.AbsolutePath ?? "/").Trim('/').ToLowerInvariant();
            QueueLog($"HTTP {context.Request.HttpMethod} /{path} from {context.Request.RemoteEndPoint}");
            if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "health")
            {
                await WriteJsonAsync(context.Response, new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["plugin"] = "GeometryRendererPlugin",
                    ["base_url"] = DefaultBaseUrl.TrimEnd('/'),
                    ["active_document_serial"] = RhinoDoc.ActiveDoc?.RuntimeSerialNumber,
                }).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "objects")
            {
                var snapshot = await InvokeOnRhinoThreadAsync(() =>
                {
                    var registry = _plugin.GetActiveRegistry();
                    var registrySnapshot = registry?.Snapshot() ?? new DisplayRegistrySnapshot();
                    Log($"GET objects -> total={registrySnapshot.TotalCount} visible={registrySnapshot.VisibleCount} groups={registrySnapshot.GroupCount}");
                    return registrySnapshot;
                }).ConfigureAwait(false);
                await WriteJsonAsync(context.Response, snapshot).ConfigureAwait(false);
                return;
            }

            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteErrorAsync(context.Response, 405, "Unsupported method").ConfigureAwait(false);
                return;
            }

            var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
            QueueLog($"HTTP body bytes={Encoding.UTF8.GetByteCount(body)}");
            var result = await InvokeOnRhinoThreadAsync(() => HandlePost(path, body)).ConfigureAwait(false);
            await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            QueueLog($"ERROR {context.Request.HttpMethod} {context.Request.Url?.AbsolutePath}: {exception.GetType().Name}: {exception.Message}");
            await WriteErrorAsync(context.Response, 500, exception.Message).ConfigureAwait(false);
        }
    }

    private Dictionary<string, object?> HandlePost(string path, string body)
    {
        using var document = string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
        var root = document.RootElement;

        var action = PathToAction(path);
        if (action == "message")
        {
            action = GetString(root, "action") ?? GetString(root, "type") ?? string.Empty;
        }

        action = NormalizeAction(action);
        var registry = _plugin.GetActiveRegistry() ?? throw new InvalidOperationException("No active Rhino document");
        Log($"POST action={action} path=/{path} activeDoc={registry.Document.RuntimeSerialNumber}");

        switch (action)
        {
            case "upsert":
            case "geometry":
            {
                var objectId = GetString(root, "object_id") ?? GetString(root, "objectId") ?? Guid.NewGuid().ToString("N");
                var groupId = GetString(root, "group_id") ?? GetString(root, "groupId");
                var type = GetString(root, "geometry_type") ?? GetString(root, "geometryType") ?? GetString(root, "type");
                var visible = GetBool(root, "visible") ?? true;
                var geometryElement = TryGetProperty(root, "geometry") ?? root;
                var geometry = DecodeGeometry(geometryElement, type);
                var style = ParseSettingsForGeometry(root, geometry);
                Log(
                    $"Geometry upsert object_id={objectId} group_id={groupId ?? "default"} visible={visible} " +
                    $"type_hint={type ?? "(none)"} settings={SettingsKeyForGeometry(geometry)} {DescribeGeometry(geometry)} " +
                    $"style={DescribeStyle(style)}");
                var record = registry.Upsert(objectId, geometry, style, groupId, visible);
                var snapshot = registry.Snapshot();
                Log($"Registry after upsert -> total={snapshot.TotalCount} visible={snapshot.VisibleCount} groups={snapshot.GroupCount} conduit_enabled={registry.Conduit.Enabled}");
                _plugin.OpenPanel();
                return Success(new Dictionary<string, object?>
                {
                    ["action"] = "upsert",
                    ["object_id"] = record.ObjectId,
                    ["group_id"] = record.GroupId,
                    ["visible"] = record.Visible,
                });
            }
            case "hide":
            case "show":
            {
                var visible = action == "show";
                var objectId = GetString(root, "object_id") ?? GetString(root, "objectId");
                var groupId = GetString(root, "group_id") ?? GetString(root, "groupId");
                if (string.IsNullOrWhiteSpace(objectId))
                {
                    var count = registry.SetAllVisible(visible, groupId);
                    var allVisibleSnapshot = registry.Snapshot();
                    Log($"{action} all group_id={groupId ?? "(all)"} changed={count} -> total={allVisibleSnapshot.TotalCount} visible={allVisibleSnapshot.VisibleCount}");
                    return Success(new Dictionary<string, object?>
                    {
                        ["action"] = action,
                        ["count"] = count,
                        ["group_id"] = groupId,
                    });
                }

                var changed = registry.SetVisible(objectId, visible, groupId);
                var setVisibleSnapshot = registry.Snapshot();
                Log($"{action} object_id={objectId} group_id={groupId ?? "(any)"} changed={changed} -> total={setVisibleSnapshot.TotalCount} visible={setVisibleSnapshot.VisibleCount}");
                return Success(new Dictionary<string, object?>
                {
                    ["action"] = action,
                    ["object_id"] = objectId,
                    ["group_id"] = groupId,
                    ["changed"] = changed,
                });
            }
            case "delete":
            {
                var objectId = GetString(root, "object_id") ?? GetString(root, "objectId");
                var groupId = GetString(root, "group_id") ?? GetString(root, "groupId");
                if (string.IsNullOrWhiteSpace(objectId))
                {
                    var count = registry.DeleteAll(groupId);
                    var deleteAllSnapshot = registry.Snapshot();
                    Log($"delete all group_id={groupId ?? "(all)"} deleted={count} -> total={deleteAllSnapshot.TotalCount} visible={deleteAllSnapshot.VisibleCount}");
                    return Success(new Dictionary<string, object?> { ["action"] = "clear", ["count"] = count, ["group_id"] = groupId });
                }

                var deleted = registry.Delete(objectId, groupId);
                var deleteSnapshot = registry.Snapshot();
                Log($"delete object_id={objectId} group_id={groupId ?? "(any)"} deleted={deleted} -> total={deleteSnapshot.TotalCount} visible={deleteSnapshot.VisibleCount}");
                return Success(new Dictionary<string, object?> { ["action"] = "delete", ["object_id"] = objectId, ["deleted"] = deleted });
            }
            case "clear":
            {
                var groupId = GetString(root, "group_id") ?? GetString(root, "groupId");
                var count = registry.DeleteAll(groupId);
                var clearSnapshot = registry.Snapshot();
                Log($"clear group_id={groupId ?? "(all)"} deleted={count} -> total={clearSnapshot.TotalCount} visible={clearSnapshot.VisibleCount}");
                return Success(new Dictionary<string, object?> { ["action"] = "clear", ["count"] = count, ["group_id"] = groupId });
            }
            default:
                throw new InvalidOperationException("Unsupported action: " + action);
        }
    }

    private static string PathToAction(string path)
    {
        return path switch
        {
            "geometry" => "geometry",
            "objects" => "geometry",
            "hide" => "hide",
            "show" => "show",
            "delete" => "delete",
            "clear" => "clear",
            "message" => "message",
            _ => path,
        };
    }

    private static string NormalizeAction(string action)
    {
        var normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "send_geometry" => "upsert",
            "upsert_geometry" => "upsert",
            "add" => "upsert",
            "remove" => "delete",
            "delete_all" => "clear",
            _ => normalized,
        };
    }

    private static void Log(string message)
    {
        RhinoApp.WriteLine("[GeometryRenderer:debug] " + message);
    }

    private static void QueueLog(string message)
    {
        try
        {
            RhinoApp.InvokeOnUiThread((Action)(() => Log(message)));
        }
        catch
        {
            // Logging must never affect API behavior.
        }
    }

    private static string DescribeStyle(DisplayObjectStyle style)
    {
        return $"color=rgba({style.Color.R},{style.Color.G},{style.Color.B},{style.Color.A}) " +
               $"line_width={style.LineWidth} point_size={style.PointSize} mesh_display={style.MeshDisplay} " +
               $"show_edges={style.ShowEdges} naked_edges={style.IncludeNakedEdges} " +
               $"vertex_colors={style.UseVertexColors}";
    }

    private static string DescribeGeometry(object geometry)
    {
        return geometry switch
        {
            Mesh mesh => $"geometry=Mesh vertices={mesh.Vertices.Count} faces={mesh.Faces.Count} normals={mesh.Normals.Count} valid={mesh.IsValid} bbox={DescribeBoundingBox(mesh.GetBoundingBox(true))}",
            Curve curve => $"geometry={curve.GetType().Name} closed={curve.IsClosed} length={SafeCurveLength(curve):0.###} valid={curve.IsValid} bbox={DescribeBoundingBox(curve.GetBoundingBox(true))}",
            Brep brep => $"geometry=Brep faces={brep.Faces.Count} edges={brep.Edges.Count} vertices={brep.Vertices.Count} valid={brep.IsValid} bbox={DescribeBoundingBox(brep.GetBoundingBox(true))}",
            Rhino.Geometry.Point point => $"geometry=Point location={DescribePoint(point.Location)} valid={point.IsValid}",
            Point3d point => $"geometry=Point3d location={DescribePoint(point)} valid={point.IsValid}",
            GeometryBase geometryBase => $"geometry={geometryBase.GetType().Name} object_type={geometryBase.ObjectType} valid={geometryBase.IsValid} bbox={DescribeBoundingBox(geometryBase.GetBoundingBox(true))}",
            _ => $"geometry={geometry.GetType().FullName}",
        };
    }

    private static double SafeCurveLength(Curve curve)
    {
        try
        {
            return curve.GetLength();
        }
        catch
        {
            return 0.0;
        }
    }

    private static string DescribeBoundingBox(BoundingBox boundingBox)
    {
        return boundingBox.IsValid
            ? $"min={DescribePoint(boundingBox.Min)} max={DescribePoint(boundingBox.Max)}"
            : "invalid";
    }

    private static string DescribePoint(Point3d point)
    {
        return $"({point.X:0.###},{point.Y:0.###},{point.Z:0.###})";
    }

    private static object DecodeGeometry(JsonElement geometryElement, string? geometryType)
    {
        var normalizedType = (geometryType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedType == "point" || LooksLikePoint(geometryElement))
        {
            return DecodePoint(geometryElement);
        }

        if (normalizedType == "polyline" || LooksLikePointArray(geometryElement))
        {
            return DecodePolyline(geometryElement);
        }

        var rawJson = geometryElement.GetRawText();
        var decoded = Rhino.Runtime.CommonObject.FromJSON(rawJson);
        if (decoded == null)
        {
            throw new InvalidOperationException("Could not decode geometry JSON");
        }

        if (decoded is GeometryBase geometryBase)
        {
            if (geometryBase is Mesh mesh)
            {
                if (mesh.Normals.Count == 0)
                {
                    mesh.Normals.ComputeNormals();
                }

                mesh.Compact();
            }

            if (!geometryBase.IsValid)
            {
                throw new InvalidOperationException("Decoded geometry is invalid: " + geometryBase.GetType().Name);
            }

            return geometryBase;
        }

        throw new InvalidOperationException("Decoded JSON is not Rhino geometry: " + decoded.GetType().Name);
    }

    private static bool LooksLikePoint(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object
            && TryGetProperty(element, "X") != null
            && TryGetProperty(element, "Y") != null
            && TryGetProperty(element, "Z") != null;
    }

    private static bool LooksLikePointArray(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array
            && element.GetArrayLength() > 0
            && element[0].ValueKind == JsonValueKind.Array;
    }

    private static Point3d DecodePoint(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() >= 3)
        {
            return new Point3d(element[0].GetDouble(), element[1].GetDouble(), element[2].GetDouble());
        }

        var x = GetDouble(element, "X") ?? GetDouble(element, "x") ?? 0.0;
        var y = GetDouble(element, "Y") ?? GetDouble(element, "y") ?? 0.0;
        var z = GetDouble(element, "Z") ?? GetDouble(element, "z") ?? 0.0;
        return new Point3d(x, y, z);
    }

    private static Curve DecodePolyline(JsonElement element)
    {
        var points = new List<Point3d>();
        foreach (var pointElement in element.EnumerateArray())
        {
            points.Add(DecodePoint(pointElement));
        }

        var polyline = new Polyline(points);
        if (!polyline.IsValid || polyline.Count < 2)
        {
            throw new InvalidOperationException("Polyline requires at least two valid points");
        }

        return new PolylineCurve(polyline);
    }

    private static DisplayObjectStyle ParseSettingsForGeometry(JsonElement root, object geometry)
    {
        var settingsKey = SettingsKeyForGeometry(geometry);
        var settingsElement = TryGetProperty(root, settingsKey);
        if (settingsElement == null || settingsElement.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{GeometryKindForSettings(geometry)} geometry requires object '{settingsKey}'");
        }

        return ParseSettings(root, settingsElement.Value, settingsKey);
    }

    private static string SettingsKeyForGeometry(object geometry)
    {
        return geometry switch
        {
            Mesh => "mesh_settings",
            Brep => "mesh_settings",
            Curve => "curve_settings",
            Rhino.Geometry.Point => "point_settings",
            Point3d => "point_settings",
            _ => throw new InvalidOperationException("Unsupported geometry settings type: " + geometry.GetType().Name),
        };
    }

    private static string GeometryKindForSettings(object geometry)
    {
        return geometry switch
        {
            Mesh => "mesh",
            Brep => "brep",
            Curve => "curve",
            Rhino.Geometry.Point => "point",
            Point3d => "point",
            _ => geometry.GetType().Name,
        };
    }

    private static DisplayObjectStyle ParseSettings(JsonElement root, JsonElement element, string settingsKey)
    {
        var style = DisplayObjectStyle.Default;
        var opacity = 1.0;
        if (settingsKey == "mesh_settings")
        {
            opacity = Clamp(GetDouble(element, "opacity") ?? 1.0, 0.0, 1.0);
        }
        else if (TryGetProperty(element, "opacity") != null)
        {
            throw new InvalidOperationException("opacity is only supported in mesh_settings");
        }

        style.Color = ParseColor(TryGetProperty(root, "color"), opacity);

        switch (settingsKey)
        {
            case "mesh_settings":
            {
                style.LineWidth = Math.Max(1, (int)Math.Round(GetDouble(element, "edge_width") ?? 1.0));
                var display = (GetString(element, "display") ?? "shaded").ToLowerInvariant();
                style.MeshDisplay = display == "wireframe" ? MeshDisplayMode.Wireframe : MeshDisplayMode.Shaded;
                style.ShowEdges = GetBool(element, "show_edges") ?? false;
                style.IncludeNakedEdges = GetBool(element, "include_naked_edges") ?? true;
                style.UseVertexColors = GetBool(element, "use_vertex_colors") ?? false;
                style.SharpEdgeAngleDegrees = Clamp(GetDouble(element, "sharp_edge_angle_degrees") ?? 30.0, 0.0, 180.0);
                break;
            }
            case "curve_settings":
                style.LineWidth = Math.Max(1, (int)Math.Round(GetDouble(element, "line_width") ?? 1.0));
                style.ShowEdges = false;
                break;
            case "point_settings":
                style.PointSize = Math.Max(1, (int)Math.Round(GetDouble(element, "point_size") ?? 4.0));
                style.ShowEdges = false;
                break;
            default:
                throw new InvalidOperationException("Unsupported settings key: " + settingsKey);
        }

        return style;
    }

    private static Color ParseColor(JsonElement? colorElement, double opacity)
    {
        var red = 255;
        var green = 0;
        var blue = 0;
        var alpha = ClampToByte(opacity * 255.0);

        if (colorElement == null)
        {
            return Color.FromArgb(alpha, red, green, blue);
        }

        var color = colorElement.Value;
        if (color.ValueKind == JsonValueKind.String)
        {
            var text = color.GetString()?.Trim() ?? string.Empty;
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                text = text[1..];
            }

            if (text.Length == 6 && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                red = (rgb >> 16) & 0xff;
                green = (rgb >> 8) & 0xff;
                blue = rgb & 0xff;
            }
        }
        else if (color.ValueKind == JsonValueKind.Array)
        {
            if (color.GetArrayLength() > 0) red = ClampToByte(color[0].GetDouble());
            if (color.GetArrayLength() > 1) green = ClampToByte(color[1].GetDouble());
            if (color.GetArrayLength() > 2) blue = ClampToByte(color[2].GetDouble());
            if (color.GetArrayLength() > 3) alpha = ClampToByte(color[3].GetDouble());
        }
        else if (color.ValueKind == JsonValueKind.Object)
        {
            red = ClampToByte(GetDouble(color, "red") ?? GetDouble(color, "r") ?? red);
            green = ClampToByte(GetDouble(color, "green") ?? GetDouble(color, "g") ?? green);
            blue = ClampToByte(GetDouble(color, "blue") ?? GetDouble(color, "b") ?? blue);
            alpha = ClampToByte(GetDouble(color, "alpha") ?? GetDouble(color, "a") ?? alpha);
        }

        return Color.FromArgb(alpha, red, green, blue);
    }

    private static Dictionary<string, object?> Success(Dictionary<string, object?> data)
    {
        data["ok"] = true;
        return data;
    }

    private static JsonElement? TryGetProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(name, out var value) ? value : null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        var value = TryGetProperty(element, name);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        var value = TryGetProperty(element, name);
        if (value == null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.Value.ValueKind == JsonValueKind.String && double.TryParse(value.Value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        var value = TryGetProperty(element, name);
        if (value == null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.Value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static int ClampToByte(double value)
    {
        return (int)Math.Round(Clamp(value, 0.0, 255.0));
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static Task WriteErrorAsync(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        return WriteJsonAsync(response, new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["error"] = message,
        });
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private static Task<T> InvokeOnRhinoThreadAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }));
        return completion.Task;
    }
}

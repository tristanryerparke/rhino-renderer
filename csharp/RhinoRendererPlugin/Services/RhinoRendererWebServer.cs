using System.Drawing;
using System.Net;
using System.Text;
using System.Text.Json;
using Rhino;
using Rhino.Geometry;
using RhinoRendererPlugin.Display;

namespace RhinoRendererPlugin.Services;

public sealed class RhinoRendererWebServer : IDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:17891/";

    private readonly RhinoRendererPlugin _plugin;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenTask;

    public RhinoRendererWebServer(RhinoRendererPlugin plugin)
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
            if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "health")
            {
                await WriteJsonAsync(context.Response, new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["plugin"] = "RhinoRendererPlugin",
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
                    return registry?.Snapshot() ?? new DisplayRegistrySnapshot();
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
            var result = await InvokeOnRhinoThreadAsync(() => HandlePost(path, body)).ConfigureAwait(false);
            await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
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

        switch (action)
        {
            case "upsert":
            case "geometry":
            {
                var objectId = GetString(root, "object_id") ?? GetString(root, "objectId") ?? Guid.NewGuid().ToString("N");
                var groupId = GetString(root, "group_id") ?? GetString(root, "groupId");
                var type = GetString(root, "geometry_type") ?? GetString(root, "geometryType") ?? GetString(root, "type");
                var visible = GetBool(root, "visible") ?? true;
                var style = ParseStyle(TryGetProperty(root, "style"));
                var geometryElement = TryGetProperty(root, "geometry") ?? root;
                var geometry = DecodeGeometry(geometryElement, type);
                var record = registry.Upsert(objectId, geometry, style, groupId, visible);
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
                    return Success(new Dictionary<string, object?>
                    {
                        ["action"] = action,
                        ["count"] = count,
                        ["group_id"] = groupId,
                    });
                }

                var changed = registry.SetVisible(objectId, visible, groupId);
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
                    return Success(new Dictionary<string, object?> { ["action"] = "clear", ["count"] = count, ["group_id"] = groupId });
                }

                var deleted = registry.Delete(objectId, groupId);
                return Success(new Dictionary<string, object?> { ["action"] = "delete", ["object_id"] = objectId, ["deleted"] = deleted });
            }
            case "clear":
            {
                var groupId = GetString(root, "group_id") ?? GetString(root, "groupId");
                var count = registry.DeleteAll(groupId);
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

    private static DisplayObjectStyle ParseStyle(JsonElement? styleElement)
    {
        var style = DisplayObjectStyle.Default;
        if (styleElement == null || styleElement.Value.ValueKind != JsonValueKind.Object)
        {
            return style;
        }

        var element = styleElement.Value;
        var opacity = Clamp(GetDouble(element, "opacity") ?? 1.0, 0.0, 1.0);
        var color = ParseColor(TryGetProperty(element, "color"), opacity);
        style.Color = color;
        style.LineWidth = Math.Max(1, (int)Math.Round(GetDouble(element, "line_width") ?? GetDouble(element, "lineWidth") ?? 1.0));

        var display = (GetString(element, "display") ?? GetString(element, "mesh_display") ?? GetString(element, "meshDisplay") ?? "shaded").ToLowerInvariant();
        style.MeshDisplay = display == "wireframe" ? MeshDisplayMode.Wireframe : MeshDisplayMode.Shaded;
        style.ShowEdges = GetBool(element, "show_edges") ?? GetBool(element, "showEdges") ?? true;
        style.IncludeNakedEdges = GetBool(element, "include_naked_edges") ?? GetBool(element, "includeNakedEdges") ?? true;
        style.UseVertexColors = GetBool(element, "use_vertex_colors") ?? GetBool(element, "useVertexColors") ?? false;
        style.SharpEdgeAngleDegrees = Clamp(GetDouble(element, "sharp_edge_angle_degrees") ?? GetDouble(element, "sharpEdgeAngleDegrees") ?? 30.0, 0.0, 180.0);
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

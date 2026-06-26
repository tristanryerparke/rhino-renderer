using Rhino;
using Rhino.PlugIns;
using Rhino.UI;
using GeometryRendererPlugin.Display;
using GeometryRendererPlugin.Panels;
using GeometryRendererPlugin.Services;

namespace GeometryRendererPlugin;

[System.Runtime.InteropServices.Guid("959C6565-9CA8-445C-A36A-CD9E44B715C6")]
public sealed class GeometryRendererPlugin : PlugIn
{
    private readonly Dictionary<uint, DisplayRegistry> _registries = new();
    private GeometryRendererWebServer? _webServer;

    public GeometryRendererPlugin()
    {
        Instance = this;
    }

    public static GeometryRendererPlugin? Instance { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        Rhino.UI.Panels.RegisterPanel(
            this,
            typeof(GeometryRendererPanel),
            "Geometry Renderer",
            null,
            PanelType.PerDoc);

        RhinoDoc.CloseDocument += OnCloseDocument;

        try
        {
            _webServer = new GeometryRendererWebServer(this);
            _webServer.Start();
            RhinoApp.WriteLine("[GeometryRenderer] Web API listening on http://127.0.0.1:17891");
        }
        catch (Exception exception)
        {
            _webServer?.Dispose();
            _webServer = null;
            RhinoApp.WriteLine($"[GeometryRenderer] Web API failed to start: {exception.Message}");
        }

        return LoadReturnCode.Success;
    }

    protected override void OnShutdown()
    {
        _webServer?.Dispose();
        _webServer = null;

        RhinoDoc.CloseDocument -= OnCloseDocument;
        foreach (var registry in _registries.Values)
        {
            registry.Conduit.Enabled = false;
        }

        _registries.Clear();
        base.OnShutdown();
    }

    public DisplayRegistry GetRegistry(RhinoDoc document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!_registries.TryGetValue(document.RuntimeSerialNumber, out var registry))
        {
            registry = new DisplayRegistry(document);
            _registries[document.RuntimeSerialNumber] = registry;
        }

        return registry;
    }

    public DisplayRegistry? GetRegistry(uint documentSerialNumber)
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(documentSerialNumber);
        return document == null ? null : GetRegistry(document);
    }

    public DisplayRegistry? GetActiveRegistry()
    {
        var document = RhinoDoc.ActiveDoc;
        return document == null ? null : GetRegistry(document);
    }

    public void OpenPanel()
    {
        Rhino.UI.Panels.OpenPanel(GeometryRendererPanel.PanelId);
    }

    private void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        if (e.Document == null)
        {
            return;
        }

        if (_registries.TryGetValue(e.Document.RuntimeSerialNumber, out var registry))
        {
            registry.Conduit.Enabled = false;
            _registries.Remove(e.Document.RuntimeSerialNumber);
        }
    }
}

using Rhino;
using Rhino.PlugIns;
using Rhino.UI;
using RhinoRendererPlugin.Display;
using RhinoRendererPlugin.Panels;
using RhinoRendererPlugin.Services;

namespace RhinoRendererPlugin;

[System.Runtime.InteropServices.Guid("1126EF13-CD7F-47EB-9186-8235DACE042E")]
public sealed class RhinoRendererPlugin : PlugIn
{
    private readonly Dictionary<uint, DisplayRegistry> _registries = new();
    private RhinoRendererWebServer? _webServer;

    public RhinoRendererPlugin()
    {
        Instance = this;
    }

    public static RhinoRendererPlugin? Instance { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        Rhino.UI.Panels.RegisterPanel(
            this,
            typeof(RhinoRendererPanel),
            "Rhino Renderer",
            null,
            PanelType.PerDoc);

        RhinoDoc.CloseDocument += OnCloseDocument;

        _webServer = new RhinoRendererWebServer(this);
        _webServer.Start();
        RhinoApp.WriteLine("[RhinoRenderer] Web API listening on http://127.0.0.1:17891");

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
        Rhino.UI.Panels.OpenPanel(RhinoRendererPanel.PanelId);
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

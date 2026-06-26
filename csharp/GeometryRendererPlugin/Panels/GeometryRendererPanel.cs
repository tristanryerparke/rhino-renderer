using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using GeometryRendererPlugin.Display;

namespace GeometryRendererPlugin.Panels;

[System.Runtime.InteropServices.Guid("373FED19-17D4-490F-AD70-985108C71E4F")]
public sealed class GeometryRendererPanel : Panel, IPanel
{
    private readonly uint _documentSerialNumber;
    private readonly Label _summaryLabel = new() { Text = "No objects", Wrap = WrapMode.Word };

    public GeometryRendererPanel(uint documentSerialNumber)
    {
        _documentSerialNumber = documentSerialNumber;
        Title = "Geometry Renderer";
        Content = BuildContent();

        var registry = Registry();
        if (registry != null)
        {
            registry.Changed += OnRegistryChanged;
        }

        Load += (_, _) => RefreshSummary();
    }

    public static Guid PanelId => typeof(GeometryRendererPanel).GUID;

    public string Title { get; }

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        RefreshSummary();
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
    {
    }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        var registry = Registry();
        if (registry != null)
        {
            registry.Changed -= OnRegistryChanged;
        }
    }

    private Control BuildContent()
    {
        var showAllButton = new Button { Text = "Show all" };
        var hideAllButton = new Button { Text = "Hide all" };
        var clearButton = new Button { Text = "Clear" };
        var refreshButton = new Button { Text = "Refresh" };

        showAllButton.Click += (_, _) =>
        {
            Registry()?.SetAllVisible(true);
            RefreshSummary();
        };
        hideAllButton.Click += (_, _) =>
        {
            Registry()?.SetAllVisible(false);
            RefreshSummary();
        };
        clearButton.Click += (_, _) =>
        {
            Registry()?.DeleteAll();
            RefreshSummary();
        };
        refreshButton.Click += (_, _) => RefreshSummary();

        var layout = new DynamicLayout
        {
            Padding = new Padding(4),
            DefaultSpacing = new Size(4, 4),
        };
        layout.AddRow(showAllButton);
        layout.AddRow(hideAllButton);
        layout.AddRow(clearButton);
        layout.AddRow(refreshButton);
        layout.AddRow(_summaryLabel);
        layout.Add(null);
        return new Scrollable
        {
            Border = BorderType.None,
            Content = layout,
        };
    }

    private DisplayRegistry? Registry()
    {
        return GeometryRendererPlugin.Instance?.GetRegistry(_documentSerialNumber);
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        RhinoApp.InvokeOnUiThread((Action)RefreshSummary);
    }

    private void RefreshSummary()
    {
        var registry = Registry();
        if (registry == null)
        {
            _summaryLabel.Text = "No active registry";
            return;
        }

        var snapshot = registry.Snapshot();
        _summaryLabel.Text =
            $"Objects: {snapshot.TotalCount}\n" +
            $"Visible: {snapshot.VisibleCount}\n" +
            $"Groups: {snapshot.GroupCount}\n\n" +
            "API:\n" +
            "127.0.0.1\n" +
            "port 17891";
    }
}

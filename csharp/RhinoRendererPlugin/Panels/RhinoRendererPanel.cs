using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using RhinoRendererPlugin.Display;

namespace RhinoRendererPlugin.Panels;

[System.Runtime.InteropServices.Guid("2EA8C644-5C87-46FE-A094-E87C83454215")]
public sealed class RhinoRendererPanel : Panel, IPanel
{
    private readonly uint _documentSerialNumber;
    private readonly Label _summaryLabel = new() { Text = "No objects", Wrap = WrapMode.Word };

    public RhinoRendererPanel(uint documentSerialNumber)
    {
        _documentSerialNumber = documentSerialNumber;
        Title = "Rhino Renderer";
        Content = BuildContent();

        var registry = Registry();
        if (registry != null)
        {
            registry.Changed += OnRegistryChanged;
        }

        Load += (_, _) => RefreshSummary();
    }

    public static Guid PanelId => typeof(RhinoRendererPanel).GUID;

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
            Padding = 8,
            DefaultSpacing = new Size(6, 6),
        };
        layout.AddRow(showAllButton, hideAllButton);
        layout.AddRow(clearButton, refreshButton);
        layout.AddRow(_summaryLabel);
        layout.Add(null);
        return layout;
    }

    private DisplayRegistry? Registry()
    {
        return RhinoRendererPlugin.Instance?.GetRegistry(_documentSerialNumber);
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
            "Web API: http://127.0.0.1:17891";
    }
}

using Rhino;
using Rhino.Commands;

namespace RhinoRendererPlugin.Commands;

public sealed class RhinoRendererCommand : Command
{
    public override string EnglishName => "RhinoRenderer";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoRendererPlugin.Instance?.OpenPanel();
        return Result.Success;
    }
}

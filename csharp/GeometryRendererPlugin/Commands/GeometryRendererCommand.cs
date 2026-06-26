using Rhino;
using Rhino.Commands;

namespace GeometryRendererPlugin.Commands;

[System.Runtime.InteropServices.Guid("3755DD27-9EED-4977-AC42-1F754E424A61")]
public sealed class GeometryRendererCommand : Command
{
    public override string EnglishName => "GeometryRenderer";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        GeometryRendererPlugin.Instance?.OpenPanel();
        return Result.Success;
    }
}

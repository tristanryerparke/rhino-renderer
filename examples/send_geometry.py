"""Example: send rhino3dm geometry to the Rhino Renderer plugin.

Run Rhino, load the C# plugin, then run:

    python examples/send_geometry.py
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import rhino3dm

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python"))
from rhino_renderer_client import RhinoRendererClient  # noqa: E402


def make_box_mesh(size: float = 10.0) -> rhino3dm.Mesh:
    mesh = rhino3dm.Mesh()
    vertices = [
        (0, 0, 0),
        (size, 0, 0),
        (size, size, 0),
        (0, size, 0),
        (0, 0, size),
        (size, 0, size),
        (size, size, size),
        (0, size, size),
    ]
    for vertex in vertices:
        mesh.Vertices.Add(*vertex)

    mesh.Faces.AddFace(0, 1, 2, 3)
    mesh.Faces.AddFace(4, 7, 6, 5)
    mesh.Faces.AddFace(0, 4, 5, 1)
    mesh.Faces.AddFace(1, 5, 6, 2)
    mesh.Faces.AddFace(2, 6, 7, 3)
    mesh.Faces.AddFace(3, 7, 4, 0)
    mesh.Normals.ComputeNormals()
    return mesh


def make_spiral_curve() -> rhino3dm.PolylineCurve:
    points = []
    for index in range(80):
        t = index / 8.0
        radius = 0.35 * index
        points.append(rhino3dm.Point3d(math.cos(t) * radius, math.sin(t) * radius, 12 + index * 0.1))
    return rhino3dm.PolylineCurve(rhino3dm.Polyline(points))


def main() -> None:
    client = RhinoRendererClient()
    print("health:", client.health())

    print(
        "box:",
        client.send_geometry(
            make_box_mesh(),
            object_id="sample-box",
            group_id="sample",
            color="#3b82f6",
            opacity=0.55,
            display="shaded",
            line_width=2,
            show_edges=True,
        ),
    )

    print(
        "spiral:",
        client.send_geometry(
            make_spiral_curve(),
            object_id="sample-spiral",
            group_id="sample",
            color=(255, 128, 0),
            opacity=1.0,
            line_width=4,
        ),
    )

    # These messages target previously sent geometry.
    # client.hide(object_id="sample-box")
    # client.show(object_id="sample-box")
    # client.clear(group_id="sample")


if __name__ == "__main__":
    main()

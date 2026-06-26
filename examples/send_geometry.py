"""Example: send rhino3dm geometry to the Geometry Renderer plugin.

Run Rhino, load the C# plugin, then run:

    python examples/send_geometry.py
"""

from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

import rhino3dm

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python"))
from geometry_renderer_client import GeometryRendererClient  # noqa: E402


def load_stl_mesh(path: Path) -> rhino3dm.Mesh:
    data = path.read_bytes()
    if len(data) >= 84:
        triangle_count = struct.unpack_from("<I", data, 80)[0]
        expected_size = 84 + triangle_count * 50
        if expected_size == len(data):
            return load_binary_stl_mesh(data, triangle_count)

    return load_ascii_stl_mesh(data.decode("utf-8", errors="replace"))


def load_binary_stl_mesh(data: bytes, triangle_count: int) -> rhino3dm.Mesh:
    mesh = rhino3dm.Mesh()
    offset = 84
    for _ in range(triangle_count):
        # normal: 3 floats, vertices: 9 floats, attribute byte count: uint16
        values = struct.unpack_from("<12fH", data, offset)
        vertex_values = values[3:12]
        first_index = len(mesh.Vertices)
        for index in range(0, 9, 3):
            mesh.Vertices.Add(*vertex_values[index : index + 3])
        mesh.Faces.AddFace(first_index, first_index + 1, first_index + 2)
        offset += 50

    mesh.Normals.ComputeNormals()
    return mesh


def load_ascii_stl_mesh(text: str) -> rhino3dm.Mesh:
    mesh = rhino3dm.Mesh()
    triangle_vertices: list[tuple[float, float, float]] = []
    for line in text.splitlines():
        parts = line.strip().split()
        if len(parts) == 4 and parts[0].lower() == "vertex":
            triangle_vertices.append((float(parts[1]), float(parts[2]), float(parts[3])))
            if len(triangle_vertices) == 3:
                first_index = len(mesh.Vertices)
                for vertex in triangle_vertices:
                    mesh.Vertices.Add(*vertex)
                mesh.Faces.AddFace(first_index, first_index + 1, first_index + 2)
                triangle_vertices.clear()

    if len(mesh.Faces) == 0:
        raise ValueError("No triangles found in STL file")

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
    client = GeometryRendererClient()
    print("health:", client.health())

    stl_path = Path(__file__).resolve().parents[1] / "test-mesh.stl"
    test_mesh = load_stl_mesh(stl_path)
    print(f"loaded {stl_path.name}: {len(test_mesh.Vertices)} vertices, {len(test_mesh.Faces)} faces")

    print(
        "mesh:",
        client.send_geometry(
            test_mesh,
            object_id="test-mesh",
            group_id="sample",
            color="#3b82f6",
            opacity=1,
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
    # client.hide(object_id="test-mesh")
    # client.show(object_id="test-mesh")
    # client.clear(group_id="sample")


if __name__ == "__main__":
    main()

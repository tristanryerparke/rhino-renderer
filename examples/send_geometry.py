"""Example: send rhino3dm geometry to the Geometry Renderer plugin.

Run Rhino, load the C# plugin, then run:

    python examples/send_geometry.py
"""

from __future__ import annotations

import math
import struct
import sys
from pathlib import Path
from typing import Any, cast

import rhino3dm

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python"))
from geometry_renderer_client import GeometryRendererClient  # noqa: E402


def mesh_vertex_count(mesh: rhino3dm.Mesh) -> int:
    return len(cast(Any, mesh.Vertices))


def mesh_face_count(mesh: rhino3dm.Mesh) -> int:
    return int(cast(Any, mesh.Faces).Count)


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
        face_indices: list[int] = []
        for index in range(0, 9, 3):
            face_indices.append(mesh.Vertices.Add(*vertex_values[index : index + 3]))
        mesh.Faces.AddFace(face_indices[0], face_indices[1], face_indices[2])
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
                face_indices = [mesh.Vertices.Add(*vertex) for vertex in triangle_vertices]
                mesh.Faces.AddFace(face_indices[0], face_indices[1], face_indices[2])
                triangle_vertices.clear()

    if mesh_face_count(mesh) == 0:
        raise ValueError("No triangles found in STL file")

    mesh.Normals.ComputeNormals()
    return mesh


def make_spiral_curve() -> rhino3dm.PolylineCurve:
    points = []
    for index in range(80):
        t = index / 8.0
        radius = 0.35 * index
        points.append(rhino3dm.Point3d(math.cos(t) * radius, math.sin(t) * radius, 12 + index * 0.1))
    polyline = rhino3dm.Polyline(len(points))
    for point in points:
        polyline.Add(point.X, point.Y, point.Z)
    curve = polyline.ToPolylineCurve()
    if curve is None:
        raise ValueError("Could not create spiral polyline curve")
    return curve


def main() -> None:
    client = GeometryRendererClient()
    print("health:", client.health())

    stl_path = Path(__file__).resolve().parents[1] / "test-mesh.stl"
    test_mesh = load_stl_mesh(stl_path)
    print(f"loaded {stl_path.name}: {mesh_vertex_count(test_mesh)} vertices, {mesh_face_count(test_mesh)} faces")

    print(
        "mesh:",
        client.send_geometry(
            test_mesh,
            object_id="test-mesh",
            group_id="sample",
            geometry_type="mesh",
            color=(18, 130, 240),
            mesh_settings={
                "opacity": 1,
                "display": "shaded",
                "edge_width": 5,
                "show_edges": True,
                "sharp_edge_angle_degrees": 15,
                "include_naked_edges": False,
            },
        ),
    )

    print(
        "spiral:",
        client.send_geometry(
            make_spiral_curve(),
            object_id="sample-spiral",
            group_id="sample",
            geometry_type="curve",
            color=(255, 128, 0),
            curve_settings={
                "line_width": 1,
            },
        ),
    )


if __name__ == "__main__":
    main()

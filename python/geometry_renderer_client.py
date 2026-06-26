"""Small Python client for the Geometry Renderer C# plugin.

The only required geometry convention is that Rhino geometry objects must expose
rhino3dm's ``Encode()`` method, or you can pass an already-encoded dict.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from typing import Any


class GeometryRendererError(RuntimeError):
    pass


class GeometryRendererClient:
    def __init__(self, base_url: str = "http://127.0.0.1:17891", timeout: float = 10.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def health(self) -> dict[str, Any]:
        return self._request("GET", "/health")

    def send_geometry(
        self,
        geometry: Any,
        *,
        object_id: str | None = None,
        group_id: str | None = None,
        geometry_type: str,
        color: str | tuple[int, int, int] | dict[str, int] = "#ff0000",
        mesh_settings: dict[str, Any] | None = None,
        curve_settings: dict[str, Any] | None = None,
        point_settings: dict[str, Any] | None = None,
        visible: bool = True,
    ) -> dict[str, Any]:
        """Send or replace one conduit object.

        ``geometry_type`` must be "mesh", "brep", "curve", or "point".
        Mesh/Brep geometry requires ``mesh_settings``.
        Curve geometry requires ``curve_settings``.
        Point geometry requires ``point_settings``.
        """
        display_kind = normalize_geometry_type(geometry_type)
        expected_settings_key = settings_key_for_geometry_type(display_kind)
        settings_by_key = {
            "mesh_settings": mesh_settings,
            "curve_settings": curve_settings,
            "point_settings": point_settings,
        }
        settings = settings_by_key[expected_settings_key]
        if settings is None:
            raise TypeError(f"{display_kind} geometry requires {expected_settings_key}")
        validate_settings(expected_settings_key, settings)

        unexpected_settings = [
            key for key, value in settings_by_key.items()
            if key != expected_settings_key and value is not None
        ]
        if unexpected_settings:
            raise TypeError(
                f"{display_kind} geometry only accepts {expected_settings_key}; "
                f"unexpected: {', '.join(unexpected_settings)}"
            )

        payload = {
            "object_id": object_id,
            "group_id": group_id,
            "geometry_type": display_kind,
            "geometry": encode_geometry(geometry),
            "color": color,
            expected_settings_key: settings,
            "visible": visible,
        }
        return self._request("POST", "/geometry", payload)

    def hide(self, *, object_id: str | None = None, group_id: str | None = None) -> dict[str, Any]:
        return self._request("POST", "/hide", {"object_id": object_id, "group_id": group_id})

    def show(self, *, object_id: str | None = None, group_id: str | None = None) -> dict[str, Any]:
        return self._request("POST", "/show", {"object_id": object_id, "group_id": group_id})

    def delete(self, *, object_id: str, group_id: str | None = None) -> dict[str, Any]:
        return self._request("POST", "/delete", {"object_id": object_id, "group_id": group_id})

    def clear(self, *, group_id: str | None = None) -> dict[str, Any]:
        return self._request("POST", "/clear", {"group_id": group_id})

    def message(self, action: str, **payload: Any) -> dict[str, Any]:
        payload["action"] = action
        return self._request("POST", "/message", payload)

    def _request(self, method: str, path: str, payload: dict[str, Any] | None = None) -> dict[str, Any]:
        data = None
        headers = {"Accept": "application/json"}
        if payload is not None:
            data = json.dumps(strip_none(payload), separators=(",", ":")).encode("utf-8")
            headers["Content-Type"] = "application/json"

        request = urllib.request.Request(
            self.base_url + path,
            data=data,
            headers=headers,
            method=method,
        )

        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                body = response.read().decode("utf-8")
        except urllib.error.HTTPError as error:
            body = error.read().decode("utf-8", errors="replace")
            raise GeometryRendererError(f"Geometry Renderer returned HTTP {error.code}: {body}") from error
        except urllib.error.URLError as error:
            raise GeometryRendererError(f"Could not connect to Geometry Renderer at {self.base_url}: {error}") from error

        result = json.loads(body) if body else {}
        if isinstance(result, dict) and result.get("ok") is False:
            raise GeometryRendererError(str(result.get("error") or result))
        return result


def infer_geometry_type(geometry: Any) -> str:
    type_name = type(geometry).__name__.lower()
    if "mesh" in type_name:
        return "mesh"
    if "brep" in type_name:
        return "brep"
    if "curve" in type_name or "polyline" in type_name or "line" in type_name:
        return "curve"
    if "point" in type_name or is_point_sequence(geometry):
        return "point"
    if isinstance(geometry, dict):
        raise TypeError("encoded geometry dictionaries require geometry_type")

    raise TypeError(f"Could not infer geometry type from {type(geometry).__name__}; pass geometry_type")


def normalize_geometry_type(geometry_type: str) -> str:
    normalized = geometry_type.strip().lower()
    if normalized in {"mesh", "brep", "curve", "point"}:
        return normalized
    if "mesh" in normalized:
        return "mesh"
    if "brep" in normalized or "surface" in normalized or "polysurface" in normalized:
        return "brep"
    if "curve" in normalized or "polyline" in normalized or "line" in normalized:
        return "curve"
    if "point" in normalized:
        return "point"
    raise TypeError(f"Unsupported geometry_type: {geometry_type}")


def settings_key_for_geometry_type(geometry_type: str) -> str:
    if geometry_type in {"mesh", "brep"}:
        return "mesh_settings"
    if geometry_type == "curve":
        return "curve_settings"
    if geometry_type == "point":
        return "point_settings"
    raise TypeError(f"Unsupported geometry_type: {geometry_type}")


ALLOWED_SETTINGS_KEYS = {
    "mesh_settings": {
        "opacity",
        "display",
        "edge_width",
        "show_edges",
        "sharp_edge_angle_degrees",
        "include_naked_edges",
        "use_vertex_colors",
    },
    "curve_settings": {"line_width"},
    "point_settings": {"point_size"},
}


def validate_settings(settings_key: str, settings: dict[str, Any]) -> None:
    allowed_keys = ALLOWED_SETTINGS_KEYS[settings_key]
    unexpected_keys = sorted(set(settings) - allowed_keys)
    if unexpected_keys:
        raise TypeError(
            f"Unsupported keys for {settings_key}: {', '.join(unexpected_keys)}. "
            f"Allowed keys: {', '.join(sorted(allowed_keys))}"
        )


def encode_geometry(geometry: Any) -> Any:
    if isinstance(geometry, dict):
        return geometry
    if is_point_sequence(geometry):
        return list(geometry)
    if type(geometry).__name__.lower() == "point3d":
        return [geometry.X, geometry.Y, geometry.Z]

    encode = getattr(geometry, "Encode", None)
    if encode is None:
        raise TypeError("geometry must be a rhino3dm object with Encode(), a point sequence, or an encoded dict")

    return encode()


def is_point_sequence(value: Any) -> bool:
    if not isinstance(value, (list, tuple)) or len(value) != 3:
        return False
    return all(isinstance(item, (int, float)) for item in value)


def strip_none(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: strip_none(item) for key, item in value.items() if item is not None}
    if isinstance(value, list):
        return [strip_none(item) for item in value]
    return value


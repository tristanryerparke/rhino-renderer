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
        color: str | tuple[int, int, int] | dict[str, int] = "#ff0000",
        opacity: float = 1.0,
        line_width: float = 1.0,
        display: str = "shaded",
        show_edges: bool = True,
        visible: bool = True,
        **style_options: Any,
    ) -> dict[str, Any]:
        """Send or replace one conduit object.

        ``geometry`` can be any rhino3dm object with ``Encode()``:
        Mesh, Curve, Brep, Point, PolylineCurve, etc.
        """
        style = {
            "color": color,
            "opacity": opacity,
            "line_width": line_width,
            "display": display,
            "show_edges": show_edges,
            **style_options,
        }
        payload = {
            "object_id": object_id,
            "group_id": group_id,
            "geometry": encode_geometry(geometry),
            "style": style,
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


def encode_geometry(geometry: Any) -> Any:
    if isinstance(geometry, dict):
        return geometry

    encode = getattr(geometry, "Encode", None)
    if encode is None:
        raise TypeError("geometry must be a rhino3dm object with Encode(), or an encoded dict")

    return encode()


def strip_none(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: strip_none(item) for key, item in value.items() if item is not None}
    if isinstance(value, list):
        return [strip_none(item) for item in value]
    return value


# Python Renderer Parity TODO

Goal: make this C# plugin + HTTP client repo able to do everything `~/projects-local/rhino-python-renderer` can do, without reintroducing the old Redis/RhinoCode architecture.

## Guiding principle

- Recreate **capabilities**, not necessarily the old transport or class structure.
- Prefer direct localhost HTTP + RhinoCommon/C# plugin behavior where possible.

## Current known gaps

- [ ] Prompt Rhino users to select geometry interactively
- [ ] Fetch existing Rhino document geometry by object GUID
- [ ] Return decoded geometry results to external Python callers
- [ ] Batch action support comparable to `MultiConduitAction`
- [ ] Document-targeting behavior comparable to `FileClient`
- [ ] Demo / test coverage for all old workflows

## 1. Client API parity

### Selection / prompting
- [ ] Add `prompt_select_geometry(...)` to `python/geometry_renderer_client.py`
- [ ] Support old parameters:
  - [ ] `prompt`
  - [ ] `multiple`
  - [ ] `object_type` (`point`, `curve`, `mesh`, `polysurface`)
  - [ ] `mesh_brep` (`coarse`, `smooth`)
  - [ ] `require_enter`
  - [ ] `preselect`
  - [ ] `select`
  - [ ] `subobjects`
  - [ ] `max_count`
- [ ] Return one decoded `rhino3dm` object for single-select
- [ ] Return a list of decoded `rhino3dm` objects for multi-select
- [ ] Preserve access to raw selection records (`object_id`, type, geometry payload)

### Existing-object fetch
- [ ] Add `get_object_by_guid(object_id, mesh_brep=None)` to the Python client
- [ ] Return decoded `rhino3dm` geometry
- [ ] Support meshing Breps/polysurfaces before returning them

### Batch operations
- [ ] Add a batch upsert API
- [ ] Add batch hide/show/delete APIs
- [ ] Decide whether batch API should be endpoint-based or use one generic action endpoint

### Introspection / health
- [ ] Keep `health()`
- [ ] Decide whether to add an explicit document info endpoint beyond current health payload
- [ ] Decide whether to expose object listing/filtering beyond current `/objects`

## 2. Plugin / server API work

### New endpoints or actions
- [ ] Add a server-side selection action/endpoint
- [ ] Add a server-side fetch-by-GUID action/endpoint
- [ ] Add a batch action/endpoint
- [ ] Decide whether `/message` should become a first-class generic action router or stay minimal

### Selection implementation
- [ ] Implement selection with RhinoCommon (`Rhino.Input.Custom.GetObject` / related APIs)
- [ ] Filter by requested object type
- [ ] Support single vs multi-select
- [ ] Support `require_enter`
- [ ] Support preselection toggles
- [ ] Support optionally selecting picked objects in the document
- [ ] Support subobject selection where meaningful
- [ ] Support max-count limiting
- [ ] Return cancellation state cleanly

### Fetch implementation
- [ ] Resolve Rhino document objects by GUID
- [ ] Support point / curve / mesh fetch directly
- [ ] Support Brep/polysurface fetch
- [ ] Support `mesh_brep=coarse|smooth` conversion before returning
- [ ] Return a structured payload with `object_id`, `object_type`, and encoded geometry

## 3. Geometry/result encoding parity

- [ ] Define a standard result payload for fetched/selected geometry
- [ ] Make sure `rhino3dm` decode works for all returned payloads
- [ ] Normalize naming differences:
  - [ ] current repo: `curve`, `brep`
  - [ ] old repo: `curve`, `mesh`, `point`, `polysurface`
- [ ] Decide how `polyline` should be represented at the API level
- [ ] Confirm point payload compatibility in both directions

## 4. Document targeting behavior

Old repo behavior was document-bound; this repo is currently active-document-oriented.

- [ ] Decide desired model:
  - [ ] keep active-doc-only
  - [ ] add explicit document serial targeting
  - [ ] support both
- [ ] If supporting explicit targeting, add document identifier parameters to relevant endpoints
- [ ] Handle active-document changes safely
- [ ] Decide whether the client should fail fast if Rhino switches documents mid-session

## 5. Python client ergonomics

- [ ] Add helpers to decode returned geometry payloads into `rhino3dm`
- [ ] Decide whether to return plain dicts, typed dataclasses/models, or decoded objects plus raw metadata
- [ ] Add convenience helpers similar to the old repo where still useful
- [ ] Keep the API simple enough for non-Rhino Python callers

## 6. Examples to add

- [ ] `examples/prompt_mesh.py`
- [ ] `examples/prompt_polysurface.py`
- [ ] `examples/prompt_polyline.py` or equivalent curve example
- [ ] `examples/get_existing_mesh.py`
- [ ] `examples/get_existing_polyline.py` or equivalent curve example
- [ ] `examples/get_existing_polysurface.py`
- [ ] one batch-upsert example

## 7. Tests

- [ ] Unit tests for Python client request/response shaping
- [ ] Unit tests for settings validation on new APIs
- [ ] Plugin-side tests for fetch result encoding
- [ ] Plugin-side tests for selection result shaping where practical
- [ ] End-to-end smoke tests for:
  - [ ] send geometry
  - [ ] hide/show/delete/clear
  - [ ] fetch existing geometry
  - [ ] prompt/select geometry
  - [ ] batch operations

## 8. Docs

- [ ] Update `README.md` with a parity roadmap section
- [ ] Document the new prompt/fetch endpoints
- [ ] Document result payload schemas
- [ ] Document document-targeting behavior clearly
- [ ] Document any deliberate differences from `rhino-python-renderer`

## 9. Migration / compatibility decisions

- [ ] Decide whether to mimic old method names exactly (`prompt_select_geometry`, `get_object_by_guid`)
- [ ] Decide whether to preserve old parameter names exactly
- [ ] Decide whether to add a compatibility layer so old demo scripts need minimal changes
- [ ] Decide whether to support both old and new geometry type labels (`polysurface` vs `brep`)

## Suggested implementation order

- [ ] 1. Add fetch-by-GUID server endpoint + Python client helper
- [ ] 2. Add prompt-select server endpoint + Python client helper
- [ ] 3. Add result decoding helpers and examples
- [ ] 4. Add batch operations
- [ ] 5. Decide and implement document-targeting semantics
- [ ] 6. Add tests and README updates

## Useful old-repo reference points

- `~/projects-local/rhino-python-renderer/src/rhino_renderer/action.py`
- `~/projects-local/rhino-python-renderer/src/rhino_renderer/file_client.py`
- `~/projects-local/rhino-python-renderer/src/rhino_renderer/in_rhino/handlers.py`
- `~/projects-local/rhino-python-renderer/demos/`

## Explicit non-goal

- [ ] Do **not** reintroduce Redis/RhinoCode as a dependency unless there is a compelling reason.

"""Generate the API contract from the FastAPI app.

Writes `openapi.json` (machine-readable, for client code generators) and
`API contract.md` (human-readable) next to `pyproject.toml`.

Run from `src/server`:  uv run python scripts/generate_contract.py
"""

import json
import sys
from pathlib import Path

SERVER_DIR = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(SERVER_DIR / "src"))

from main import app  # noqa: E402

# FastAPI's built-in error schemas; errors are left out of the contract for now.
HIDDEN_SCHEMAS = {"HTTPValidationError", "ValidationError"}
METHOD_ORDER = ["get", "post", "put", "patch", "delete"]


def ref_name(ref: str) -> str:
    return ref.rsplit("/", 1)[-1]


def is_inline(name: str) -> bool:
    # FastAPI's generated form-body schemas, e.g. `Body_create_job_jobs_post`.
    return name.startswith("Body_")


def type_of(schema: dict) -> str:
    if "$ref" in schema:
        name = ref_name(schema["$ref"])
        return f"[`{name}`](#{name.lower()})"
    if "anyOf" in schema:
        return " \\| ".join(type_of(s) for s in schema["anyOf"])
    if schema.get("type") == "array":
        return f"list of {type_of(schema.get('items', {}))}"
    if schema.get("format") == "binary" or schema.get("contentMediaType"):
        return "file"
    if "enum" in schema:
        return " \\| ".join(f"`{v}`" for v in schema["enum"])
    return f"`{schema.get('type', 'any')}`"


def constraints(schema: dict) -> str:
    parts = []
    for key, label in [
        ("minLength", "min length"),
        ("maxLength", "max length"),
        ("minimum", "min"),
        ("maximum", "max"),
        ("pattern", "pattern"),
    ]:
        if key in schema:
            parts.append(f"{label} `{schema[key]}`")
    if schema.get("default") not in (None, ""):
        parts.append(f"default `{schema['default']}`")
    return ", ".join(parts)


def cell(text: str) -> str:
    return " ".join(text.split()).replace("|", "\\|")


def fields_table(schema: dict) -> list[str]:
    required = set(schema.get("required", []))
    rows = ["| Field | Type | Required | Description |", "| -- | -- | -- | -- |"]
    for name, prop in schema.get("properties", {}).items():
        desc = "; ".join(x for x in [prop.get("description", ""), constraints(prop)] if x)
        rows.append(
            f"| `{name}` | {type_of(prop)} | {'yes' if name in required else 'no'} | {cell(desc)} |"
        )
    return rows


def render_operation(method: str, path: str, op: dict, schemas: dict) -> list[str]:
    out = [f"### `{method.upper()} {path}`", ""]
    if op.get("description"):
        out += [op["description"].strip(), ""]
    params = op.get("parameters", [])
    if params:
        out += ["**Parameters**", "", "| Name | In | Type | Required | Description |"]
        out.append("| -- | -- | -- | -- | -- |")
        for p in params:
            req = "yes" if p.get("required") else "no"
            desc = "; ".join(
                x for x in [p.get("description", ""), constraints(p["schema"])] if x
            )
            out.append(
                f"| `{p['name']}` | {p['in']} | {type_of(p['schema'])} | {req} | {cell(desc)} |"
            )
        out.append("")

    body = op.get("requestBody")
    if body:
        content_type, media = next(iter(body["content"].items()))
        schema = media["schema"]
        out += [f"**Request body** (`{content_type}`)", ""]
        name = ref_name(schema["$ref"]) if "$ref" in schema else None
        if name and is_inline(name):
            out += fields_table(schemas[name])
        else:
            out.append(f"Type: {type_of(schema)}")
        out.append("")

    for code, resp in op.get("responses", {}).items():
        if not code.startswith("2"):
            continue
        content = resp.get("content", {})
        json_schema = content.get("application/json", {}).get("schema")
        if json_schema:
            out.append(f"**Response `{code}`:** {type_of(json_schema)}")
        elif content:
            types = ", ".join(f"`{t}`" for t in content)
            out.append(f"**Response `{code}`:** raw bytes ({types})")
        else:
            out.append(f"**Response `{code}`:** empty")
    out.append("")
    return out


def render(spec: dict) -> str:
    info = spec["info"]
    schemas = spec.get("components", {}).get("schemas", {})
    out = [
        f"# {info['title']} contract (v{info['version']})",
        "",
        "> Generated from the server code by `scripts/generate_contract.py`. "
        "Don't edit this file by hand; change the code and regenerate.",
        "",
        info.get("description", "").strip(),
        "",
    ]

    # Group operations by their first tag, keeping the order they're declared in.
    groups: dict[str, list[tuple[str, str, dict]]] = {}
    for path, item in spec["paths"].items():
        for method in sorted(item, key=METHOD_ORDER.index):
            op = item[method]
            groups.setdefault((op.get("tags") or ["other"])[0], []).append(
                (method, path, op)
            )

    for tag, ops in groups.items():
        out += [f"## {tag.capitalize()}", ""]
        for method, path, op in ops:
            out += render_operation(method, path, op, schemas)

    out += ["## Models", ""]
    for name, schema in schemas.items():
        if name in HIDDEN_SCHEMAS or is_inline(name):
            continue
        out += [f"### `{name}`", ""]
        if schema.get("description"):
            out += [schema["description"].strip(), ""]
        if "enum" in schema:
            out += ["Values: " + ", ".join(f"`{v}`" for v in schema["enum"])]
        else:
            out += fields_table(schema)
        out.append("")

    return "\n".join(out).rstrip() + "\n"


def main() -> None:
    spec = app.openapi()
    (SERVER_DIR / "openapi.json").write_text(json.dumps(spec, indent=2) + "\n")
    (SERVER_DIR / "API contract.md").write_text(render(spec))
    print("Wrote openapi.json and API contract.md")


if __name__ == "__main__":
    main()

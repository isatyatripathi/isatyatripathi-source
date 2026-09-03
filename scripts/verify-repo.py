#!/usr/bin/env python3
"""Static repository verification for environments without a .NET SDK.

This is intentionally dependency-free. It validates the repository shape,
configuration relationships, XML project files, solution/project references,
and basic C# delimiter structure. It does not replace `dotnet build`.
"""

from __future__ import annotations

import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
FAILURES: list[str] = []
WARNINGS: list[str] = []
PASSES: list[str] = []


def passed(message: str) -> None:
    PASSES.append(message)
    print(f"PASS  {message}")


def failed(message: str) -> None:
    FAILURES.append(message)
    print(f"FAIL  {message}")


def warned(message: str) -> None:
    WARNINGS.append(message)
    print(f"WARN  {message}")


def load_json(path: Path) -> object:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def require_files() -> None:
    required = [
        "DevSignalStudio.sln",
        "global.json",
        "Directory.Build.props",
        "config/topics.json",
        "config/sources.json",
        "config/ai-providers.json",
        "config/content-recipes.json",
        "config/profile.json",
        "src/backend/DevSignalStudio.Api/Program.cs",
        "tests/backend/DevSignalStudio.Tests/Program.cs",
    ]
    missing = [item for item in required if not (ROOT / item).is_file()]
    if missing:
        failed("Required files are missing: " + ", ".join(missing))
    else:
        passed(f"Repository contains all {len(required)} required entry files")


def validate_json_files() -> None:
    paths = sorted(
        path
        for folder in (ROOT / "config", ROOT / "schemas")
        for path in folder.rglob("*.json")
    )
    paths += sorted((ROOT / "src/backend/DevSignalStudio.Api").rglob("*.json"))
    paths.append(ROOT / "global.json")

    invalid: list[str] = []
    for path in paths:
        try:
            load_json(path)
        except Exception as exc:  # noqa: BLE001 - verifier reports all parse failures
            invalid.append(f"{path.relative_to(ROOT)}: {exc}")
    if invalid:
        failed("JSON parsing failed:\n      " + "\n      ".join(invalid))
    else:
        passed(f"All {len(paths)} JSON files parse successfully")


def validate_xml_files() -> None:
    paths = sorted(ROOT.rglob("*.csproj")) + sorted(ROOT.glob("*.props")) + sorted(ROOT.glob("*.targets"))
    invalid: list[str] = []
    for path in paths:
        try:
            ET.parse(path)
        except Exception as exc:  # noqa: BLE001
            invalid.append(f"{path.relative_to(ROOT)}: {exc}")
    if invalid:
        failed("MSBuild XML parsing failed:\n      " + "\n      ".join(invalid))
    else:
        passed(f"All {len(paths)} MSBuild XML files parse successfully")


def validate_solution_paths() -> None:
    solution = (ROOT / "DevSignalStudio.sln").read_text(encoding="utf-8")
    project_paths = re.findall(r'^Project\("[^"]+"\) = "[^"]+", "([^"]+\.csproj)"', solution, re.MULTILINE)
    missing = [path for path in project_paths if not (ROOT / Path(path.replace("\\", "/"))).is_file()]
    if not project_paths:
        failed("Solution contains no C# project entries")
    elif missing:
        failed("Solution references missing projects: " + ", ".join(missing))
    else:
        passed(f"Solution references {len(project_paths)} existing projects")


def validate_project_references() -> None:
    missing: list[str] = []
    count = 0
    for project in ROOT.rglob("*.csproj"):
        tree = ET.parse(project)
        for reference in tree.findall(".//ProjectReference"):
            include = reference.attrib.get("Include")
            if not include:
                continue
            count += 1
            target = (project.parent / include.replace("\\", os.sep)).resolve()
            if not target.is_file():
                missing.append(f"{project.relative_to(ROOT)} -> {include}")
    if missing:
        failed("Project references are missing: " + ", ".join(missing))
    else:
        passed(f"All {count} project references resolve")


def duplicate_ids(values: Iterable[object]) -> list[str]:
    seen: set[str] = set()
    duplicates: set[str] = set()
    for value in values:
        normalized = str(value or "").strip().casefold()
        if not normalized:
            continue
        if normalized in seen:
            duplicates.add(normalized)
        seen.add(normalized)
    return sorted(duplicates)


def validate_configuration_relationships() -> None:
    topics = load_json(ROOT / "config/topics.json")
    recipes = load_json(ROOT / "config/content-recipes.json")
    sources = load_json(ROOT / "config/sources.json")
    providers = load_json(ROOT / "config/ai-providers.json")
    profile = load_json(ROOT / "config/profile.json")

    assert isinstance(topics, dict)
    assert isinstance(recipes, dict)
    assert isinstance(sources, dict)
    assert isinstance(providers, dict)
    assert isinstance(profile, dict)

    checks: list[str] = []
    for label, collection in (
        ("topic", topics.get("pillars", [])),
        ("recipe", recipes.get("recipes", [])),
        ("source", sources.get("sources", [])),
        ("provider", providers.get("providers", [])),
        ("route", providers.get("routes", [])),
    ):
        duplicates = duplicate_ids(item.get("id") for item in collection if isinstance(item, dict))
        if duplicates:
            checks.append(f"duplicate {label} IDs: {', '.join(duplicates)}")

    provider_ids = {
        str(item.get("id", "")).casefold()
        for item in providers.get("providers", [])
        if isinstance(item, dict)
    }
    route_ids = {
        str(item.get("id", "")).casefold()
        for item in providers.get("routes", [])
        if isinstance(item, dict)
    }
    default_route = str(providers.get("defaultRoute", "")).casefold()
    if default_route not in route_ids:
        checks.append(f"default AI route '{providers.get('defaultRoute')}' does not exist")

    for route in providers.get("routes", []):
        if not isinstance(route, dict):
            continue
        for task, ids in (route.get("tasks") or {}).items():
            for provider_id in ids or []:
                if str(provider_id).casefold() not in provider_ids:
                    checks.append(
                        f"route '{route.get('id')}' task '{task}' references unknown provider '{provider_id}'"
                    )

    supported_connectors = {"rss", "stackexchange", "json-file", "http-json", "manual"}
    for source in sources.get("sources", []):
        if not isinstance(source, dict):
            continue
        connector = str(source.get("connectorType", "")).casefold()
        if connector not in supported_connectors:
            checks.append(f"source '{source.get('id')}' uses unknown connector '{connector}'")
        if connector != "manual" and not source.get("endpoint"):
            checks.append(f"source '{source.get('id')}' has no endpoint")

    storage = profile.get("storage") or {}
    configured = str(storage.get("directory") or "data")
    candidate = (ROOT / configured).resolve() if not Path(configured).is_absolute() else Path(configured).resolve()
    try:
        candidate.relative_to(ROOT.resolve())
    except ValueError:
        checks.append("profile storage directory escapes the repository root")

    sdk = load_json(ROOT / "global.json")
    assert isinstance(sdk, dict)
    sdk_version = str((sdk.get("sdk") or {}).get("version", ""))
    if not sdk_version.startswith("10."):
        checks.append(f"global.json does not select a .NET 10 SDK: '{sdk_version}'")

    if checks:
        failed("Configuration relationship checks failed:\n      " + "\n      ".join(checks))
    else:
        passed("Configuration IDs, routes, sources, storage, and SDK relationships are consistent")


@dataclass
class ScanState:
    index: int = 0
    line: int = 1


def validate_csharp_delimiters() -> None:
    problems: list[str] = []
    for path in sorted(ROOT.rglob("*.cs")):
        text = path.read_text(encoding="utf-8")
        error = scan_csharp(text)
        if error:
            problems.append(f"{path.relative_to(ROOT)}: {error}")
    if problems:
        failed("C# delimiter scan failed:\n      " + "\n      ".join(problems))
    else:
        passed(f"Basic C# lexical/delimiter scan passed for {len(list(ROOT.rglob('*.cs')))} files")


def scan_csharp(text: str) -> str | None:
    stack: list[tuple[str, int]] = []
    matching = {")": "(", "]": "[", "}": "{"}
    state = ScanState()
    length = len(text)

    while state.index < length:
        char = text[state.index]
        next_char = text[state.index + 1] if state.index + 1 < length else ""

        if char == "\n":
            state.line += 1
            state.index += 1
            continue
        if char == "/" and next_char == "/":
            state.index += 2
            while state.index < length and text[state.index] != "\n":
                state.index += 1
            continue
        if char == "/" and next_char == "*":
            start_line = state.line
            state.index += 2
            while state.index + 1 < length and text[state.index : state.index + 2] != "*/":
                if text[state.index] == "\n":
                    state.line += 1
                state.index += 1
            if state.index + 1 >= length:
                return f"unterminated block comment beginning on line {start_line}"
            state.index += 2
            continue
        if char == "'":
            error = skip_quoted(text, state, "'", verbatim=False)
            if error:
                return error
            continue
        if char == '@' and next_char == '"':
            state.index += 1
            error = skip_quoted(text, state, '"', verbatim=True)
            if error:
                return error
            continue

        # C# raw strings have at least three quotes and may be prefixed by one
        # or more '$' characters. Skip the complete literal. Interpolation is
        # intentionally not parsed by this lightweight verifier.
        raw_start = state.index
        while raw_start < length and text[raw_start] == "$":
            raw_start += 1
        quote_count = 0
        while raw_start + quote_count < length and text[raw_start + quote_count] == '"':
            quote_count += 1
        if quote_count >= 3:
            state.index = raw_start + quote_count
            end = '"' * quote_count
            closing = text.find(end, state.index)
            if closing < 0:
                return f"unterminated raw string beginning on line {state.line}"
            state.line += text[state.index:closing].count("\n")
            state.index = closing + quote_count
            continue

        if char == '"':
            error = skip_quoted(text, state, '"', verbatim=False)
            if error:
                return error
            continue

        if char in "([{":
            stack.append((char, state.line))
        elif char in ")]}":
            expected = matching[char]
            if not stack or stack[-1][0] != expected:
                return f"unexpected '{char}' on line {state.line}"
            stack.pop()
        state.index += 1

    if stack:
        char, line = stack[-1]
        return f"unclosed '{char}' from line {line}"
    return None


def skip_quoted(text: str, state: ScanState, quote: str, verbatim: bool) -> str | None:
    start_line = state.line
    state.index += 1
    while state.index < len(text):
        char = text[state.index]
        if char == "\n":
            state.line += 1
            if not verbatim and quote == '"':
                return f"newline in regular string beginning on line {start_line}"
        if verbatim and char == '"' and state.index + 1 < len(text) and text[state.index + 1] == '"':
            state.index += 2
            continue
        if not verbatim and char == "\\":
            state.index += 2
            continue
        if char == quote:
            state.index += 1
            return None
        state.index += 1
    return f"unterminated quoted literal beginning on line {start_line}"


def validate_api_route_uniqueness() -> None:
    path = ROOT / "src/backend/DevSignalStudio.Api/Endpoints/ApiEndpoints.cs"
    text = path.read_text(encoding="utf-8")
    routes = re.findall(
        r'([A-Za-z_][A-Za-z0-9_]*)\.(MapGet|MapPost|MapPut|MapPatch|MapDelete)\("([^"]+)"',
        text,
    )
    duplicates = sorted({route for route in routes if routes.count(route) > 1})
    if duplicates:
        failed(
            "Duplicate API receiver/method/path declarations: "
            + ", ".join(f"{receiver}.{method} {path}" for receiver, method, path in duplicates)
        )
    else:
        passed(f"No unintended duplicate receiver/method/path declarations across {len(routes)} endpoint mappings")


def validate_dependency_policy() -> None:
    package_references: list[str] = []
    for project in ROOT.rglob("*.csproj"):
        tree = ET.parse(project)
        for reference in tree.findall(".//PackageReference"):
            package_references.append(
                f"{project.relative_to(ROOT)}:{reference.attrib.get('Include', '(unknown)')}"
            )
    if package_references:
        warned("External NuGet packages are present: " + ", ".join(package_references))
    else:
        passed("Backend and smoke tests use only the .NET shared framework; no external NuGet restore is required")


def main() -> int:
    print(f"DevSignal Studio static verification\nRoot: {ROOT}\n")
    require_files()
    validate_json_files()
    validate_xml_files()
    validate_solution_paths()
    validate_project_references()
    validate_configuration_relationships()
    validate_csharp_delimiters()
    validate_api_route_uniqueness()
    validate_dependency_policy()

    print("\nSummary")
    print(f"  Passed:   {len(PASSES)}")
    print(f"  Warnings: {len(WARNINGS)}")
    print(f"  Failed:   {len(FAILURES)}")
    if FAILURES:
        print("\nStatic verification failed. Run `dotnet build` after fixing the items above.")
        return 1
    print("\nStatic verification passed. Run the .NET smoke-test project for compiler and runtime verification.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

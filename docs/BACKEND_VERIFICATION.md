# Backend verification

## Verification layers

The repository provides two complementary verification paths.

### 1. Dependency-free static verifier

Run:

```bash
python3 scripts/verify-repo.py
```

or on Windows:

```powershell
python .\scripts\verify-repo.py
```

It checks:

- required repository entry files
- all JSON files
- all MSBuild XML files
- solution project paths
- project-to-project references
- duplicate configuration IDs
- AI route/provider references
- source connector and endpoint relationships
- storage path containment
- the selected .NET major version
- basic C# lexical and delimiter structure
- duplicate API receiver/method/path declarations
- external NuGet package references

It is useful in constrained environments, but it is not a compiler.

### 2. Compiler and runtime smoke checks

Run:

```bash
./scripts/test-backend.sh
```

or on Windows PowerShell:

```powershell
.\scripts\test-backend.ps1
```

The script performs:

1. Static repository verification.
2. `dotnet restore`.
3. A full solution build.
4. The dependency-free console smoke-test executable.

## Smoke-test coverage

The test executable currently covers:

1. Canonical URL tracking-parameter removal.
2. Mermaid security filtering.
3. Loopback and private-network URL safety behavior.
4. Local JSON connector ingestion.
5. JSON snapshot persistence and reload.
6. .NET and AI topic relevance scoring.
7. Duplicate configuration ID rejection.
8. Storage path traversal rejection.
9. Case-insensitive manual connector validation.
10. Unknown manual source rejection.
11. Draft publication lifecycle and URL validation.
12. End-to-end local JSON ingestion followed by deterministic mock draft generation.

The smoke tests intentionally use no test framework package, so the repository can restore from the .NET shared framework without reaching NuGet.

## Verification result in the build environment

The dependency-free verifier passed all repository, JSON, XML, reference, configuration, API-route and C# delimiter checks.

The current build environment does not contain a .NET SDK, compiler, MSBuild or container runtime, and outbound package/SDK download is blocked. For that reason, a real C# compile and runtime execution could not be completed here. The PowerShell and shell scripts are included so the compiler and smoke checks can be run immediately on a machine with the .NET 10 SDK.

## Expected successful output

The static verifier ends with:

```text
Static verification passed. Run the .NET smoke-test project for compiler and runtime verification.
```

The smoke test executable should end with:

```text
12/12 smoke checks passed.
```

Any compiler error or failed smoke check makes the test script return a non-zero exit code.

## Manual API check

After starting the API, verify:

```text
GET http://localhost:5180/health/ready
```

A ready instance returns HTTP 200 with `workspaceReady` and `configurationReady` set to `true`.

Then run the local-only flow from `requests/DevSignalStudio.Api.http`:

1. Test `curated-local`.
2. Start an ingestion run for `curated-local`.
3. Copy one returned item ID.
4. Start a draft with route `offline`.
5. Inspect the generation run and draft.
6. Edit, validate, approve and export it.

This path requires no internet access and no AI credentials.

# Local setup

This guide starts the DevSignal Studio backend on a Windows development machine. The default workflow uses a local JSON source and the deterministic mock AI provider, so no API key, database, Docker installation, or internet connection is required after the .NET SDK is available.

## 1. Install the prerequisite

Install a .NET 10 SDK, reopen PowerShell, and confirm:

```powershell
dotnet --version
```

The output must begin with `10.`. The repository's `global.json` selects SDK `10.0.100` and permits roll-forward to a later .NET 10 feature band.

Python 3 is optional for running the dependency-free repository verifier. It is not required merely to start the API.

## 2. Extract and open the repository

Extract the archive, then open PowerShell in the directory containing `DevSignalStudio.sln`.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

This changes the execution policy only for the current PowerShell process.

## 3. Build and run all backend checks

```powershell
.\scripts\bootstrap.ps1
```

That command restores the solution, builds all five projects, and executes the 12 console smoke checks.

For a Release build:

```powershell
.\scripts\bootstrap.ps1 -Configuration Release
```

## 4. Start the API

```powershell
.\scripts\run-backend.ps1
```

The local API listens on:

```text
http://127.0.0.1:5180
```

Useful first endpoints:

```text
http://127.0.0.1:5180/health/ready
http://127.0.0.1:5180/api/v1/dashboard
http://127.0.0.1:5180/api/v1/sources
```

Stop it with `Ctrl+C`.

## 5. Exercise the offline workflow

Open `requests/DevSignalStudio.Api.http` in Visual Studio, Rider, or VS Code with a REST client extension.

Run these requests in order:

1. Test the `curated-local` source.
2. Start an ingestion run using only local seed data.
3. Poll the ingestion run or list the highest-scoring items.
4. Copy an item ID into `@itemId` at the top of the file.
5. Generate a draft using the `offline` provider route.
6. Copy the generation run ID into `@generationRunId` and poll it.
7. Copy the returned `draftId` into `@draftId`.
8. Review, edit, validate, approve, and export the draft.

Ingestion and generation endpoints return `202 Accepted`; the work is executed by bounded background queues.

## 6. Run verification separately

Static verification only:

```powershell
.\scripts\verify-backend.ps1
```

Full static, compiler, and runtime verification:

```powershell
.\scripts\test-backend.ps1
```

A successful smoke-test run ends with:

```text
12/12 smoke checks passed.
```

## 7. Enable Ollama

Keep the API stopped while editing configuration.

1. Start Ollama on a loopback URL.
2. Edit `config/ai-providers.json`.
3. Set `ollama-local.enabled` to `true`.
4. Replace its model value with a model installed on your machine.
5. Set `defaultRoute` to `local-first`, or send `providerRoute: "local-first"` in a draft request.
6. Restart DevSignal Studio.
7. Test `POST /api/v1/providers/ollama-local/test`.

The route falls back to the mock provider when Ollama is disabled or unavailable.

## 8. Enable a hosted provider

Store credentials in environment variables, not JSON files.

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:ANTHROPIC_API_KEY = "your-key"
```

Then enable the corresponding provider, configure its model, and select a route containing it. Environment variables set this way last only for the current PowerShell process.

## 9. Configure the daily job

Edit `config/profile.json`:

```json
"schedule": {
  "enabled": true,
  "localTime": "07:00",
  "runOnStartupWhenOverdue": true,
  "maximumRunsPerDay": 1,
  "generateDrafts": true
}
```

The scheduler runs only while the local API process is running. Windows Task Scheduler can start the API at sign-in if unattended local operation is needed later.

## 10. Reset local runtime data

Stop the API and remove generated JSON files under `data/`. Keep `data/.gitkeep` if the repository is under source control.

```powershell
Get-ChildItem .\data -File | Where-Object Name -ne '.gitkeep' | Remove-Item
```

Configuration remains under `config/` and is not removed.

## Troubleshooting

### The required SDK was not found

Confirm `dotnet --version` begins with `10.`. Reopen the terminal after installing the SDK. A global SDK from another major version does not satisfy `net10.0`.

### DevSignal root could not be found

Start the API through the supplied script, or set the repository root explicitly:

```powershell
$env:DEVSIGNAL_ROOT = (Get-Location).Path
```

The directory must contain `config/topics.json`.

### Port 5180 is already in use

Stop the other process or override the URL for one session:

```powershell
$env:ASPNETCORE_URLS = "http://127.0.0.1:5190"
dotnet run --project .\src\backend\DevSignalStudio.Api
```

### A feed fails

Failures are isolated per source. Inspect the ingestion run's `sources`, `warnings`, and `errors`. Disable a problematic source through the API or `config/sources.json` while keeping the remaining sources active.

### A draft cannot be approved

Run the validation endpoint and resolve every issue with severity `error`. Warnings permit approval; errors do not. Use the current revision number in edit and decision requests.

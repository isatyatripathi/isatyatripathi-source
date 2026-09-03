# DevSignal Studio: Simple Setup Guide

This guide explains how to start the project and fix the most common AI provider problems.

## Open API Key = "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"

## 1. Project URLs

When the project is running, use these addresses:

- Frontend demo: `http://127.0.0.1:4175`
- Backend API: `http://127.0.0.1:5180`
- Backend readiness check: `http://127.0.0.1:5180/api/v1/health/ready`

The frontend demo is a local UI. The backend is the service that performs real provider checks and AI calls.

## 2. Build the project

Run these commands from the repository root:

```bash
dotnet build DevSignalStudio.sln --no-restore

cd src/frontend
npm run build:demo
cd ../..
```

## 3. Start the backend

Open Terminal 1 and run:

```bash
cd "/Users/satya/My Projects/devsignal-studio-mvp-with-frontend-demo"
dotnet run --project src/backend/DevSignalStudio.Api --no-build --urls http://127.0.0.1:5180
```

Leave this terminal open. Closing it stops the backend.

Check that it is running from Terminal 2:

```bash
curl http://127.0.0.1:5180/api/v1/health/ready
```

Expected result:

```json
{"status":"healthy","workspaceReady":true,"configurationReady":true}
```

## 4. Start the frontend demo

Open another terminal and run:

```bash
cd "/Users/satya/My Projects/devsignal-studio-mvp-with-frontend-demo/src/frontend"
PORT=4175 npm run preview:demo
```

Open `http://127.0.0.1:4175` in a browser.

If a port is already in use, choose another port, for example `PORT=4176`.

## 5. Configure OpenAI

Edit `config/ai-providers.json`. The OpenAI provider must use:

```json
{
  "id": "openai",
  "type": "openai",
  "enabled": true,
  "baseUrl": "https://api.openai.com/v1",
  "model": "gpt-4o-mini",
  "apiKeyEnvironmentVariable": "OPENAI_API_KEY"
}
```

Important:

- `baseUrl` is the OpenAI API URL. It must not be the provider test URL.
- `model` must be a real model name. Do not leave it as `configure-me`.
- `apiKeyEnvironmentVariable` must be the name `OPENAI_API_KEY`, not the actual key.
- Never store an API key in JSON, source code, screenshots, chat messages, or Git.

Set the key in the same terminal that starts the backend:

```bash
export OPENAI_API_KEY="your-new-openai-key"
dotnet run --project src/backend/DevSignalStudio.Api --no-build --urls http://127.0.0.1:5180
```

The environment variable is copied into the backend only when the backend starts. Setting it after the backend is already running does not update that process.

## 6. Test OpenAI correctly

The provider test endpoint accepts `POST`, not `GET`.

Correct:

```bash
curl -X POST http://127.0.0.1:5180/api/v1/providers/openai/test
```

Opening this URL directly in a browser sends `GET` and causes:

```text
405 Method Not Allowed
```

Possible results:

```json
{"providerId":"openai","status":"healthy"}
```

If the result says `OPENAI_API_KEY is not set`, restart the backend from the terminal where the variable is set.

If it says `requires a configured model`, replace `configure-me` with a real model name.

If it says an environment variable with a long `sk-...` value is not set, the API key was incorrectly placed in `apiKeyEnvironmentVariable`. Replace that field with `OPENAI_API_KEY`.

## 7. Select the provider in the UI

The default route is usually:

```json
"defaultRoute": "offline"
```

The `offline` route uses the deterministic Mock provider. This is intentional and works without an internet connection or API key.

To use OpenAI:

1. Enable and configure OpenAI on the **AI Models** page.
2. Save the provider.
3. Select OpenAI from the home-page model selector.
4. Or configure a route whose task list includes `openai`.

The **All enabled models** option runs all enabled providers concurrently. A provider must be enabled, have a valid model, and have its required environment variable available to the backend.

## 8. Security checklist

If an API key has been pasted into `ai-providers.json`, the terminal, a screenshot, or a chat:

1. Revoke that key in the provider dashboard.
2. Create a replacement key.
3. Store only the environment variable name in the JSON file.
4. Set the replacement key with `export OPENAI_API_KEY=...` before starting the backend.

## 9. Quick troubleshooting

### Provider page is blank

Rebuild the frontend and restart the demo server:

```bash
cd src/frontend
npm run build:demo
PORT=4175 npm run preview:demo
```

### Provider request returns HTML

The request reached the frontend fallback instead of the API. Start the backend on `5180`, then use the backend URL or configure the frontend proxy correctly.

### Port is already in use

Check the process:

```bash
lsof -nP -iTCP:5180 -sTCP:LISTEN
lsof -nP -iTCP:4175 -sTCP:LISTEN
```

Stop only the process belonging to this project, or use another port.

### Backend does not see the API key

Run this before starting the backend:

```bash
printenv OPENAI_API_KEY >/dev/null && echo "API key is set"
```

Then start the backend in that same terminal.

## More documentation

The original technical documentation remains in [`README.md`](README.md) and the detailed local setup guide is [`docs/LOCAL_SETUP.md`](docs/LOCAL_SETUP.md).
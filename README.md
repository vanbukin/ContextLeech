# ContextLeech 🪝

A lightweight, AI-based analyzer for .NET repositories.
ContextLeech ingests one file at a time and emits a compact, AI‑optimized analysis record for C#, ASP.NET Core, Razor, EF Core, and common supporting assets.

- Focus: high‑signal summaries for AI agents
- Guarantee: No code execution, no web browsing

## Features

- Single‑pass workflow per file with strict interaction protocol
- Clear categorization (Config | Source | Test | Docs | Other) and complexity rating (Low | Medium | High)
- Dependency awareness (upstream/downstream)
- Practical AI guidance and integration points in the output
- Token‑efficient JSON output ready for downstream tooling

## Requirements

- .NET SDK 9.0.305+ (or newer)
- A local LLM endpoint (example below uses llama.cpp’s `llama-server` with the `gpt‑oss‑20b` model)

## Quick start

1) Start a local model server (example, Windows PowerShell):

```powershell
llama-server `
--model "gpt-oss-20b-mxfp4.gguf" `
--threads 12 `
--ctx-size 0 `
--batch-size 131072 `
--ubatch-size 8192 `
--n-cpu-moe 0 `
--n-gpu-layers 999 `
--temp 0.6 `
--min-p 0.0 `
--top-p 0.8 `
--top-k 40 `
--repeat-penalty 1.15 `
--min-p 0.05 `
--no-mmap `
--host 0.0.0.0 `
--port 8080 `
--jinja `
--chat-template-kwargs '{"reasoning_effort": "high"}' `
--alias gpt-oss-20b `
--swa-checkpoints 0 `
--no-slots `
--slot-prompt-similarity 0.0 `
--no-prefill-assistant
```

2) Configure ContextLeech:

Edit `appsettings.json`:

```json
{
    "RepoPath": "PATH_TO_YOUR_REPOSITORY"
}
```

Set this to an absolute path to the repo you want analyzed.

3) Build and run:

```bash
dotnet build
dotnet run
```

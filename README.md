# Ollama Code Completions for Visual Studio

[![VS Marketplace Version](https://vsmarketplacebadges.dev/version-short/profanterdev.OllamaCodeCompletions.png?label=VS%20Marketplace&color=blue)](https://marketplace.visualstudio.com/items?itemName=profanterdev.OllamaCodeCompletions) [![Installs](https://vsmarketplacebadges.dev/installs-short/profanterdev.OllamaCodeCompletions.png?label=installs&color=brightgreen)](https://marketplace.visualstudio.com/items?itemName=profanterdev.OllamaCodeCompletions) [![Rating](https://vsmarketplacebadges.dev/rating-short/profanterdev.OllamaCodeCompletions.png?label=rating)](https://marketplace.visualstudio.com/items?itemName=profanterdev.OllamaCodeCompletions) [![Build](https://img.shields.io/github/actions/workflow/status/profanter-dev/ollama-code-completions/build.yml?label=build)](https://github.com/profanter-dev/ollama-code-completions/actions) [![License](https://img.shields.io/badge/license-MIT-lightgrey)](https://github.com/profanter-dev/ollama-code-completions/blob/main/LICENSE)

**Also available for** [Visual Studio Code](https://marketplace.visualstudio.com/items?itemName=profanterdev.ollama-code-completions)

---

Inline ghost-text autocomplete for **Visual Studio 2022 and Visual Studio 2026**, powered by your own self-hosted [Ollama](https://ollama.com/) instance or llama.cpp [`llama-server`](https://github.com/ggml-org/llama.cpp/tree/master/tools/server).

It debounces your typing, sends the code before _and_ after the cursor to the server using **fill-in-the-middle (FIM)** prompting, and renders the result as inline ghost text. **Tab** to accept, **Esc** to dismiss. Credentials for your server (HTTP Basic auth or a llama-server API key) are stored in the **Windows Credential Manager**, never in plaintext settings.

```
def quicksort(arr):
    if len(arr) <= 1:
        return arr
    pivot = arr[len(arr) // 2]
    █ left = [x for x in arr if x < pivot]    ← ghost text (Tab to accept)
        middle = [x for x in arr if x == pivot]
        right = [x for x in arr if x > pivot]
        return quicksort(left) + middle + quicksort(right)
```

---

## Features

- **FIM autocomplete** — uses Ollama's native `suffix` parameter, so any FIM-capable code model works out of the box (qwen2.5-coder, codellama, codegemma, deepseek-coder, starcoder2, …).
- **Ollama or llama-server** — talks to Ollama (`/api/generate`) or llama.cpp's `llama-server` (`/infill`); pick the backend in the options.
- **Debounced** — ~300 ms by default; in-flight requests are cancelled the moment you keep typing.
- **Instant cache replay** — backspacing over a recent suggestion or typing the first character of one re-shows it immediately from an in-memory LRU cache, with no network call and no debounce delay.
- **File-path prompt header** — the file's solution-relative path is prepended as a comment (`// File: src/Services/UserService.cs`) so the model knows the language and can pick up project conventions from directory names.
- **Output post-processing** — raw FIM output is cleaned before display: leading newlines on fresh lines are stripped, mid-line completions are truncated to one line, suffix overlap with existing buffer text is removed, unbalanced brackets are truncated, echoed prefix text is stripped, and a configurable line cap is applied.
- **Smart multi-line control** — a built-in classifier decides whether to request a single-line or multi-line completion based on the context (comment, block opener, blank line, etc.). Overridable via the _Multi-line completions_ setting.
- **Inline ghost text** — multi-line suggestions render in a faded color directly in the editor, aligned with your code's font.
- **Tab to accept, Esc to dismiss** — only intercepted when a suggestion is actually showing.
- **HTTP Basic auth** — for self-hosted instances behind a reverse proxy (nginx, Caddy, Traefik, Cloudflare Access).
- **llama-server API key** — sent as `Authorization: Bearer <key>` for servers started with `--api-key`.
- **Secure credential storage** — username/password stored in the Windows Credential Manager under target `OllamaCopilot:Auth`, the API key under `OllamaCopilot:ApiKey`.
- **Status bar feedback** — shows `Ollama Code Completions: thinking…` while a request is in flight.
- **Crash-safe** — connection failures, timeouts, and bad config never throw out of the extension.

---

## Installation

### From the prebuilt VSIX

1. Build the project (see below) or download a release.
2. Double-click `OllamaCodeCompletions.vsix`.
3. Pick the VS instances you want to install into (VS 2022 and/or VS 2026).
4. Restart Visual Studio.

### Building from source

Prerequisites:

- Visual Studio 2022 (17.x) or VS 2026 with the **Visual Studio extension development** workload.
- .NET Framework 4.7.2 targeting pack.

Then:

```
git clone <this-repo>
cd OllamaCopilot
dotnet restore OllamaCodeCompletions.sln
msbuild OllamaCodeCompletions.sln /p:Configuration=Release
```

The VSIX will land at `bin/Release/OllamaCodeCompletions.vsix`.

> **Note on VS 2026:** A single `[17.0,)` install target covers both VS 2022 and VS 2026. VS 2026 introduced an API-version-based compatibility model, so VS 2022 VSIXes load unchanged when they target supported APIs — which this extension does.

---

## Configuration

Open **Tools → Options → Ollama Code Completions → General**.

| Category       | Setting                       | Default                  | Notes                                                               |
| -------------- | ----------------------------- | ------------------------ | ------------------------------------------------------------------- |
| Connection     | Server backend                | `Ollama`                 | `Ollama` or `LlamaServer` (llama.cpp `llama-server`).               |
| Connection     | Server URL                    | `http://localhost:11434` | Base URL only — no trailing `/api/generate` or `/infill`.           |
| Connection     | Model                         | `qwen2.5-coder:1.5b`     | Any FIM-capable Ollama model tag. Optional for a single-model llama-server, which ignores it. |
| Authentication | Use HTTP Basic authentication | `false`                  | When on, sends `Authorization: Basic <base64>` on every request.    |
| Authentication | Username                      | _(empty)_                | Stored in Windows Credential Manager.                               |
| Authentication | Password                      | _(empty)_                | Stored in Windows Credential Manager. Shown as `********` once set. |
| Authentication | API key                       | _(empty)_                | llama-server only: sent as `Authorization: Bearer <key>` when Basic auth is off. Stored in Windows Credential Manager. |
| Behavior       | Enabled                       | `true`                   | Global kill-switch.                                                 |
| Behavior       | Debounce delay (ms)           | `300`                    | Idle time after the last keystroke before a request fires.          |
| Behavior       | Multi-line completions        | `Auto`                   | `Auto` chooses single-line for comments and string values, multi-line for block openers and blank lines, default single-line otherwise. `Always` never restricts. `Never` always single-line. |
| Behavior       | Max completion lines          | `6`                      | Hard ceiling on lines per suggestion, applied after all other filters. Only meaningful when multi-line is allowed. |
| Behavior       | Max prefix characters         | `4096`                   | Context before the cursor.                                          |
| Behavior       | Max suffix characters         | `1024`                   | Context after the cursor.                                           |
| Behavior       | Max tokens to predict         | `128`                    | Hard ceiling per suggestion (`num_predict` / `n_predict`).          |
| Behavior       | Request timeout (seconds)     | `30`                     | Per-request HTTP timeout.                                           |

### Recommended models

For the qwen2.5-coder family (which the defaults assume):

```
ollama pull qwen2.5-coder:1.5b   # very fast, fine for completions
ollama pull qwen2.5-coder:7b     # noticeably better, needs ~5 GB VRAM
ollama pull qwen2.5-coder:14b    # great quality if you have the hardware
```

Other solid options: `codellama:7b-code`, `deepseek-coder:6.7b-base`, `codegemma:2b-code`, `starcoder2:7b`.

> Make sure to use the **base / code** variant (not `-instruct`) for FIM completion — instruct-tuned models tend to wrap completions in prose.

---

## Self-hosting Ollama with Basic Auth

A typical setup behind nginx:

```nginx
server {
    listen 443 ssl http2;
    server_name ollama.yourdomain.com;

    ssl_certificate     /etc/letsencrypt/live/ollama.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/ollama.yourdomain.com/privkey.pem;

    auth_basic           "Ollama";
    auth_basic_user_file /etc/nginx/.htpasswd;

    location / {
        proxy_pass http://127.0.0.1:11434;
        proxy_set_header Host $host;
        proxy_buffering off;
        proxy_read_timeout 300s;
    }
}
```

Then in Tools → Options:

- **Server URL**: `https://ollama.yourdomain.com`
- **Use HTTP Basic authentication**: ✓
- **Username** / **Password**: your htpasswd creds

---

## Using llama-server

The extension can talk to llama.cpp's `llama-server` instead of Ollama. It uses the server's `/infill` endpoint, which applies the FIM tokens of the loaded model. For example, with the built-in Qwen 2.5 Coder preset:

```
llama-server --fim-qwen-1.5b-default --api-key <your-key>
```

The preset listens on port 8012. Without a preset, the server listens on whatever `--port` you start it with. Then in Tools → Options:

- **Server backend**: `LlamaServer`
- **Server URL**: `http://localhost:8012` (match the server's port)
- **Model**: anything — a single-model server ignores it. In router mode (several models), pick one with **Refresh**.
- **API key**: the `--api-key` value, if you set one. llama-server expects it as a Bearer token; it does not accept HTTP Basic auth. Use **Use HTTP Basic authentication** only when a reverse proxy in front of the server checks the credentials.

**Test connection** checks the model list (`/v1/models`) and then sends a one-token `/infill` request, so a wrong API key or a model without FIM support is reported right away.

> The loaded model must have FIM tokens (a code model such as qwen2.5-coder, codellama or deepseek-coder). Chat-only models are rejected by `/infill` with HTTP 501.

---

## Usage

1. Start typing in any editor window for a supported content type (most code files qualify).
2. Pause for ~300 ms.
3. Ghost text appears inline.
4. **Tab** to insert it, **Esc** (or just keep typing) to dismiss.

The extension is a no-op if:

- **Enabled** is off in settings, or
- the cursor is in an empty file with no surrounding context, or
- the server is unreachable (you'll see `Ollama Code Completions: <error>` in the status bar; the IDE keeps working normally).

---

## How it works

```
keystroke
   ↓
TextBuffer.Changed                   ← in SuggestionSession
   ↓ cancel in-flight CTS
cache hit? ──yes──→ render immediately (no debounce, no network)
   ↓ miss
Task.Delay(debounceMs, ct)           ← cancellable wait
   ↓
snapshot prefix + suffix around caret
prepend "// File: <solution-relative-path>\n" header
   ↓
cache hit (second check)? ──yes──→ render
   ↓ miss
POST {url}/api/generate              ← Ollama, `suffix` for native FIM
  or {url}/infill                    ← llama-server, `input_prefix` / `input_suffix`
   ↓
CompletionPostProcessor.Clean()      ← strip noise from raw FIM output
store cleaned completion in LRU cache
   ↓
re-validate caret hasn't moved
   ↓
add WPF Canvas of TextBlocks to a custom adornment layer
```

Tab / Esc are handled by an `IOleCommandTarget` (`CommandFilter`) that's chained into the legacy command pipeline of each `IVsTextView`. It only steals those keys when a suggestion is actually visible — otherwise commands fall straight through to the editor.

### File map

```
OllamaCopilot/
├── OllamaCodeCompletions.csproj       SDK-style net472 VSIX project
├── OllamaCodeCompletions.sln
├── source.extension.vsixmanifest      install targets + assets
├── OllamaCodeCompletionsPackage.cs    AsyncPackage, registers OptionsPage
│
├── Editor/
│   ├── TextViewListener.cs            MEF: defines adornment layer, attaches sessions
│   ├── SuggestionSession.cs           Per-view: debounce, cache, request, render, accept
│   ├── CommandFilter.cs               Tab/Esc interception
│   ├── CommandFilterProvider.cs       MEF: chains the filter into IVsTextView
│   ├── GhostTextLineTransformSource.cs  Extra vertical space for multi-line ghost text
│   ├── CompletionPostProcessor.cs     Cleans raw FIM output (8-stage pipeline, incl. line cap)
│   ├── MultilineDecider.cs            Classifies context → single-line or multi-line decision
│   └── CompletionCache.cs             Per-view bounded LRU cache (prefix, suffix) → completion
│
├── Ollama/
│   ├── CompletionClient.cs            Abstract base: HTTP, timeouts, auth, test connection
│   ├── OllamaClient.cs                Ollama: /api/generate + /api/tags
│   └── LlamaServerClient.cs           llama-server: /infill + /v1/models, Bearer API key
│
├── Settings/
│   ├── OptionsPage.cs                 UIElementDialogPage (persisted properties)
│   ├── OptionsPageControl.xaml        WPF Options UI (custom layout)
│   ├── OptionsPageControl.xaml.cs     Refresh / Test connection / Open log handlers
│   └── CredentialStorage.cs           Windows Credential Manager P/Invoke
│
├── Infrastructure/
│   ├── Logger.cs                      Diagnostic logger (file + Output pane sinks)
│   ├── StatusBar.cs                   Status bar helper
│   └── FileHeaderBuilder.cs           Builds the "// File: …" prompt header
│
├── Tests/
│   ├── CompletionPostProcessor.Tests.cs
│   ├── CompletionCache.Tests.cs
│   └── FileHeaderBuilder.Tests.cs
│
└── README.md                          this file
```

---

## Troubleshooting

**No suggestions appear.** Verify Ollama is reachable: `curl http://your-server/api/tags`. Then confirm the model is FIM-capable and pulled. Check the status bar for an error.

**No suggestions with llama-server.** Check the server answers: `curl -H "Authorization: Bearer <key>" http://your-server/v1/models`. Use **Test connection** — `HTTP 501` / "does not support fill-in-the-middle" means the loaded model has no FIM tokens. With **Log to file** on, failed requests log the server's error message.

**Suggestions appear but always look like prose / explanations.** You're using an instruct-tuned model. Switch to the base or `:code` variant — for example `qwen2.5-coder:7b-base` or `codellama:7b-code`.

**Auth always fails.** Open _Credential Manager_ (Windows) → _Windows Credentials_ and look for `OllamaCopilot:Auth`. You can delete it there to reset. Re-enter the password in Tools → Options. The llama-server API key lives under `OllamaCopilot:ApiKey`.

**Tab never inserts the suggestion.** Another extension may be eating Tab earlier in the command chain. Try briefly disabling other AI / IntelliSense extensions to isolate.

**Ghost text is misaligned.** Caused by mid-line completions when the line already has trailing text — a known limitation; accept the suggestion to see the real result.

---

## License

[MIT](LICENSE)

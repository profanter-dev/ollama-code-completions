using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OllamaCodeCompletions
{
    /// <summary>
    /// Base class for the fill-in-the-middle server clients. Owns the shared HTTP plumbing
    /// (timeouts, cancellation, auth, logging, sentinel stripping); subclasses supply the
    /// endpoint paths, payload shape and response parsing of their server API.
    /// </summary>
    internal abstract class CompletionClient
    {
        protected static readonly HttpClient s_http = new HttpClient
        {
            // Per-request timeouts are still enforced via the CancellationToken; this is just a
            // hard upper bound to prevent stuck connections from leaking forever.
            Timeout = TimeSpan.FromMinutes(2),
        };

        /// <summary>Max characters of an error response body written to the log.</summary>
        private const int MaxLoggedErrorBody = 300;

        public sealed class CompletionRequest
        {
            public string ServerUrl { get; set; }
            public string Model { get; set; }
            public string Prefix { get; set; }
            public string Suffix { get; set; }
            public int MaxPredict { get; set; } = 128;
            public int TimeoutSeconds { get; set; } = 30;
            public bool UseAuth { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string ApiKey { get; set; }
            public IReadOnlyList<string> StopTokens { get; set; }
        }

        /// <summary>Result of <see cref="TestConnectionAsync"/>.</summary>
        public sealed class TestConnectionResult
        {
            public bool Success { get; }
            public string Message { get; }
            public TestConnectionResult(bool success, string message)
            {
                Success = success;
                Message = message ?? string.Empty;
            }
        }

        /// <summary>Returns the client for the given server backend.</summary>
        public static CompletionClient Create(ServerBackend backend)
        {
            switch (backend)
            {
                case ServerBackend.LlamaServer: return new LlamaServerClient();
                default: return new OllamaClient();
            }
        }

        // ---- Backend-specific API surface ----

        /// <summary>Path of the completion endpoint, e.g. <c>/api/generate</c>.</summary>
        protected abstract string CompletionPath { get; }

        /// <summary>JSON field of the completion response that holds the generated text.</summary>
        protected abstract string CompletionResponseField { get; }

        /// <summary>Path of the model-list endpoint, e.g. <c>/api/tags</c>.</summary>
        protected abstract string ModelsPath { get; }

        /// <summary>JSON field of the model-list response that holds the model array.</summary>
        protected abstract string ModelsField { get; }

        /// <summary>Whether a completion request without a model name is pointless.</summary>
        protected virtual bool RequiresModel => true;

        protected abstract JObject BuildCompletionPayload(CompletionRequest req, JArray stopArray);

        /// <summary>
        /// Extracts the model names from a model-list response, or null if
        /// <see cref="ModelsField"/> is absent.
        /// </summary>
        protected abstract List<string> ParseModelNames(JObject parsed);

        /// <summary>Adds an <c>Authorization: Basic …</c> header when auth is enabled.</summary>
        protected virtual void ApplyAuth(HttpRequestMessage http, bool useAuth, string username, string password, string apiKey)
        {
            if (!useAuth) return;
            string raw = (username ?? string.Empty) + ":" + (password ?? string.Empty);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            http.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
        }

        // ---- Completion ----

        /// <summary>
        /// Returns the suggested completion text, or null if the request failed,
        /// was cancelled, or the server returned an empty response. Never throws.
        /// </summary>
        public async Task<string> GetCompletionAsync(CompletionRequest req, CancellationToken ct)
        {
            if (req == null) return null;
            if (string.IsNullOrWhiteSpace(req.ServerUrl)) return null;
            if (RequiresModel && string.IsNullOrWhiteSpace(req.Model)) return null;

            string url = req.ServerUrl.TrimEnd('/') + CompletionPath;

            // Build the stop-token array: always include the standard FIM/EOT sentinels,
            // then append any caller-supplied tokens (e.g. "\n" for single-line mode).
            var stopArray = new JArray
            {
                "<|endoftext|>", "<|fim_pad|>", "<|file_sep|>", "<EOT>",
                "<|im_end|>", "<|im_start|>"
            };
            if (req.StopTokens != null)
            {
                foreach (string token in req.StopTokens)
                    stopArray.Add(token);
            }

            JObject payload = BuildCompletionPayload(req, stopArray);

            // Per-request linked timeout so a hung server doesn't pin a suggestion forever.
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, req.TimeoutSeconds))))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
            using (var http = new HttpRequestMessage(HttpMethod.Post, url))
            {
                http.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                ApplyAuth(http, req.UseAuth, req.Username, req.Password, req.ApiKey);

                Logger.Log("Http", $"POST {url} model={req.Model} predict={req.MaxPredict} timeout={req.TimeoutSeconds}s");
                var sw = Stopwatch.StartNew();
                try
                {
                    using (var resp = await s_http.SendAsync(http, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false))
                    {
                        sw.Stop();
                        Logger.Log("Http", $"{(int)resp.StatusCode} in {sw.ElapsedMilliseconds}ms");

                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            // The body usually says why (bad key, model without FIM support, …).
                            Logger.Log("Http", $"HTTP {(int)resp.StatusCode}: {Truncate(body)}");
                            return null;
                        }

                        var parsed = JObject.Parse(body);
                        string completion = (string)parsed[CompletionResponseField];
                        if (completion == null)
                        {
                            Logger.Log("Http", $"{CompletionResponseField} field missing or non-string");
                            completion = string.Empty;
                        }
                        return PostProcess(completion);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // User kept typing — propagate so the session can stay quiet.
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // Linked token cancelled by timeoutCts — request exceeded TimeoutSeconds.
                    Logger.Log("Http", $"timeout after {req.TimeoutSeconds}s");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogException("Http", ex);
                    return null;
                }
                catch (JsonReaderException ex)
                {
                    Logger.LogException("Http", ex);
                    return null;
                }
            }
        }

        // ---- Server discovery + diagnostics (used by the Options page UI) ----

        /// <summary>
        /// Returns the alphabetically-sorted list of model names available on the
        /// configured server. Throws on any failure — the caller is expected to
        /// surface the message to the user.
        /// </summary>
        public async Task<List<string>> ListModelsAsync(
            string serverUrl, bool useAuth, string username, string password, string apiKey, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
                throw new ArgumentException("Server URL is required.", nameof(serverUrl));

            string url = serverUrl.TrimEnd('/') + ModelsPath;

            using (var http = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ApplyAuth(http, useAuth, username, password, apiKey);
                Logger.Log("Http", $"GET {url}");
                var sw = Stopwatch.StartNew();
                using (var resp = await s_http.SendAsync(http, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                {
                    sw.Stop();
                    Logger.Log("Http", $"{(int)resp.StatusCode} in {sw.ElapsedMilliseconds}ms");

                    if (!resp.IsSuccessStatusCode)
                        throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var result = ParseModelNames(JObject.Parse(body));
                    if (result == null)
                        throw new HttpRequestException($"Unexpected response shape (no '{ModelsField}' field).");
                    result.Sort(StringComparer.OrdinalIgnoreCase);
                    return result;
                }
            }
        }

        /// <summary>
        /// GETs the model list to verify reachability + auth, then checks the configured
        /// model is on the server. Expected failures (HTTP, JSON, auth) come back as
        /// <see cref="TestConnectionResult"/> with <see cref="TestConnectionResult.Success"/>=false;
        /// truly unexpected exceptions propagate to the caller.
        /// </summary>
        public virtual async Task<TestConnectionResult> TestConnectionAsync(
            string serverUrl, string model, bool useAuth, string username, string password, string apiKey, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
                return new TestConnectionResult(false, "Server URL is empty.");

            string url = serverUrl.TrimEnd('/') + ModelsPath;

            using (var http = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ApplyAuth(http, useAuth, username, password, apiKey);
                Logger.Log("Http", $"GET {url} (test)");
                try
                {
                    using (var resp = await s_http.SendAsync(http, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        Logger.Log("Http", $"{(int)resp.StatusCode} (test)");
                        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                            return new TestConnectionResult(false, "Authentication failed (HTTP 401).");
                        if (!resp.IsSuccessStatusCode)
                            return new TestConnectionResult(false, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var models = ParseModelNames(JObject.Parse(body));
                        if (models == null)
                            return new TestConnectionResult(false, $"Unexpected response shape (no '{ModelsField}' field).");

                        return CheckModel(models, model);
                    }
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogException("Http", ex);
                    return new TestConnectionResult(false, ex.Message);
                }
                catch (JsonReaderException ex)
                {
                    Logger.LogException("Http", ex);
                    return new TestConnectionResult(false, $"Bad response: {ex.Message}");
                }
            }
        }

        /// <summary>Decides the test result once the server's model list is known.</summary>
        protected virtual TestConnectionResult CheckModel(List<string> models, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return new TestConnectionResult(true, $"{models.Count} model(s) available.");

            foreach (string name in models)
            {
                if (string.Equals(name, model, StringComparison.OrdinalIgnoreCase))
                    return new TestConnectionResult(true, $"Model '{model}' is available.");
            }
            return new TestConnectionResult(false, $"Connected, but model '{model}' is not on the server.");
        }

        /// <summary>Shortens a response body for log and status messages.</summary>
        protected static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= MaxLoggedErrorBody ? s : s.Substring(0, MaxLoggedErrorBody) + "…";
        }

        /// <summary>Strip stray FIM/EOT sentinels that occasionally leak through.</summary>
        private static string PostProcess(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string[] junk =
            {
                "<|endoftext|>", "<|fim_pad|>", "<|file_sep|>", "<EOT>",
                "<|fim_middle|>", "<|fim_prefix|>", "<|fim_suffix|>",
                "<|im_end|>", "<|im_start|>",
            };
            foreach (var j in junk)
            {
                int idx = s.IndexOf(j, StringComparison.Ordinal);
                if (idx >= 0) s = s.Substring(0, idx);
            }
            return s;
        }
    }
}

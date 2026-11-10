using System.Collections.Generic;
using System.Net;
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
    /// Client for llama.cpp's <c>llama-server</c>: fill-in-the-middle via <c>/infill</c>
    /// (<c>input_prefix</c>/<c>input_suffix</c>), models via the OpenAI-style <c>/v1/models</c>.
    /// The server applies the FIM tokens of the loaded model, and rejects models that have none.
    /// Authenticates with the <c>--api-key</c> as a Bearer token, or with HTTP Basic auth
    /// when the server sits behind a reverse proxy.
    /// </summary>
    internal sealed class LlamaServerClient : CompletionClient
    {
        protected override string CompletionPath => "/infill";
        protected override string CompletionResponseField => "content";
        protected override string ModelsPath => "/v1/models";
        protected override string ModelsField => "data";

        // A single-model llama-server ignores "model"; only router-mode servers use it.
        protected override bool RequiresModel => false;

        protected override JObject BuildCompletionPayload(CompletionRequest req, JArray stopArray)
        {
            var payload = new JObject
            {
                ["input_prefix"] = req.Prefix ?? string.Empty,
                ["input_suffix"] = req.Suffix ?? string.Empty,
                ["stream"] = false,
                ["cache_prompt"] = true,
                ["temperature"] = 0.2,
                ["top_p"] = 0.95,
                ["n_predict"] = req.MaxPredict,
                ["stop"] = stopArray
            };
            if (!string.IsNullOrWhiteSpace(req.Model))
                payload["model"] = req.Model;
            return payload;
        }

        /// <summary>Reads <c>data[].id</c>.</summary>
        protected override List<string> ParseModelNames(JObject parsed)
        {
            var list = parsed[ModelsField] as JArray;
            if (list == null) return null;
            var result = new List<string>();
            foreach (var m in list)
            {
                string name = (string)m["id"];
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// Basic auth when enabled (reverse proxy in front), otherwise the API key as
        /// <c>Authorization: Bearer …</c>, which is what <c>llama-server --api-key</c> expects.
        /// </summary>
        protected override void ApplyAuth(HttpRequestMessage http, bool useAuth, string username, string password, string apiKey)
        {
            if (useAuth)
                base.ApplyAuth(http, useAuth, username, password, apiKey);
            else if (!string.IsNullOrEmpty(apiKey))
                http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>A single-model server ignores the configured model, so a mismatch there is fine.</summary>
        protected override TestConnectionResult CheckModel(List<string> models, string model)
        {
            var result = base.CheckModel(models, model);
            if (!result.Success && models.Count == 1)
                return new TestConnectionResult(true, $"Server serves '{models[0]}' (configured model is ignored).");
            return result;
        }

        /// <summary>
        /// Runs the model-list check, then sends a one-token <c>/infill</c> request so a
        /// loaded model without FIM tokens is reported here instead of failing silently
        /// on every completion.
        /// </summary>
        public override async Task<TestConnectionResult> TestConnectionAsync(
            string serverUrl, string model, bool useAuth, string username, string password, string apiKey, CancellationToken ct)
        {
            var result = await base.TestConnectionAsync(serverUrl, model, useAuth, username, password, apiKey, ct).ConfigureAwait(false);
            if (!result.Success) return result;

            string url = serverUrl.TrimEnd('/') + CompletionPath;
            var payload = new JObject
            {
                ["input_prefix"] = string.Empty,
                ["input_suffix"] = string.Empty,
                ["stream"] = false,
                ["n_predict"] = 1,
            };
            if (!string.IsNullOrWhiteSpace(model))
                payload["model"] = model;

            using (var http = new HttpRequestMessage(HttpMethod.Post, url))
            {
                http.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                ApplyAuth(http, useAuth, username, password, apiKey);
                Logger.Log("Http", $"POST {url} (test)");
                try
                {
                    using (var resp = await s_http.SendAsync(http, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        Logger.Log("Http", $"{(int)resp.StatusCode} (test)");
                        if (resp.IsSuccessStatusCode)
                            return result;

                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Logger.Log("Http", $"HTTP {(int)resp.StatusCode}: {Truncate(body)}");
                        if (resp.StatusCode == HttpStatusCode.Unauthorized)
                            return new TestConnectionResult(false, "Authentication failed (HTTP 401). Check the API key.");
                        if (resp.StatusCode == HttpStatusCode.NotImplemented)
                            return new TestConnectionResult(false, "The loaded model does not support fill-in-the-middle (/infill).");
                        return new TestConnectionResult(false, $"/infill failed: HTTP {(int)resp.StatusCode} {Truncate(body)}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogException("Http", ex);
                    return new TestConnectionResult(false, ex.Message);
                }
            }
        }
    }
}

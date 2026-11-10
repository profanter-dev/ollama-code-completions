using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OllamaCodeCompletions
{
    /// <summary>
    /// Minimal client for Ollama's <c>/api/generate</c> endpoint, using its native
    /// <c>suffix</c> parameter for fill-in-the-middle prompting. The endpoint applies
    /// the right FIM tokens for whichever code model is configured (qwen2.5-coder,
    /// codellama, codegemma, deepseek-coder, …) so we don't have to hard-code them.
    /// Models are listed via <c>/api/tags</c>.
    /// </summary>
    internal sealed class OllamaClient : CompletionClient
    {
        protected override string CompletionPath => "/api/generate";
        protected override string CompletionResponseField => "response";
        protected override string ModelsPath => "/api/tags";
        protected override string ModelsField => "models";

        protected override JObject BuildCompletionPayload(CompletionRequest req, JArray stopArray)
        {
            return new JObject
            {
                ["model"] = req.Model,
                ["prompt"] = req.Prefix ?? string.Empty,
                ["suffix"] = req.Suffix ?? string.Empty,
                ["stream"] = false,
                ["options"] = new JObject
                {
                    ["temperature"] = 0.2,
                    ["top_p"] = 0.95,
                    ["num_predict"] = req.MaxPredict,
                    ["stop"] = stopArray
                }
            };
        }

        /// <summary>Reads <c>models[].name</c>.</summary>
        protected override List<string> ParseModelNames(JObject parsed)
        {
            var models = parsed[ModelsField] as JArray;
            if (models == null) return null;
            var result = new List<string>();
            foreach (var m in models)
            {
                string name = (string)m["name"];
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }
    }
}

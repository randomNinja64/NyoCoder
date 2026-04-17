using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace NyoCoder
{
    /// <summary>
    /// Presents the user with a question and a set of preset choices, plus a
    /// free-form "Other" text field. Inspired by mistral-vibe's
    /// ask_user_question built-in tool.
    /// </summary>
    internal static class AskUserQuestionTool
    {
        internal static string Ask(string question, JArray optionsArray, out int exitCode)
        {
            exitCode = 0;

            NyoCoderControl control = null;
            try { control = NyoCoder_VSIXPackage.Instance != null ? NyoCoder_VSIXPackage.Instance.ToolWindowControl : null; } catch { }

            if (control == null)
            {
                exitCode = 1;
                return "Error: NyoCoder tool window unavailable; cannot prompt the user.";
            }

            List<string> options = new List<string>();
            if (optionsArray != null)
            {
                foreach (JToken token in optionsArray)
                {
                    if (token == null || token.Type == JTokenType.Null)
                        continue;
                    string value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        options.Add(value);
                }
            }

            string answer = control.RequestUserQuestion(question, options.ToArray());
            if (string.IsNullOrEmpty(answer))
            {
                exitCode = 1;
                return "No answer received (user cancelled).";
            }
            return answer;
        }
    }
}

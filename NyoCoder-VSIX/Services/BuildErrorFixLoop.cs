using System;
using System.Threading;
using EnvDTE;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// After agent file edits, detects build errors and runs Debug-mode corrective turns.
    /// </summary>
    internal static class BuildErrorFixLoop
    {
        /// <summary>
        /// Runs the build-error detection and correction loop. Callers must only invoke this when
        /// files were modified during the completed turn (main turn, plan review, and plan execution).
        /// </summary>
        public static void RunIfNeeded(
            LLMClient llmClient,
            Func<bool> stopRequested,
            Action<string> appendText,
            Func<string> dequeueSteerMessage,
            Action<int> onSummarized,
            Action startBlock = null)
        {
            if (llmClient == null)
                return;

            if (startBlock == null)
                startBlock = delegate { };

            if (ConfigHandler.GetBuildErrorCheckMode() == BuildErrorCheckMode.Off)
                return;

            if (stopRequested != null && stopRequested())
                return;

            ContextEngine.CompilerErrorSnapshot snapshot;
            if (!TryDetectErrors(out snapshot, appendText, startBlock))
                return;

            if (snapshot.Count == 0)
                return;

            string previousSignature = snapshot.Signature;
            int unchangedStreak = 0;
            int maxAttempts = ConfigHandler.GetBuildErrorFixMaxAttempts();
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                if (stopRequested != null && stopRequested())
                    return;

                attempts++;
                startBlock();
                AppendLine(appendText, "Assistant: ");

                string fixPrompt =
                    "The following build errors were reported after your previous changes. " +
                    "Fix them with minimal, targeted edits. Do not refactor unrelated code.\n\n" +
                    snapshot.FormattedText;

                llmClient.ProcessConversation(
                    fixPrompt,
                    null,
                    ConfigHandler.GetShowToolOutput(),
                    appendText,
                    ToolApprovalService.Request,
                    stopRequested: stopRequested,
                    onSummarized: onSummarized,
                    mode: ChatMode.Debug,
                    dequeueSteerMessage: dequeueSteerMessage,
                    startBlock: startBlock);

                if (stopRequested != null && stopRequested())
                    return;

                if (!TryDetectErrors(out snapshot, appendText, startBlock))
                    return;

                if (snapshot.Count == 0)
                    return;

                if (string.Equals(snapshot.Signature, previousSignature, StringComparison.Ordinal))
                {
                    unchangedStreak++;
                    if (unchangedStreak >= 2)
                    {
                        startBlock();
                        AppendLine(appendText, "[Unable to resolve build errors: no progress after 2 attempts]");
                        return;
                    }
                }
                else
                {
                    unchangedStreak = 0;
                    previousSignature = snapshot.Signature;
                }
            }

            startBlock();
            AppendLine(appendText, "[Unable to resolve build errors: max attempts reached]");
        }

        private static bool TryDetectErrors(
            out ContextEngine.CompilerErrorSnapshot snapshot,
            Action<string> appendText,
            Action startBlock)
        {
            snapshot = new ContextEngine.CompilerErrorSnapshot();
            BuildErrorCheckMode mode = ConfigHandler.GetBuildErrorCheckMode();

            startBlock();
            AppendLine(appendText, "Checking for build errors...");

            if (mode == BuildErrorCheckMode.IntelliSense)
            {
                System.Threading.Thread.Sleep(ConfigHandler.GetBuildErrorCheckWaitSeconds() * 1000);
            }
            else if (mode == BuildErrorCheckMode.BuildSolution)
            {
                string buildMessage;
                if (!TryBuildSolution(out buildMessage))
                {
                    if (!string.IsNullOrEmpty(buildMessage))
                        AppendLine(appendText, buildMessage);
                    return false;
                }
            }

            ContextEngine.CompilerErrorSnapshot collected = new ContextEngine.CompilerErrorSnapshot();

            EditorService.InvokeOnUIThread(() =>
            {
                DTE2 dte = EditorService.GetDte();
                if (dte == null) return;
                ContextEngine engine = new ContextEngine(dte);
                engine.TryCollectCompilerErrors(null, out collected);
            });

            snapshot = collected;

            AppendLine(appendText, snapshot.Count == 0
                ? "No errors found."
                : snapshot.Count + " errors found.");
            return true;
        }

        private static bool TryBuildSolution(out string message)
        {
            message = null;
            bool? buildOk = null;
            bool solutionAvailable = false;

            EditorService.InvokeOnUIThread(() =>
            {
                try
                {
                    DTE2 dte = EditorService.GetDte();
                    if (dte == null || dte.Solution == null)
                        return;

                    if (string.IsNullOrEmpty(dte.Solution.FullName))
                        return;

                    solutionAvailable = true;
                    SolutionBuild solutionBuild = dte.Solution.SolutionBuild;
                    if (solutionBuild == null)
                        return;

                    solutionBuild.Build(true);
                    buildOk = true;
                }
                catch
                {
                    buildOk = false;
                }
            });

            if (!solutionAvailable)
            {
                message = "[Build check skipped: no solution loaded]";
                return false;
            }

            if (!buildOk.HasValue)
            {
                message = "[Build check skipped: build unavailable]";
                return false;
            }

            return true;
        }

        private static void AppendLine(Action<string> appendText, string line)
        {
            if (appendText == null) return;
            appendText(line + "\n");
        }
    }
}

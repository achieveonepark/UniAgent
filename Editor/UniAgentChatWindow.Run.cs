using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Achieve.UniAgent;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Achieve.UniAgent.Editor
{
    /// <summary>
    /// 프롬프트 전송, CLI 실행 결과 처리, Diff Preview 연동, Unity Action Bridge 적용을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void SendCurrentInput()
        {
            if (_isBusy)
            {
                return;
            }

            if (!_codexInstalled)
            {
                var missingCli = _selectedProvider == "Claude Code" ? "Claude Code CLI" : "Codex CLI";
                AddMessage(ChatRole.Error, $"{missingCli} is not installed. Install it, then click Refresh in Settings.");
                return;
            }

            if (!_codexLoggedIn)
            {
                var loginCmd = _selectedProvider == "Claude Code" ? "claude auth login" : "codex login --device-auth";
                AddMessage(ChatRole.Error, $"Login is required. Click Login in Settings or run `{loginCmd}` in terminal.");
                return;
            }

            var text = _inputField.value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            HideMentionSuggestions();
            HideNewSessionPopup();
            _inputField.value = string.Empty;
            AddMessage(ChatRole.User, text);
            StartPendingAssistantMessage();

            var diffPreviewThisTurn = _chatMode == ChatMode.Build && _buildDiffPreviewMode;
            var prompt = diffPreviewThisTurn ? BuildDiffPreviewPrompt(text) : BuildPrompt(text);
            var agentName = GetProviderDisplayName();
            SetBusy(true, diffPreviewThisTurn ? $"{agentName} is generating diff preview..." : $"{agentName} is thinking...");
            IncrementActiveRuns();

            RunThroughUniAgentClient(prompt, diffPreviewThisTurn ? false : (bool?)null).ContinueWith(task =>
            {
                var result = task.IsFaulted
                    ? UniAgentRunResult.FromError(task.Exception?.GetBaseException().Message ?? "Unknown execution error")
                    : task.Result;
                DecrementActiveRuns();

                EditorApplication.delayCall += () => DispatchRunResult(result, diffPreviewThisTurn);
            });
        }

        private string BuildPrompt(string userText)
        {
            var targetedFiles = ResolveTargetedFilesFromMentions(userText);
            var sb = new StringBuilder();
            sb.AppendLine($"Chat mode: {_chatMode}");
            if (_chatMode == ChatMode.Plan)
            {
                sb.AppendLine("Mode instruction: Focus on analysis and step-by-step planning. Do not assume code edits are already applied.");
            }
            else
            {
                sb.AppendLine("Mode instruction: Focus on implementation details, concrete changes, and verification steps.");
            }

            var includeMarkdownContext = string.IsNullOrWhiteSpace(_sessionId) && targetedFiles.Count == 0;
            if (targetedFiles.Count > 0)
            {
                sb.AppendLine($"Prompt mode: Targeted file turn ({targetedFiles.Count} file(s) from @mentions).");
            }
            if (!includeMarkdownContext)
            {
                sb.AppendLine("Prompt mode: Compact follow-up turn (reduced context for faster response).");
            }

            if (!string.IsNullOrWhiteSpace(_pendingCompactedContext))
            {
                sb.AppendLine();
                sb.AppendLine("Prior conversation summary (auto-compacted because the session neared its token budget):");
                sb.AppendLine(_pendingCompactedContext);
                _pendingCompactedContext = string.Empty;
            }

            sb.AppendLine();
            sb.Append(UniAgentChatHelper.BuildPrompt(
                userText,
                _markdownFiles,
                _maxMarkdownChars,
                includeMarkdownContext,
                targetedFiles,
                MaxTargetedFileChars));
            return sb.ToString();
        }

        private void EnsurePendingAssistantForActiveRun()
        {
            if (!HasActiveRuns())
            {
                return;
            }

            var loading = FindLatestLoadingMessage();
            if (loading != null)
            {
                StartPendingAssistantMessage(loading);
                return;
            }

            StartPendingAssistantMessage(null);
        }

        private string BuildDiffPreviewPrompt(string userText)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Build diff preview mode:");
            sb.AppendLine("- Do not apply or write any files.");
            sb.AppendLine("- Do not write Unity action bridge JSON.");
            sb.AppendLine("- Propose all required code changes as unified git diff.");
            sb.AppendLine("- Include every file that should change.");
            sb.AppendLine("- Response format:");
            sb.AppendLine("  1) Short refactor spec (what changed / why / risk or check points).");
            sb.AppendLine("  2) One ```diff fenced block with the full patch.");
            sb.AppendLine("- If no changes are needed, respond exactly with NO_CHANGES.");
            sb.AppendLine();
            sb.Append(BuildPrompt(userText));
            return sb.ToString();
        }

        private void HandleCodexResult(UniAgentRunResult result, bool diffPreviewTurn)
        {
            if (!string.IsNullOrWhiteSpace(result.ThreadId))
            {
                _sessionId = result.ThreadId;
                SavePrefs();
            }

            var tokenSummary = UniAgentCliService.BuildTokenSummary(result);
            if (!string.IsNullOrWhiteSpace(tokenSummary))
            {
                _lastTokenUsageText = tokenSummary;
            }

            AccumulateSessionTokens(result);
            if (!diffPreviewTurn)
            {
                ApplyPendingUnityActionsFromBridge();
            }

            if (result.Success)
            {
                var finalText = string.IsNullOrWhiteSpace(result.Message) ? "(Empty response)" : result.Message;
                if (diffPreviewTurn)
                {
                    ShowDiffPreviewWindow(finalText);
                    var diffSummaryMessage = BuildDiffPreviewAssistantMessage(finalText);
                    CompletePendingAssistantMessage(diffSummaryMessage, ChatRole.Assistant, tokenSummary);
                }
                else
                {
                    CompletePendingAssistantMessage(finalText, ChatRole.Assistant, tokenSummary);
                }

                AutoCompactSessionIfNeeded();
                SetBusy(false, $"Ready (turn tok: {FormatTokenCount(ComputeTurnTokenCost(result))})");
                return;
            }

            var runProviderName = GetProviderDisplayName();
            var errorText = string.IsNullOrWhiteSpace(result.Message) ? $"{runProviderName} execution failed." : result.Message;
            CompletePendingAssistantMessage(errorText, ChatRole.Error, tokenSummary);
            SetBusy(false, $"{runProviderName} execution failed");
        }

        /// <summary>
        /// 세션 토큰 사용량이 예산의 <see cref="AutoCompactThresholdRatio"/>를 넘으면 대화 이력을 요약하고
        /// CLI 스레드를 새로 시작한다. 요약문은 <see cref="_pendingCompactedContext"/>에 저장되어
        /// 다음 턴의 프롬프트에 한 번만 삽입된다.
        /// </summary>
        private void AutoCompactSessionIfNeeded()
        {
            if (_sessionTokenBudget <= 0 || string.IsNullOrWhiteSpace(_sessionId))
            {
                return;
            }

            if (_sessionTokenUsed < _sessionTokenBudget * AutoCompactThresholdRatio)
            {
                return;
            }

            var summary = BuildCompactionSummary();
            var previousUsed = _sessionTokenUsed;
            _pendingCompactedContext = summary;
            _sessionId = string.Empty;
            _sessionTokenUsed = EstimateSummaryTokenCost(summary);
            _recentTurnTokenCosts.Clear();
            SavePrefs();
            UpdateTokenGaugeUI();

            AddMessage(
                ChatRole.System,
                $"Context auto-compacted at {FormatTokenCount(previousUsed)} tokens ({AutoCompactThresholdRatio:P0} of budget). " +
                $"Starting a fresh {GetProviderDisplayName()} thread seeded with a summary of the recent conversation.");
        }

        /// <summary>최근 대화 이력에서 사용자/어시스턴트 turn만 뽑아 글자 수 예산 안에서 요약 텍스트를 만든다.</summary>
        private string BuildCompactionSummary()
        {
            const int maxMessages = 20;
            const int charBudget = 6000;
            const int perMessageCap = 800;

            var sb = new StringBuilder();
            var usedChars = 0;
            var startIndex = Mathf.Max(0, _messages.Count - maxMessages);
            for (var i = startIndex; i < _messages.Count; i++)
            {
                var message = _messages[i];
                if (message == null || message.IsLoading || string.IsNullOrWhiteSpace(message.Text))
                {
                    continue;
                }

                if (message.Role != ChatRole.User && message.Role != ChatRole.Assistant)
                {
                    continue;
                }

                var line = message.Text.Length > perMessageCap
                    ? message.Text.Substring(0, perMessageCap) + "..."
                    : message.Text;
                var entry = $"[{message.Role}] {line}";
                if (usedChars + entry.Length > charBudget)
                {
                    break;
                }

                sb.AppendLine(entry);
                usedChars += entry.Length;
            }

            return sb.ToString().TrimEnd();
        }

        private static int EstimateSummaryTokenCost(string summary)
        {
            return string.IsNullOrWhiteSpace(summary) ? 0 : Mathf.Max(0, summary.Length / 4);
        }

        private static void DispatchRunResult(UniAgentRunResult result, bool diffPreviewTurn)
        {
            var window = TryGetAnyChatWindow();
            if (window != null)
            {
                window.HandleCodexResult(result, diffPreviewTurn);
                return;
            }

            lock (DeferredRunLock)
            {
                DeferredRunResults.Enqueue(new UniAgentDeferredRunResult
                {
                    Result = result,
                    DiffPreviewTurn = diffPreviewTurn
                });
            }

            if (HasActiveRuns())
            {
                UniAgentToolbarShortcut.SetBusyState();
            }
            else
            {
                UniAgentToolbarShortcut.SetCompleteState();
            }
        }

        private void ApplyDeferredRunResults()
        {
            while (true)
            {
                UniAgentDeferredRunResult pending;
                lock (DeferredRunLock)
                {
                    if (DeferredRunResults.Count <= 0)
                    {
                        break;
                    }

                    pending = DeferredRunResults.Dequeue();
                }

                HandleCodexResult(pending.Result, pending.DiffPreviewTurn);
            }
        }

        private void ShowDiffPreviewWindow(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                UniAgentDiffPreviewWindow.ShowDiff("Diff Preview", "NO_CHANGES", RequestDiffRefinementAsync);
                return;
            }

            if (string.Equals(responseText.Trim(), "NO_CHANGES", StringComparison.OrdinalIgnoreCase))
            {
                UniAgentDiffPreviewWindow.ShowDiff("Diff Preview", "NO_CHANGES", RequestDiffRefinementAsync);
                return;
            }

            var diffText = ExtractUnifiedDiffBlock(responseText);
            if (string.IsNullOrWhiteSpace(diffText))
            {
                // Fallback: show raw response so user can still inspect what model produced.
                diffText = responseText;
            }

            var sessionLabel = _sessionPopup?.value;
            var title = string.IsNullOrWhiteSpace(sessionLabel) ? "Diff Preview" : $"Diff Preview - {sessionLabel}";
            UniAgentDiffPreviewWindow.ShowDiff(title, diffText, RequestDiffRefinementAsync);
        }

        private Task<UniAgentRunResult> RequestDiffRefinementAsync(string currentDiff, string refineRequest)
        {
            var prompt = BuildDiffRefinementPrompt(currentDiff, refineRequest);
            return RunThroughUniAgentClient(prompt, false).ContinueWith(task =>
            {
                var result = task.IsFaulted
                    ? UniAgentRunResult.FromError(task.Exception?.GetBaseException().Message ?? "Diff refine execution error")
                    : task.Result;

                EditorApplication.delayCall += () => ApplyCodexResultRuntimeState(result);
                return result;
            });
        }

        internal Task<UniAgentRunResult> RequestDiffRefinementFromPreview(string currentDiff, string refineRequest)
        {
            return RequestDiffRefinementAsync(currentDiff, refineRequest);
        }

        internal static Func<string, string, Task<UniAgentRunResult>> TryGetDiffRefineHandler()
        {
            var window = TryGetAnyChatWindow();
            return window == null ? null : window.RequestDiffRefinementFromPreview;
        }

        private static UniAgentChatWindow TryGetAnyChatWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<UniAgentChatWindow>();
            if (windows == null || windows.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                if (window != null)
                {
                    return window;
                }
            }

            return null;
        }

        private static void IncrementActiveRuns()
        {
            lock (ActiveRunCountLock)
            {
                ActiveRunCount++;
            }
        }

        private static void DecrementActiveRuns()
        {
            lock (ActiveRunCountLock)
            {
                if (ActiveRunCount > 0)
                {
                    ActiveRunCount--;
                }
            }
        }

        private static bool HasActiveRuns()
        {
            lock (ActiveRunCountLock)
            {
                return ActiveRunCount > 0;
            }
        }

        private static bool HasDeferredRunResults()
        {
            lock (DeferredRunLock)
            {
                return DeferredRunResults.Count > 0;
            }
        }

        private string BuildDiffRefinementPrompt(string currentDiff, string refineRequest)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Build diff refinement mode:");
            sb.AppendLine("- You are refining an existing unified diff before apply.");
            sb.AppendLine("- Return exactly one unified diff (prefer ```diff fenced block).");
            sb.AppendLine("- Keep valid patch structure (`---`, `+++`, `@@`).");
            sb.AppendLine("- Preserve untouched behavior unless explicitly requested.");
            sb.AppendLine("- If no changes are needed, respond exactly with NO_CHANGES.");
            sb.AppendLine();
            sb.AppendLine("Refine request:");
            sb.AppendLine(refineRequest ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("Current diff:");
            sb.AppendLine("```diff");
            sb.AppendLine(currentDiff ?? string.Empty);
            sb.AppendLine("```");
            return sb.ToString();
        }

        private void ApplyCodexResultRuntimeState(UniAgentRunResult result)
        {
            if (result == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.ThreadId))
            {
                _sessionId = result.ThreadId;
                SavePrefs();
            }

            var tokenSummary = UniAgentCliService.BuildTokenSummary(result);
            if (!string.IsNullOrWhiteSpace(tokenSummary))
            {
                _lastTokenUsageText = tokenSummary;
            }

            AccumulateSessionTokens(result);
        }

        private static string ExtractUnifiedDiffBlock(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            var fencedBlocks = Regex.Matches(responseText, "```(?:diff|patch)?\\s*\\n([\\s\\S]*?)```", RegexOptions.IgnoreCase);
            var collected = new StringBuilder();
            for (var i = 0; i < fencedBlocks.Count; i++)
            {
                var block = fencedBlocks[i].Groups.Count > 1 ? fencedBlocks[i].Groups[1].Value : string.Empty;
                if (LooksLikeUnifiedDiff(block))
                {
                    if (collected.Length > 0)
                    {
                        collected.Append('\n');
                    }

                    collected.Append(block.Trim());
                }
            }

            if (collected.Length > 0)
            {
                return collected.ToString();
            }

            return LooksLikeUnifiedDiff(responseText) ? responseText.Trim() : string.Empty;
        }

        private static string BuildDiffPreviewAssistantMessage(string responseText)
        {
            var trimmed = responseText?.Trim() ?? string.Empty;
            if (string.Equals(trimmed, "NO_CHANGES", StringComparison.OrdinalIgnoreCase))
            {
                return "No code changes were needed.\n\n`Diff Preview` window shows `NO_CHANGES`.";
            }

            var diffText = ExtractUnifiedDiffBlock(responseText);
            var narrative = ExtractDiffPreviewNarrative(responseText);
            var stats = BuildDiffQuickStats(diffText);

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(narrative))
            {
                sb.AppendLine(narrative.Trim());
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Diff preview generated.");
                sb.AppendLine();
            }

            sb.Append("Opened `Diff Preview` window");
            if (!string.IsNullOrWhiteSpace(stats))
            {
                sb.Append(" (");
                sb.Append(stats);
                sb.Append(")");
            }

            sb.Append(". ");
            sb.Append("Use file tabs to refine/apply one-by-one in the preview window.");
            return sb.ToString();
        }

        private static string ExtractDiffPreviewNarrative(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            var trimmed = responseText.Trim();
            if (string.Equals(trimmed, "NO_CHANGES", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // Preferred path: remove fenced diff/patch blocks and keep prose.
            var withoutFencedDiff = Regex.Replace(
                trimmed,
                "```(?:diff|patch)?\\s*\\n[\\s\\S]*?```",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(withoutFencedDiff))
            {
                return withoutFencedDiff;
            }

            // Fallback for unfenced diff output: keep prose before first diff marker.
            if (TryFindDiffStartIndex(trimmed, out var startIndex) && startIndex > 0)
            {
                var prefix = trimmed.Substring(0, startIndex).Trim();
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    return prefix;
                }
            }

            return string.Empty;
        }

        private static bool TryFindDiffStartIndex(string text, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            UpdateMinimumIndex(ref index, text.IndexOf("diff --git ", StringComparison.OrdinalIgnoreCase));
            UpdateMinimumIndex(ref index, text.IndexOf("\ndiff --git ", StringComparison.OrdinalIgnoreCase));
            UpdateMinimumIndex(ref index, text.IndexOf("\n--- ", StringComparison.Ordinal));
            UpdateMinimumIndex(ref index, text.IndexOf("--- ", StringComparison.Ordinal));
            UpdateMinimumIndex(ref index, text.IndexOf("\n@@ ", StringComparison.Ordinal));
            UpdateMinimumIndex(ref index, text.IndexOf("@@ ", StringComparison.Ordinal));

            return index >= 0;
        }

        private static void UpdateMinimumIndex(ref int index, int candidate)
        {
            if (candidate < 0)
            {
                return;
            }

            if (index < 0 || candidate < index)
            {
                index = candidate;
            }
        }

        private static string BuildDiffQuickStats(string diffText)
        {
            if (string.IsNullOrWhiteSpace(diffText))
            {
                return string.Empty;
            }

            var normalized = diffText.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var fileCount = 0;
            var added = 0;
            var removed = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("diff --git ", StringComparison.OrdinalIgnoreCase))
                {
                    fileCount++;
                    continue;
                }

                if (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("+", StringComparison.Ordinal))
                {
                    added++;
                    continue;
                }

                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    removed++;
                }
            }

            if (fileCount == 0)
            {
                for (var i = 0; i + 1 < lines.Length; i++)
                {
                    if (lines[i].StartsWith("--- ", StringComparison.Ordinal)
                        && lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
                    {
                        fileCount++;
                    }
                }
            }

            return $"files: {fileCount}, +{added} / -{removed}";
        }

        private static bool LooksLikeUnifiedDiff(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.IndexOf("diff --git ", StringComparison.OrdinalIgnoreCase) >= 0
                || ((text.StartsWith("--- ", StringComparison.Ordinal) || text.IndexOf("\n--- ", StringComparison.Ordinal) >= 0)
                    && (text.StartsWith("+++ ", StringComparison.Ordinal) || text.IndexOf("\n+++ ", StringComparison.Ordinal) >= 0))
                || text.StartsWith("@@ ", StringComparison.Ordinal)
                || text.IndexOf("\n@@ ", StringComparison.Ordinal) >= 0;
        }

        private void ApplyPendingUnityActionsFromBridge()
        {
            if (!UniAgentUnityEditorHelper.TryApplyPendingActions(out var summary))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                AddMessage(ChatRole.System, summary);
            }
        }

        // -------------------------
        // Unity editor controls
        // -------------------------

        private void ApplyEnterPlayModeSettings(bool addChatMessage)
        {
            var options = EnterPlayModeOptions.None;
            if (_disableDomainReloadOnPlay)
            {
                options |= EnterPlayModeOptions.DisableDomainReload;
            }

            if (_disableSceneReloadOnPlay)
            {
                options |= EnterPlayModeOptions.DisableSceneReload;
            }

            EditorSettings.enterPlayModeOptionsEnabled = options != EnterPlayModeOptions.None;
            EditorSettings.enterPlayModeOptions = options;

            if (addChatMessage)
            {
                AddMessage(ChatRole.System, $"EnterPlayMode applied: Enabled={EditorSettings.enterPlayModeOptionsEnabled}, Options={EditorSettings.enterPlayModeOptions}");
            }

            SetStatus("EnterPlayMode settings applied");
        }

        private void RefreshScriptsManually()
        {
            var shouldRelock = _autoRefreshLocked;
            if (shouldRelock)
            {
                SetAutoRefreshLock(false);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            CompilationPipeline.RequestScriptCompilation();

            if (shouldRelock)
            {
                EditorApplication.delayCall += () => SetAutoRefreshLock(true);
            }

            SetStatus("Refresh + script compilation requested");
        }

        private void SetAutoRefreshLock(bool shouldLock)
        {
            if (shouldLock == _autoRefreshLocked)
            {
                return;
            }

            try
            {
                if (shouldLock)
                {
                    AssetDatabase.DisallowAutoRefresh();
                    _autoRefreshLocked = true;
                    SetStatus("Auto refresh locked (manual refresh mode)");
                    return;
                }

                AssetDatabase.AllowAutoRefresh();
                _autoRefreshLocked = false;
                SetStatus("Auto refresh enabled");
            }
            catch (Exception ex)
            {
                AddMessage(ChatRole.Error, $"AutoRefresh lock change failed: {ex.Message}");
            }
        }

        private void ReleaseAutoRefreshLock()
        {
            if (!_autoRefreshLocked)
            {
                return;
            }

            try
            {
                AssetDatabase.AllowAutoRefresh();
            }
            catch
            {
                // Best-effort unlock on shutdown/reload.
            }
            finally
            {
                _autoRefreshLocked = false;
            }
        }

    }
}

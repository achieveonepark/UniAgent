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
    /// 세션 초기화/초기화, 진행중 메시지 애니메이션, 채팅 메시지 렌더링을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void ResetSession()
        {
            _sessionId = string.Empty;
            _sessionTokenUsed = 0;
            _recentTurnTokenCosts.Clear();
            _lastTokenUsageText = "-";
            SaveActiveSessionSnapshot();
            SaveChatHistory();
            SavePrefs();
            AddMessage(ChatRole.System, $"Session reset. Next message starts a new {GetProviderDisplayName()} thread.");
            UpdateTokenGaugeUI();
            UpdateStatusUI();
        }

        private void ClearChat()
        {
            _messages.Clear();
            _pendingAssistantMessage = null;
            StopPendingAssistantAnimation();
            _lastTokenUsageText = "-";
            _sessionTokenUsed = 0;
            _recentTurnTokenCosts.Clear();
            _sessionId = string.Empty;
            SaveActiveSessionSnapshot();
            SaveChatHistory();
            SavePrefs();
            UpdateTokenGaugeUI();
            RefreshChatUI();
            SetStatus("Chat cleared and session reset");
        }

        // -------------------------
        // Chat rendering
        // -------------------------

        private UniAgentChatMessage AddMessage(ChatRole role, string text, string tokenSummary = null, bool isLoading = false)
        {
            var message = new UniAgentChatMessage
            {
                Role = role,
                Text = text,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                TokenSummary = tokenSummary,
                IsLoading = isLoading
            };

            _messages.Add(message);
            if (!isLoading)
            {
                SaveChatHistory();
            }

            AddMessageToView(message);
            ScrollToBottom();
            return message;
        }

        private void StartPendingAssistantMessage(UniAgentChatMessage existingMessage = null)
        {
            StopPendingAssistantAnimation();
            lock (_progressUpdateLock)
            {
                _queuedProgressTexts.Clear();
                _progressDispatchPending = false;
            }

            _pendingDotCount = 0;
            _pendingStartRealtime = EditorApplication.timeSinceStartup;
            _pendingProgressText = LocalizeProgressText("Preparing request");
            _pendingProgressLines.Clear();
            _pendingProgressLines.Add(_pendingProgressText);
            if (existingMessage != null)
            {
                existingMessage.IsLoading = true;
                existingMessage.Role = ChatRole.Assistant;
                existingMessage.Text = BuildThinkingText(_pendingDotCount, _pendingProgressLines, GetProviderDisplayName(), 0d);
                existingMessage.Time = DateTime.Now.ToString("HH:mm:ss");
                existingMessage.TokenSummary = null;
                _pendingAssistantMessage = existingMessage;
                RefreshChatUI();
            }
            else
            {
                _pendingAssistantMessage = AddMessage(ChatRole.Assistant, BuildThinkingText(_pendingDotCount, _pendingProgressLines, GetProviderDisplayName(), 0d), null, true);
            }

            _pendingAnimationItem = rootVisualElement.schedule.Execute(() =>
            {
                if (_pendingAssistantMessage == null || !_pendingAssistantMessage.IsLoading)
                {
                    StopPendingAssistantAnimation();
                    return;
                }

                _pendingDotCount = (_pendingDotCount + 1) % 4;
                var elapsed = EditorApplication.timeSinceStartup - _pendingStartRealtime;
                _pendingAssistantMessage.Text = BuildThinkingText(_pendingDotCount, _pendingProgressLines, GetProviderDisplayName(), elapsed);
                RefreshChatUI();
            }).Every(300);
        }

        private void QueueCliProgressUpdate(string progressText)
        {
            if (string.IsNullOrWhiteSpace(progressText))
            {
                return;
            }

            lock (_progressUpdateLock)
            {
                _queuedProgressTexts.Enqueue(progressText);
                if (_progressDispatchPending)
                {
                    return;
                }

                _progressDispatchPending = true;
            }

            EditorApplication.delayCall += ApplyQueuedProgressUpdate;
        }

        private void ApplyQueuedProgressUpdate()
        {
            var textsToApply = new List<string>();
            lock (_progressUpdateLock)
            {
                while (_queuedProgressTexts.Count > 0)
                {
                    textsToApply.Add(_queuedProgressTexts.Dequeue());
                }

                _progressDispatchPending = false;
            }

            for (var i = 0; i < textsToApply.Count; i++)
            {
                UpdatePendingProgressText(textsToApply[i]);
            }
        }

        private void UpdatePendingProgressText(string progressText)
        {
            if (_pendingAssistantMessage == null || !_pendingAssistantMessage.IsLoading)
            {
                return;
            }

            var normalized = NormalizeProgressText(LocalizeProgressText(progressText));
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, _pendingProgressText, StringComparison.Ordinal))
            {
                return;
            }

            _pendingProgressText = normalized;
            _pendingProgressLines.Add(normalized);
            if (_pendingProgressLines.Count > 6)
            {
                _pendingProgressLines.RemoveAt(0);
            }

            var elapsed = EditorApplication.timeSinceStartup - _pendingStartRealtime;
            _pendingAssistantMessage.Text = BuildThinkingText(_pendingDotCount, _pendingProgressLines, GetProviderDisplayName(), elapsed);
            RefreshChatUI();
            ScrollToBottom();
        }

        private static string NormalizeProgressText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            var text = rawText.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 140)
            {
                text = text.Substring(0, 140) + "...";
            }

            return text;
        }

        private void StopPendingAssistantAnimation()
        {
            if (_pendingAnimationItem == null)
            {
                return;
            }

            _pendingAnimationItem.Pause();
            _pendingAnimationItem = null;
        }

        private void CompletePendingAssistantMessage(string text, ChatRole role, string tokenSummary)
        {
            StopPendingAssistantAnimation();
            lock (_progressUpdateLock)
            {
                _queuedProgressTexts.Clear();
                _progressDispatchPending = false;
            }
            _pendingProgressText = string.Empty;
            _pendingProgressLines.Clear();

            if (_pendingAssistantMessage == null)
            {
                _pendingAssistantMessage = FindLatestLoadingMessage();
                if (_pendingAssistantMessage == null)
                {
                    AddMessage(role, text, tokenSummary);
                    return;
                }
            }

            _pendingAssistantMessage.IsLoading = false;
            _pendingAssistantMessage.Role = role;
            _pendingAssistantMessage.Text = text;
            _pendingAssistantMessage.Time = DateTime.Now.ToString("HH:mm:ss");
            _pendingAssistantMessage.TokenSummary = tokenSummary;
            _pendingAssistantMessage = null;
            SaveChatHistory();
            RefreshChatUI();
            ScrollToBottom();
        }

        private UniAgentChatMessage FindLatestLoadingMessage()
        {
            for (var i = _messages.Count - 1; i >= 0; i--)
            {
                var message = _messages[i];
                if (message != null && message.IsLoading)
                {
                    return message;
                }
            }

            return null;
        }

        private void SynchronizeBusyState()
        {
            if (!_isBusy)
            {
                return;
            }

            if (HasActiveRuns() || HasDeferredRunResults())
            {
                return;
            }

            var loading = FindLatestLoadingMessage();
            if (loading != null)
            {
                loading.IsLoading = false;
                loading.Role = ChatRole.System;
                loading.Time = DateTime.Now.ToString("HH:mm:ss");
                loading.TokenSummary = null;
                loading.Text = "Previous request was interrupted. Please send again.";
                SaveChatHistory();
            }

            _pendingAssistantMessage = null;
            StopPendingAssistantAnimation();
            _isBusy = false;
            UniAgentToolbarShortcut.SetCompleteState();
            SetStatus("Ready");
        }

        private string BuildThinkingText(int dotCount, List<string> progressLines, string agentName, double elapsedSeconds)
        {
            var title = IsKoreanProgressLanguage()
                ? $"{agentName} 생각 중" + new string('.', dotCount) + FormatElapsedSuffix(elapsedSeconds)
                : $"{agentName} is thinking" + new string('.', dotCount) + FormatElapsedSuffix(elapsedSeconds);
            if (progressLines == null || progressLines.Count == 0)
            {
                return title + "\n" + LocalizeProgressText("Working...");
            }

            var sb = new StringBuilder();
            sb.AppendLine(title);
            for (var i = 0; i < progressLines.Count; i++)
            {
                var line = progressLines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                sb.Append(GetProgressLineIcon(line)).Append(' ').AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        private string ResolveActiveProgressLanguage(string promptText)
        {
            var selected = NormalizeOption(_progressLanguage, ProgressLanguageOptions, DefaultProgressLanguage);
            if (string.Equals(selected, ProgressLanguageKorean, StringComparison.Ordinal))
            {
                return ProgressLanguageKorean;
            }

            if (string.Equals(selected, ProgressLanguageEnglish, StringComparison.Ordinal))
            {
                return ProgressLanguageEnglish;
            }

            return ContainsKoreanText(promptText) ? ProgressLanguageKorean : ProgressLanguageEnglish;
        }

        private bool IsKoreanProgressLanguage()
        {
            return string.Equals(_activeProgressLanguage, ProgressLanguageKorean, StringComparison.Ordinal);
        }

        private string BuildBusyStatusText(string agentName, bool diffPreview)
        {
            if (IsKoreanProgressLanguage())
            {
                return diffPreview ? $"{agentName} 변경 미리보기 생성 중..." : $"{agentName} 생각 중...";
            }

            return diffPreview ? $"{agentName} is generating diff preview..." : $"{agentName} is thinking...";
        }

        private static bool ContainsKoreanText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if ((ch >= '\uAC00' && ch <= '\uD7A3') ||
                    (ch >= '\u3130' && ch <= '\u318F') ||
                    (ch >= '\u1100' && ch <= '\u11FF'))
                {
                    return true;
                }
            }

            return false;
        }

        private string LocalizeProgressText(string text)
        {
            if (!IsKoreanProgressLanguage() || string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var trimmed = text.Trim();
            switch (trimmed)
            {
                case "Preparing request":
                    return "요청 준비 중";
                case "Working...":
                    return "작업 중...";
                case "Initialized agent session":
                    return "세션 초기화 완료";
                case "Contacting model API":
                    return "모델 API 연결 중";
                case "Checked rate limit":
                    return "사용량 제한 확인 완료";
                case "Received assistant answer":
                    return "응답 수신 완료";
                case "Received final response":
                    return "최종 응답 수신 완료";
                case "Assistant started responding":
                    return "응답 생성 시작";
                case "Running tool":
                    return "도구 실행 중";
                case "Drafting response":
                    return "응답 작성 중";
                case "Preparing tool input":
                    return "도구 입력 준비 중";
                case "Wrapping up response":
                    return "응답 정리 중";
                case "Finalizing response":
                    return "응답 마무리 중";
            }

            const string runningPrefix = "Running ";
            if (trimmed.StartsWith(runningPrefix, StringComparison.Ordinal))
            {
                var toolName = trimmed.Substring(runningPrefix.Length).Trim();
                return string.IsNullOrEmpty(toolName) ? "도구 실행 중" : $"도구 실행 중: {toolName}";
            }

            return text;
        }

        /// <summary>경과 시간을 "(12s)"/"(1m 05s)" 형태의 접미사로 만듭니다. 1초 미만이면 빈 문자열입니다.</summary>
        private static string FormatElapsedSuffix(double elapsedSeconds)
        {
            var totalSeconds = (int)Math.Floor(Math.Max(0d, elapsedSeconds));
            if (totalSeconds < 1)
            {
                return string.Empty;
            }

            if (totalSeconds < 60)
            {
                return $" ({totalSeconds}s)";
            }

            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $" ({minutes}m {seconds:00}s)";
        }

        /// <summary>진행 상황 한 줄의 내용을 보고 어울리는 아이콘을 고릅니다. 실행/읽기/편집/검색 여부를 대략적으로 구분합니다.</summary>
        private static string GetProgressLineIcon(string line)
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("read") || lower.Contains("읽")) return "📄";
            if (lower.Contains("write") || lower.Contains("edit") || lower.Contains("patch") || lower.Contains("apply") || lower.Contains("작성") || lower.Contains("수정")) return "✏️";
            if (lower.Contains("search") || lower.Contains("grep") || lower.Contains("find") || lower.Contains("검색")) return "🔍";
            if (lower.Contains("run") || lower.Contains("shell") || lower.Contains("command") || lower.Contains("exec") || lower.Contains("실행")) return "⚙️";
            return "•";
        }

        private void RefreshChatUI()
        {
            if (_chatScrollView == null)
            {
                return;
            }

            _chatScrollView.contentContainer.Clear();
            for (var i = 0; i < _messages.Count; i++)
            {
                AddMessageToView(_messages[i]);
            }

            ScrollToBottom();
        }

        private void AddMessageToView(UniAgentChatMessage message)
        {
            if (_chatScrollView == null)
            {
                return;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignSelf = Align.Stretch;
            row.style.marginBottom = 6f;
            row.style.justifyContent = message.Role == ChatRole.User ? Justify.FlexEnd : Justify.FlexStart;
            row.style.paddingLeft = 2f;
            row.style.paddingRight = 2f;

            var bubble = new VisualElement();
            bubble.style.maxWidth = new StyleLength(new Length(78f, LengthUnit.Percent));
            bubble.style.flexShrink = 1f;
            bubble.style.minWidth = 0f;
            bubble.style.paddingBottom = 6f;
            bubble.style.paddingLeft = 8f;
            bubble.style.paddingRight = 8f;
            bubble.style.paddingTop = 6f;
            bubble.style.borderBottomLeftRadius = 6f;
            bubble.style.borderBottomRightRadius = 6f;
            bubble.style.borderTopLeftRadius = 6f;
            bubble.style.borderTopRightRadius = 6f;

            var headerText = $"{message.Role}  {message.Time}";

            var roleLabel = new Label(headerText);
            ApplyPreferredFont(roleLabel);
            roleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            roleLabel.style.marginBottom = 2f;
            roleLabel.style.fontSize = 11f;
            roleLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            bubble.Add(roleLabel);

            AddMessageContent(bubble, message);

            switch (message.Role)
            {
                case ChatRole.User:
                    bubble.style.backgroundColor = new Color(0.14f, 0.34f, 0.76f, 1f);
                    break;
                case ChatRole.Assistant:
                    bubble.style.backgroundColor = new Color(0.20f, 0.20f, 0.22f, 1f);
                    break;
                case ChatRole.System:
                    bubble.style.backgroundColor = new Color(0.25f, 0.32f, 0.18f, 1f);
                    break;
                default:
                    bubble.style.backgroundColor = new Color(0.55f, 0.18f, 0.18f, 1f);
                    break;
            }

            row.Add(bubble);
            _chatScrollView.Add(row);
        }

        private void AddMessageContent(VisualElement bubble, UniAgentChatMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (message.IsLoading)
            {
                AddPlainTextLine(bubble, message.Text);
                return;
            }

            AddMarkdownContent(bubble, message.Text);
        }

        private void AddMarkdownContent(VisualElement parent, string markdown)
        {
            var text = markdown ?? string.Empty;
            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');

            var inCodeBlock = false;
            var codeBuilder = new StringBuilder();

            for (var i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i] ?? string.Empty;
                var trimmed = rawLine.TrimStart();

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    if (inCodeBlock)
                    {
                        AddCodeBlock(parent, codeBuilder.ToString());
                        codeBuilder.Clear();
                        inCodeBlock = false;
                    }
                    else
                    {
                        inCodeBlock = true;
                    }

                    continue;
                }

                if (inCodeBlock)
                {
                    codeBuilder.AppendLine(rawLine);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    AddPlainTextLine(parent, " ");
                    continue;
                }

                if (trimmed.StartsWith("> ", StringComparison.Ordinal))
                {
                    AddRichTextLine(parent, $"<i>{FormatInlineMarkdown(trimmed.Substring(2))}</i>", new Color(0.78f, 0.86f, 0.96f, 1f));
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("+ ", StringComparison.Ordinal))
                {
                    AddRichTextLine(parent, $"• {FormatInlineMarkdown(trimmed.Substring(2))}");
                    continue;
                }

                if (StartsWithNumberedList(trimmed, out var listText))
                {
                    AddRichTextLine(parent, listText);
                    continue;
                }

                var headingLevel = GetHeadingLevel(trimmed, out var headingText);
                if (headingLevel > 0)
                {
                    var size = headingLevel == 1 ? 16 : headingLevel == 2 ? 15 : 14;
                    AddRichTextLine(parent, $"<b><size={size}>{FormatInlineMarkdown(headingText)}</size></b>");
                    continue;
                }

                AddRichTextLine(parent, FormatInlineMarkdown(rawLine));
            }

            if (inCodeBlock && codeBuilder.Length > 0)
            {
                AddCodeBlock(parent, codeBuilder.ToString());
            }
        }

        private void AddCodeBlock(VisualElement parent, string code)
        {
            var container = new VisualElement();
            container.style.marginTop = 3f;
            container.style.marginBottom = 3f;
            container.style.paddingBottom = 6f;
            container.style.paddingTop = 6f;
            container.style.paddingLeft = 8f;
            container.style.paddingRight = 8f;
            container.style.backgroundColor = new Color(0.13f, 0.13f, 0.14f, 1f);
            container.style.borderBottomLeftRadius = 4f;
            container.style.borderBottomRightRadius = 4f;
            container.style.borderTopLeftRadius = 4f;
            container.style.borderTopRightRadius = 4f;

            var normalized = (code ?? string.Empty).Replace("\t", "    ");
            var label = new Label(normalized);
            ApplyPreferredFont(label);
            // Code blocks should render raw characters like < and > as-is.
            label.enableRichText = false;
            label.style.whiteSpace = WhiteSpace.PreWrap;
            label.style.fontSize = 12f;
            label.style.color = new Color(0.87f, 0.91f, 0.95f, 1f);
            container.Add(label);

            parent.Add(container);
        }

        private void AddPlainTextLine(VisualElement parent, string text)
        {
            var label = new Label(text ?? string.Empty);
            ApplyPreferredFont(label);
            label.style.whiteSpace = WhiteSpace.PreWrap;
            label.style.flexShrink = 1f;
            label.style.minWidth = 0f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.fontSize = 12f;
            label.style.color = new Color(0.96f, 0.96f, 0.96f, 1f);
            parent.Add(label);
        }

        private void AddRichTextLine(VisualElement parent, string text, Color? color = null)
        {
            var label = new Label(text ?? string.Empty);
            ApplyPreferredFont(label);
            label.enableRichText = true;
            label.style.whiteSpace = WhiteSpace.PreWrap;
            label.style.flexShrink = 1f;
            label.style.minWidth = 0f;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.fontSize = 12f;
            label.style.color = color ?? new Color(0.96f, 0.96f, 0.96f, 1f);
            parent.Add(label);
        }

        private static int GetHeadingLevel(string trimmedLine, out string headingText)
        {
            headingText = string.Empty;
            var level = 0;
            while (level < trimmedLine.Length && trimmedLine[level] == '#')
            {
                level++;
            }

            if (level == 0 || level > 6)
            {
                return 0;
            }

            if (trimmedLine.Length <= level || trimmedLine[level] != ' ')
            {
                return 0;
            }

            headingText = trimmedLine.Substring(level + 1);
            return level;
        }

        private static bool StartsWithNumberedList(string trimmedLine, out string text)
        {
            text = string.Empty;
            var dotIndex = trimmedLine.IndexOf('.');
            if (dotIndex <= 0)
            {
                return false;
            }

            for (var i = 0; i < dotIndex; i++)
            {
                if (!char.IsDigit(trimmedLine[i]))
                {
                    return false;
                }
            }

            if (dotIndex + 1 >= trimmedLine.Length || trimmedLine[dotIndex + 1] != ' ')
            {
                return false;
            }

            text = $"{trimmedLine.Substring(0, dotIndex + 1)} {FormatInlineMarkdown(trimmedLine.Substring(dotIndex + 2))}";
            return true;
        }

        private static string FormatInlineMarkdown(string line)
        {
            var text = EscapeRichText(line ?? string.Empty);
            text = MarkdownLinkRegex.Replace(text, "$1 ($2)");

            // Protect inline code spans before applying other inline markdown transforms.
            var inlineCodeSegments = new List<string>();
            text = MarkdownCodeRegex.Replace(text, match =>
            {
                var token = $"%%CODESEG{inlineCodeSegments.Count}%%";
                inlineCodeSegments.Add($"<color=#E6C07B><b>{match.Groups[1].Value}</b></color>");
                return token;
            });

            text = MarkdownBoldAsteriskRegex.Replace(text, "<b>$1</b>");
            text = MarkdownBoldUnderscoreRegex.Replace(text, "<b>$1</b>");
            text = MarkdownItalicAsteriskRegex.Replace(text, "<i>$1</i>");
            text = MarkdownItalicUnderscoreRegex.Replace(text, "<i>$1</i>");

            for (var i = 0; i < inlineCodeSegments.Count; i++)
            {
                text = text.Replace($"%%CODESEG{i}%%", inlineCodeSegments[i]);
            }

            return text.Replace("\\*", "*").Replace("\\_", "_").Replace("\\`", "`");
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private void ScrollToBottom()
        {
            if (_chatScrollView == null)
            {
                return;
            }

            _chatScrollView.schedule.Execute(() =>
            {
                _chatScrollView.scrollOffset = new Vector2(0f, float.MaxValue);
            }).ExecuteLater(10);
        }

        // -------------------------
        // Chat sessions
        // -------------------------

    }
}

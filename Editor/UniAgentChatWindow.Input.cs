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
    /// 입력창 이벤트, @mention 자동완성, 프로젝트 파일 인덱싱을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void OnInputValueChanged(ChangeEvent<string> evt)
        {
            if (_suppressMentionRefresh)
            {
                return;
            }

            var normalized = NormalizeMentionBrokenLine(evt.newValue ?? string.Empty);
            if (!string.Equals(evt.newValue, normalized, StringComparison.Ordinal))
            {
                _suppressMentionRefresh = true;
                _inputField.value = normalized;
                _suppressMentionRefresh = false;
                RefreshMentionSuggestions(normalized);
                return;
            }

            RefreshMentionSuggestions(evt.newValue);
        }

        private void ConfigureInputFieldWordWrap()
        {
            if (_inputField == null)
            {
                return;
            }

            var textInput = QueryTextInputElement(_inputField);
            if (textInput != null)
            {
                textInput.style.whiteSpace = WhiteSpace.Normal;
                textInput.style.overflow = Overflow.Hidden;
                textInput.style.unityTextAlign = TextAnchor.UpperLeft;
                ApplyPreferredFont(textInput);
            }

            var textElement = _inputField.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.whiteSpace = WhiteSpace.Normal;
                textElement.style.unityTextAlign = TextAnchor.UpperLeft;
                ApplyPreferredFont(textElement);
            }
        }

        private bool HandleMentionInputKeyDown(KeyDownEvent evt)
        {
            if (_mentionSuggestionPanel == null || _mentionSuggestionPanel.style.display == DisplayStyle.None || _mentionSuggestions.Count == 0)
            {
                return false;
            }

            var isSubmitKey = evt.keyCode == KeyCode.Return
                || evt.keyCode == KeyCode.KeypadEnter
                || evt.character == '\n'
                || evt.character == '\r';

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    ConsumeMentionKeyEvent(evt);
                    SelectMentionSuggestionDelta(1);
                    return true;
                case KeyCode.UpArrow:
                    ConsumeMentionKeyEvent(evt);
                    SelectMentionSuggestionDelta(-1);
                    return true;
                case KeyCode.Tab:
                    ConsumeMentionKeyEvent(evt);
                    TryCommitSelectedMention();
                    return true;
                case KeyCode.Escape:
                    ConsumeMentionKeyEvent(evt);
                    HideMentionSuggestions();
                    return true;
            }

            if (isSubmitKey && !evt.shiftKey)
            {
                ConsumeMentionKeyEvent(evt);
                _suppressNextMentionSubmitKeyUp = true;
                ScheduleDeferredMentionCommit();
                return true;
            }

            return false;
        }

        private static void ConsumeMentionKeyEvent(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            evt.StopPropagation();
            evt.StopImmediatePropagation();
        }

        private static void ConsumeMentionKeyEvent(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            evt.StopPropagation();
            evt.StopImmediatePropagation();
        }

        private void OnInputKeyUp(KeyUpEvent evt)
        {
            if (!_suppressNextMentionSubmitKeyUp)
            {
                return;
            }

            var isSubmitKey = evt.keyCode == KeyCode.Return
                || evt.keyCode == KeyCode.KeypadEnter
                || evt.character == '\n'
                || evt.character == '\r';

            if (isSubmitKey)
            {
                ConsumeMentionKeyEvent(evt);
            }

            _suppressNextMentionSubmitKeyUp = false;
        }

        private void ScheduleDeferredMentionCommit()
        {
            if (_deferredMentionCommitPending)
            {
                return;
            }

            _deferredMentionCommitPending = true;
            EditorApplication.delayCall += CommitMentionSelectionDeferred;
        }

        private void CommitMentionSelectionDeferred()
        {
            _deferredMentionCommitPending = false;
            if (_inputField == null)
            {
                return;
            }

            TryCommitSelectedMention();

            // Some platforms may still inject an enter newline; collapse @path line breaks.
            var current = _inputField.value ?? string.Empty;
            var normalized = NormalizeMentionBrokenLine(current);
            if (!string.Equals(current, normalized, StringComparison.Ordinal))
            {
                _suppressMentionRefresh = true;
                _inputField.value = normalized;
                _suppressMentionRefresh = false;
                MoveInputCaretToEnd(true);
            }
        }

        private void RefreshMentionSuggestions(string text)
        {
            if (!TryExtractTrailingMention(text, out _activeMentionStartIndex, out _activeMentionQuery))
            {
                HideMentionSuggestions();
                return;
            }

            EnsureProjectFileIndex();
            _mentionSuggestions.Clear();

            var query = _activeMentionQuery ?? string.Empty;
            if (_projectFileIndex.Count > 0)
            {
                var normalizedQuery = query.Replace('\\', '/');
                var added = 0;
                for (var i = 0; i < _projectFileIndex.Count && added < MaxMentionSuggestions; i++)
                {
                    var path = _projectFileIndex[i];
                    if (!string.IsNullOrEmpty(normalizedQuery)
                        && path.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    _mentionSuggestions.Add(path);
                    added++;
                }
            }

            _selectedMentionSuggestionIndex = _mentionSuggestions.Count > 0 ? 0 : -1;
            RenderMentionSuggestions();
        }

        private void RenderMentionSuggestions()
        {
            if (_mentionSuggestionPanel == null)
            {
                return;
            }

            if (_mentionSuggestions.Count == 0)
            {
                _mentionSuggestionPanel.style.display = DisplayStyle.None;
                for (var i = 0; i < _mentionSuggestionButtonPool.Count; i++)
                {
                    _mentionSuggestionButtonPool[i].style.display = DisplayStyle.None;
                }
                return;
            }

            _mentionSuggestionPanel.style.display = DisplayStyle.Flex;
            for (var i = 0; i < _mentionSuggestions.Count; i++)
            {
                EnsureMentionSuggestionButtonPoolSize(i + 1);
                var button = _mentionSuggestionButtonPool[i];
                button.text = _mentionSuggestions[i];
                button.userData = i;
                button.style.display = DisplayStyle.Flex;
                button.style.borderBottomWidth = i == _mentionSuggestions.Count - 1 ? 0f : 1f;
                button.style.borderBottomColor = new Color(0.23f, 0.27f, 0.33f, 1f);
                button.style.backgroundColor = i == _selectedMentionSuggestionIndex
                    ? new Color(0.16f, 0.36f, 0.78f, 0.95f)
                    : new Color(0.15f, 0.17f, 0.20f, 0.95f);
            }

            for (var i = _mentionSuggestions.Count; i < _mentionSuggestionButtonPool.Count; i++)
            {
                _mentionSuggestionButtonPool[i].style.display = DisplayStyle.None;
            }
        }

        private void EnsureMentionSuggestionButtonPoolSize(int targetSize)
        {
            if (_mentionSuggestionPanel == null)
            {
                return;
            }

            while (_mentionSuggestionButtonPool.Count < targetSize)
            {
                Button button = null;
                button = new Button(() =>
                {
                    if (button?.userData is int idx)
                    {
                        ApplyMentionSuggestion(idx);
                    }
                });

                button.style.height = MentionSuggestionItemHeight;
                button.style.minHeight = MentionSuggestionItemHeight;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.paddingLeft = 6f;
                button.style.paddingRight = 6f;
                button.style.marginLeft = 0f;
                button.style.marginRight = 0f;
                button.style.marginTop = 0f;
                button.style.marginBottom = 0f;
                button.style.display = DisplayStyle.None;
                button.style.flexShrink = 0f;

                _mentionSuggestionButtonPool.Add(button);
                _mentionSuggestionPanel.Add(button);
            }
        }

        private void SelectMentionSuggestionDelta(int delta)
        {
            if (_mentionSuggestions.Count == 0)
            {
                _selectedMentionSuggestionIndex = -1;
                return;
            }

            if (_selectedMentionSuggestionIndex < 0)
            {
                _selectedMentionSuggestionIndex = 0;
            }
            else
            {
                _selectedMentionSuggestionIndex = (_selectedMentionSuggestionIndex + delta + _mentionSuggestions.Count) % _mentionSuggestions.Count;
            }

            RenderMentionSuggestions();
        }

        private void TryCommitSelectedMention()
        {
            if (_mentionSuggestions.Count == 0)
            {
                return;
            }

            var safeIndex = Mathf.Clamp(_selectedMentionSuggestionIndex, 0, _mentionSuggestions.Count - 1);
            ApplyMentionSuggestion(safeIndex);
        }

        private void ApplyMentionSuggestion(int index)
        {
            if (_inputField == null || index < 0 || index >= _mentionSuggestions.Count)
            {
                return;
            }

            var selected = NormalizeMentionSuggestionValue(_mentionSuggestions[index]);
            if (string.IsNullOrEmpty(selected))
            {
                HideMentionSuggestions();
                return;
            }

            var current = _inputField.value ?? string.Empty;
            var mentionStart = -1;
            var mentionEnd = -1;

            if (TryExtractTrailingMention(current, out var trailingStart, out _))
            {
                mentionStart = trailingStart;
                mentionEnd = current.Length;
            }
            else if (TryResolveMentionBoundsFromActiveState(current, out mentionStart, out mentionEnd))
            {
                // Fallback when enter key inserted whitespace before mention commit.
            }

            if (mentionStart < 0 || mentionEnd < mentionStart)
            {
                HideMentionSuggestions();
                return;
            }

            var replaceStart = mentionStart + 1;
            var replaceLength = mentionEnd - replaceStart;
            if (replaceStart < 0 || replaceStart > current.Length)
            {
                HideMentionSuggestions();
                return;
            }

            var prefix = current.Substring(0, replaceStart);
            var suffix = current.Substring(replaceStart + Mathf.Max(0, replaceLength));
            suffix = TrimLeadingMentionCommitWhitespace(suffix);

            var needsSpaceSuffix = string.IsNullOrEmpty(suffix) || !char.IsWhiteSpace(suffix[0]);
            var inserted = needsSpaceSuffix ? $"{selected} " : selected;
            var updated = prefix + inserted + suffix;

            _suppressMentionRefresh = true;
            _inputField.value = updated;
            _suppressMentionRefresh = false;
            HideMentionSuggestions();
            MoveInputCaretToEnd(true);
        }

        private bool TryResolveMentionBoundsFromActiveState(string current, out int mentionStart, out int mentionEnd)
        {
            mentionStart = _activeMentionStartIndex;
            mentionEnd = -1;
            if (string.IsNullOrEmpty(current) || mentionStart < 0 || mentionStart >= current.Length)
            {
                return false;
            }

            if (current[mentionStart] != '@')
            {
                mentionStart = current.LastIndexOf('@');
                if (mentionStart < 0 || mentionStart >= current.Length)
                {
                    return false;
                }
            }

            mentionEnd = mentionStart + 1;
            while (mentionEnd < current.Length && !char.IsWhiteSpace(current[mentionEnd]))
            {
                mentionEnd++;
            }

            return true;
        }

        private static string NormalizeMentionSuggestionValue(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            var normalized = rawValue
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim()
                .Replace('\\', '/');

            return normalized;
        }

        private static string TrimLeadingMentionCommitWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var index = 0;
            while (index < text.Length)
            {
                var ch = text[index];
                if (ch != '\r' && ch != '\n' && ch != '\t' && ch != ' ')
                {
                    break;
                }

                index++;
            }

            return index > 0 ? text.Substring(index) : text;
        }

        private static string NormalizeMentionBrokenLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return Regex.Replace(normalized, @"@([^\s@\n]+)\s*\n\s*/", "@$1/");
        }

        private void MoveInputCaretToEnd(bool defer)
        {
            if (_inputField == null)
            {
                return;
            }

            void Apply()
            {
                var value = _inputField.value ?? string.Empty;
                var end = value.Length;
                _inputField.Focus();
                SetTextFieldSelectionToEnd(_inputField, end);
            }

            if (defer)
            {
                _inputField.schedule.Execute(Apply).ExecuteLater(0);
            }
            else
            {
                Apply();
            }
        }

        private void HideMentionSuggestions()
        {
            _activeMentionStartIndex = -1;
            _activeMentionQuery = string.Empty;
            _selectedMentionSuggestionIndex = -1;
            _mentionSuggestions.Clear();
            if (_mentionSuggestionPanel != null)
            {
                _mentionSuggestionPanel.style.display = DisplayStyle.None;
                for (var i = 0; i < _mentionSuggestionButtonPool.Count; i++)
                {
                    _mentionSuggestionButtonPool[i].style.display = DisplayStyle.None;
                }
            }
        }

        private void EnsureProjectFileIndex()
        {
            var now = EditorApplication.timeSinceStartup;
            if (!_projectFileIndexDirty && now < _nextProjectFileIndexRefreshAt && _projectFileIndex.Count > 0)
            {
                return;
            }

            _projectFileIndexDirty = false;
            _nextProjectFileIndexRefreshAt = now + 8d;
            _projectFileIndex.Clear();

            var assetPaths = AssetDatabase.GetAllAssetPaths();
            for (var i = 0; i < assetPaths.Length; i++)
            {
                var path = assetPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _projectFileIndex.Add(path.Replace('\\', '/'));
            }

            _projectFileIndex.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void MarkProjectFileIndexDirty()
        {
            _projectFileIndexDirty = true;
        }

        private void OnProjectChanged()
        {
            MarkProjectFileIndexDirty();
        }

        private static bool TryExtractTrailingMention(string text, out int mentionStart, out string query)
        {
            mentionStart = -1;
            query = string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var atIndex = text.LastIndexOf('@');
            if (atIndex < 0)
            {
                return false;
            }

            if (atIndex > 0)
            {
                var prev = text[atIndex - 1];
                if (!char.IsWhiteSpace(prev) && prev != '(' && prev != '[' && prev != '{' && prev != '"' && prev != '\'')
                {
                    return false;
                }
            }

            for (var i = atIndex + 1; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            mentionStart = atIndex;
            query = text.Substring(atIndex + 1);
            return true;
        }

        private List<string> ResolveTargetedFilesFromMentions(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            EnsureProjectFileIndex();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = MentionPathRegex.Matches(text);
            for (var i = 0; i < matches.Count; i++)
            {
                var rawToken = matches[i].Groups[1].Value;
                var token = SanitizeMentionToken(rawToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (!TryResolveMentionPath(token, out var resolved))
                {
                    continue;
                }

                if (unique.Add(resolved))
                {
                    result.Add(resolved);
                    if (result.Count >= MaxTargetedFilesPerTurn)
                    {
                        break;
                    }
                }
            }

            return result;
        }

        private bool TryResolveMentionPath(string rawPath, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return false;
            }

            var normalized = rawPath.Trim().Replace('\\', '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.TrimStart('/');
            }

            for (var i = 0; i < _projectFileIndex.Count; i++)
            {
                var candidate = _projectFileIndex[i];
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }

            if (!normalized.Contains("/"))
            {
                string found = null;
                for (var i = 0; i < _projectFileIndex.Count; i++)
                {
                    var candidate = _projectFileIndex[i];
                    var fileName = Path.GetFileName(candidate);
                    if (!string.Equals(fileName, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (found != null && !string.Equals(found, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    found = candidate;
                }

                if (!string.IsNullOrWhiteSpace(found))
                {
                    resolvedPath = found;
                    return true;
                }
            }

            var projectRoot = UniAgentChatHelper.GetProjectRootPath().Replace('\\', '/');
            if (Path.IsPathRooted(rawPath))
            {
                var normalizedAbsolute = Path.GetFullPath(rawPath).Replace('\\', '/');
                if (normalizedAbsolute.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    var relative = normalizedAbsolute.Substring(projectRoot.Length + 1);
                    for (var i = 0; i < _projectFileIndex.Count; i++)
                    {
                        if (string.Equals(_projectFileIndex[i], relative, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedPath = _projectFileIndex[i];
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string SanitizeMentionToken(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return string.Empty;
            }

            var sanitized = rawToken.Trim();
            while (sanitized.Length > 0)
            {
                var ch = sanitized[sanitized.Length - 1];
                if (ch == ')' || ch == ']' || ch == '}' || ch == ',' || ch == '.' || ch == ';' || ch == ':' || ch == '\'' || ch == '"')
                {
                    sanitized = sanitized.Substring(0, sanitized.Length - 1);
                    continue;
                }

                break;
            }

            return sanitized;
        }

        // -------------------------
        // Environment / auth actions
        // -------------------------

    }
}

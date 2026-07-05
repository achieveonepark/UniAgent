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
    /// 상태 표시줄 갱신과 EditorPrefs 기반 설정 저장/복원을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void SetBusy(bool busy, string status)
        {
            var wasBusy = _isBusy;
            _isBusy = busy;
            if (busy)
            {
                UniAgentToolbarShortcut.SetBusyState();
            }
            else if (wasBusy)
            {
                if (HasActiveRuns())
                {
                    UniAgentToolbarShortcut.SetBusyState();
                }
                else
                {
                    UniAgentToolbarShortcut.SetCompleteState();
                }
            }

            SetStatus(status);
        }

        private void SetStatus(string status)
        {
            _statusText = status;
            UpdateStatusUI();
        }

        private void UpdateStatusUI()
        {
            var canSend = IsChatReady();
            var isBusy = _isBusy;
            var reasonText = BuildReadinessReasonText(canSend);
            var availableColor = canSend
                ? new Color(0.17f, 0.56f, 0.94f, 1f)
                : new Color(0.86f, 0.26f, 0.26f, 1f);
            if (_availabilityDot != null)
            {
                _availabilityDot.style.backgroundColor = availableColor;
            }

            if (_availabilityLabel != null)
            {
                _availabilityLabel.text = isBusy ? "Busy" : (canSend ? "Ready" : "Not Ready");
                _availabilityLabel.tooltip = reasonText;
            }

            if (_settingsStateDot != null)
            {
                _settingsStateDot.style.backgroundColor = availableColor;
            }

            if (_settingsStateLabel != null)
            {
                _settingsStateLabel.text = isBusy
                    ? "Busy"
                    : (canSend ? "Ready" : $"Not Ready ({reasonText})");
                _settingsStateLabel.tooltip = _statusText;
            }

            UpdateTokenGaugeUI();
            _sendButton?.SetEnabled(canSend);
            _inputField?.SetEnabled(canSend);
            if (_sendButton != null)
            {
                _sendButton.style.opacity = canSend ? 1f : 0.62f;
            }

            if (_inputField != null)
            {
                _inputField.style.opacity = canSend ? 1f : 0.84f;
            }
            _sessionPopup?.SetEnabled(!isBusy);
            _newSessionButton?.SetEnabled(!isBusy);
            _newSessionNameField?.SetEnabled(!isBusy);
            _newSessionCreateButton?.SetEnabled(!isBusy);
            if (!canSend)
            {
                HideMentionSuggestions();
            }
            if (isBusy)
            {
                HideNewSessionPopup();
            }
        }

        private bool IsChatReady()
        {
            return !_isBusy && _codexInstalled && _codexLoggedIn;
        }

        private string BuildReadinessReasonText(bool canSend)
        {
            if (canSend)
            {
                return "Ready";
            }

            if (_isBusy)
            {
                return "Busy";
            }

            if (!_codexInstalled)
            {
                return _selectedProvider == "Claude Code" ? "Missing Claude Code" : "Missing Codex";
            }

            if (!_codexLoggedIn)
            {
                return "Login required";
            }

            return "Not ready";
        }

        private static VisualElement CreateStatusDot(float size)
        {
            var dot = new VisualElement();
            dot.style.width = size;
            dot.style.height = size;
            var radius = size * 0.5f;
            dot.style.borderBottomLeftRadius = radius;
            dot.style.borderBottomRightRadius = radius;
            dot.style.borderTopLeftRadius = radius;
            dot.style.borderTopRightRadius = radius;
            return dot;
        }

        private static Texture2D GetSettingsIconTexture()
        {
            if (_settingsIconTexture != null)
            {
                return _settingsIconTexture;
            }

            var iconNames = new[]
            {
                "d_SettingsIcon",
                "SettingsIcon",
                "d__Popup",
                "_Popup"
            };

            for (var i = 0; i < iconNames.Length; i++)
            {
                var content = EditorGUIUtility.IconContent(iconNames[i]);
                if (content?.image is Texture2D texture)
                {
                    _settingsIconTexture = texture;
                    return _settingsIconTexture;
                }
            }

            return null;
        }

        private void LoadPrefs()
        {
            var prefix = UniAgentCliConstants.PrefPrefix;
            _cliPath = EditorPrefs.GetString(prefix + "CliPath", UniAgentCliConstants.DefaultCliPath);
            _claudeCliPath = EditorPrefs.GetString(prefix + "ClaudeCliPath", UniAgentCliConstants.DefaultClaudeCliPath);
            _selectedProvider = NormalizeOption(EditorPrefs.GetString(prefix + "Provider", DefaultProvider), ProviderOptions, DefaultProvider);
            _selectedClaudeModel = NormalizeOption(EditorPrefs.GetString(prefix + "ClaudeModel", DefaultClaudeModel), ClaudeModelOptions, DefaultClaudeModel);
            _markdownFiles = EditorPrefs.GetString(prefix + "MarkdownFiles", UniAgentCliConstants.DefaultMarkdownFiles);
            _maxMarkdownChars = Mathf.Max(500, EditorPrefs.GetInt(prefix + "MaxMarkdownChars", UniAgentCliConstants.DefaultMaxMarkdownChars));
            _disableDomainReloadOnPlay = EditorPrefs.GetBool(prefix + "DisableDomainReload", true);
            _disableSceneReloadOnPlay = EditorPrefs.GetBool(prefix + "DisableSceneReload", false);
            _manualRefreshMode = EditorPrefs.GetBool(prefix + "ManualRefreshMode", true);
            _buildDiffPreviewMode = EditorPrefs.GetBool(prefix + "BuildDiffPreviewMode", false);
            _claudeAutoAcceptEdits = EditorPrefs.GetBool(prefix + "ClaudeAutoAcceptEdits", false);
            _progressLanguage = NormalizeOption(EditorPrefs.GetString(prefix + "ProgressLanguage", DefaultProgressLanguage), ProgressLanguageOptions, DefaultProgressLanguage);
            _activeProgressLanguage = ResolveActiveProgressLanguage(string.Empty);
            _sessionId = EditorPrefs.GetString(prefix + "SessionId", string.Empty);
            _activeChatSessionId = EditorPrefs.GetString(prefix + "ActiveChatSessionId", string.Empty);
            var modeText = EditorPrefs.GetString(prefix + "ChatMode", ChatMode.Build.ToString());
            _chatMode = Enum.TryParse(modeText, out ChatMode parsedMode) ? parsedMode : ChatMode.Build;
            _selectedModel = NormalizeOption(EditorPrefs.GetString(prefix + "Model", DefaultModel), ModelOptions, DefaultModel);
            _selectedReasoningEffort = NormalizeOption(EditorPrefs.GetString(prefix + "ReasoningEffort", DefaultReasoningEffort), ReasoningEffortOptions, DefaultReasoningEffort);
            _sessionTokenUsed = 0;
            _recentTurnTokenCosts.Clear();
            RefreshSessionTokenBudgetForSelection();
        }

        /// <summary>
        /// 현재 선택된 공급자/모델을 기준으로 세션 토큰 예산을 <see cref="UniAgentModelCatalog"/>에서 다시 계산합니다.
        /// 모델마다 컨텍스트 윈도우가 다를 수 있으므로 모델을 바꿀 때마다 호출해야 합니다.
        /// </summary>
        private void RefreshSessionTokenBudgetForSelection()
        {
            var modelId = _selectedProvider == "Claude Code" ? _selectedClaudeModel : _selectedModel;
            _sessionTokenBudget = Mathf.Max(1000, UniAgentModelCatalog.GetTokenBudget(modelId, UniAgentCliConstants.DefaultSessionTokenBudget));
        }

        private void SavePrefs()
        {
            var prefix = UniAgentCliConstants.PrefPrefix;
            EditorPrefs.SetString(prefix + "CliPath", _cliPath ?? UniAgentCliConstants.DefaultCliPath);
            EditorPrefs.SetString(prefix + "ClaudeCliPath", _claudeCliPath ?? UniAgentCliConstants.DefaultClaudeCliPath);
            EditorPrefs.SetString(prefix + "Provider", NormalizeOption(_selectedProvider, ProviderOptions, DefaultProvider));
            EditorPrefs.SetString(prefix + "ClaudeModel", NormalizeOption(_selectedClaudeModel, ClaudeModelOptions, DefaultClaudeModel));
            EditorPrefs.SetString(prefix + "MarkdownFiles", _markdownFiles ?? string.Empty);
            EditorPrefs.SetInt(prefix + "MaxMarkdownChars", Mathf.Max(500, _maxMarkdownChars));
            EditorPrefs.SetBool(prefix + "DisableDomainReload", _disableDomainReloadOnPlay);
            EditorPrefs.SetBool(prefix + "DisableSceneReload", _disableSceneReloadOnPlay);
            EditorPrefs.SetBool(prefix + "ManualRefreshMode", _manualRefreshMode);
            EditorPrefs.SetBool(prefix + "BuildDiffPreviewMode", _buildDiffPreviewMode);
            EditorPrefs.SetBool(prefix + "ClaudeAutoAcceptEdits", _claudeAutoAcceptEdits);
            EditorPrefs.SetString(prefix + "ProgressLanguage", NormalizeOption(_progressLanguage, ProgressLanguageOptions, DefaultProgressLanguage));
            EditorPrefs.SetString(prefix + "SessionId", _sessionId ?? string.Empty);
            EditorPrefs.SetString(prefix + "ActiveChatSessionId", _activeChatSessionId ?? string.Empty);
            EditorPrefs.SetString(prefix + "ChatMode", _chatMode.ToString());
            EditorPrefs.SetString(prefix + "Model", NormalizeOption(_selectedModel, ModelOptions, DefaultModel));
            EditorPrefs.SetString(prefix + "ReasoningEffort", NormalizeOption(_selectedReasoningEffort, ReasoningEffortOptions, DefaultReasoningEffort));
        }

        /// <summary>
        /// 프로젝트 로컬 캐시 파일에서 채팅 이력을 복원합니다.
        /// </summary>
    }
}

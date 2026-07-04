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
    /// 설정 패널, 모드 선택기, 신규 세션 팝업, Build/Diff 모드 토글 UI를 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private VisualElement BuildSettingsPanel()
        {
            var panel = new VisualElement();
            ApplyPanelSurfaceStyle(panel, UiPanelBackground, UiPanelBorder, 8f);
            panel.style.paddingBottom = 6f;
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 6f;
            panel.style.marginBottom = 8f;

            var stateRow = new VisualElement();
            stateRow.style.flexDirection = FlexDirection.Row;
            stateRow.style.alignItems = Align.Center;
            stateRow.style.marginBottom = 8f;

            _settingsStateDot = CreateStatusDot(8f);
            _settingsStateDot.style.marginRight = 6f;
            stateRow.Add(_settingsStateDot);

            _settingsStateLabel = new Label("Not Ready");
            ApplyPreferredFont(_settingsStateLabel);
            _settingsStateLabel.style.color = UiTextPrimary;
            stateRow.Add(_settingsStateLabel);

            panel.Add(stateRow);

            var loginRow = new VisualElement();
            loginRow.style.flexDirection = FlexDirection.Row;
            loginRow.style.alignItems = Align.Center;
            loginRow.style.flexWrap = Wrap.Wrap;
            loginRow.style.marginBottom = 2f;

            var loginButton = new Button(LoginWithDeviceAuth) { text = "Login (Device)" };
            loginButton.style.width = 120f;
            loginButton.style.marginRight = 6f;
            ApplyButtonStyle(loginButton, UiPrimaryButton, UiPrimaryButtonBorder, Color.white, 24f, 6f);
            loginRow.Add(loginButton);

            var logoutButton = new Button(LogoutCodex) { text = "Logout" };
            logoutButton.style.width = 90f;
            logoutButton.style.marginRight = 6f;
            ApplyButtonStyle(logoutButton, UiDangerButton, UiDangerButtonBorder, Color.white, 24f, 6f);
            loginRow.Add(logoutButton);

            var refreshButton = new Button(RefreshEnvironmentState) { text = "Refresh" };
            refreshButton.style.width = 88f;
            refreshButton.tooltip = "Re-check CLI install and login status";
            ApplyButtonStyle(refreshButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 24f, 6f);
            loginRow.Add(refreshButton);

            panel.Add(loginRow);

            // ── Provider 선택 ─────────────────────────────────────────────
            var providerRow = new VisualElement();
            providerRow.style.flexDirection = FlexDirection.Row;
            providerRow.style.alignItems = Align.Center;
            providerRow.style.marginTop = 8f;
            providerRow.style.marginBottom = 4f;

            var providerLabel = new Label("Provider");
            ApplyPreferredFont(providerLabel);
            providerLabel.style.color = UiTextSecondary;
            providerLabel.style.width = 90f;
            providerLabel.style.minWidth = 90f;
            providerLabel.style.marginRight = 6f;
            providerRow.Add(providerLabel);

            var providerIndex = GetOptionIndex(_selectedProvider, ProviderOptions, DefaultProvider);
            var providerPopup = new PopupField<string>(ProviderOptions, providerIndex);
            providerPopup.style.flexGrow = 1f;
            ApplyPopupFieldStyle(providerPopup, 24f);
            providerPopup.RegisterValueChangedCallback(evt =>
            {
                _selectedProvider = NormalizeOption(evt.newValue, ProviderOptions, DefaultProvider);
                RefreshSessionTokenBudgetForSelection();
                UpdateTokenGaugeUI();
                SavePrefs();
                UpdateProviderDependentUI();
            });
            providerRow.Add(providerPopup);
            panel.Add(providerRow);

            // ── Codex CLI 경로(수동 재정의) ────────────────────────────────
            _codexCliPathRow = BuildCliPathRow(
                "Codex Path",
                () => _cliPath,
                value => _cliPath = value,
                UniAgentCliConstants.DefaultCliPath,
                "자동 탐지에 실패하면 `which codex`(macOS/Linux) 또는 `where codex`(Windows) 결과를 그대로 붙여넣으세요. 비워두면 자동 탐지를 사용합니다.");
            panel.Add(_codexCliPathRow);

            // ── Claude Code CLI 경로(수동 재정의) ───────────────────────────
            _claudeCliPathRow = BuildCliPathRow(
                "Claude Path",
                () => _claudeCliPath,
                value => _claudeCliPath = value,
                UniAgentCliConstants.DefaultClaudeCliPath,
                "자동 탐지에 실패하면 `which claude`(macOS/Linux) 또는 `where claude`(Windows) 결과를 그대로 붙여넣으세요. 비워두면 자동 탐지를 사용합니다.");
            panel.Add(_claudeCliPathRow);

            // ── Codex 모델 행 ─────────────────────────────────────────────
            _codexModelRow = new VisualElement();
            _codexModelRow.style.flexDirection = FlexDirection.Row;
            _codexModelRow.style.alignItems = Align.Center;
            _codexModelRow.style.marginBottom = 4f;

            var modelLabel = new Label("Model");
            ApplyPreferredFont(modelLabel);
            modelLabel.style.color = UiTextSecondary;
            modelLabel.style.width = 90f;
            modelLabel.style.minWidth = 90f;
            modelLabel.style.marginRight = 6f;
            _codexModelRow.Add(modelLabel);

            var modelIndex = GetOptionIndex(_selectedModel, ModelOptions, DefaultModel);
            var modelPopup = new PopupField<string>(ModelOptions, modelIndex);
            modelPopup.style.flexGrow = 1f;
            ApplyPopupFieldStyle(modelPopup, 24f);
            modelPopup.RegisterValueChangedCallback(evt =>
            {
                _selectedModel = NormalizeOption(evt.newValue, ModelOptions, DefaultModel);
                RefreshSessionTokenBudgetForSelection();
                UpdateTokenGaugeUI();
                SavePrefs();
            });
            _codexModelRow.Add(modelPopup);
            panel.Add(_codexModelRow);

            // ── Claude 모델 행 ────────────────────────────────────────────
            _claudeModelRow = new VisualElement();
            _claudeModelRow.style.flexDirection = FlexDirection.Row;
            _claudeModelRow.style.alignItems = Align.Center;
            _claudeModelRow.style.marginBottom = 4f;

            var claudeModelLabel = new Label("Model");
            ApplyPreferredFont(claudeModelLabel);
            claudeModelLabel.style.color = UiTextSecondary;
            claudeModelLabel.style.width = 90f;
            claudeModelLabel.style.minWidth = 90f;
            claudeModelLabel.style.marginRight = 6f;
            _claudeModelRow.Add(claudeModelLabel);

            var claudeModelIndex = GetOptionIndex(_selectedClaudeModel, ClaudeModelOptions, DefaultClaudeModel);
            var claudeModelPopup = new PopupField<string>(ClaudeModelOptions, claudeModelIndex);
            claudeModelPopup.style.flexGrow = 1f;
            ApplyPopupFieldStyle(claudeModelPopup, 24f);
            claudeModelPopup.RegisterValueChangedCallback(evt =>
            {
                _selectedClaudeModel = NormalizeOption(evt.newValue, ClaudeModelOptions, DefaultClaudeModel);
                RefreshSessionTokenBudgetForSelection();
                UpdateTokenGaugeUI();
                SavePrefs();
            });
            _claudeModelRow.Add(claudeModelPopup);
            panel.Add(_claudeModelRow);

            // ── Reasoning (Codex 전용) ────────────────────────────────────
            _reasoningRow = new VisualElement();
            _reasoningRow.style.flexDirection = FlexDirection.Row;
            _reasoningRow.style.alignItems = Align.Center;

            var reasoningLabel = new Label("Reasoning");
            ApplyPreferredFont(reasoningLabel);
            reasoningLabel.style.color = UiTextSecondary;
            reasoningLabel.style.width = 90f;
            reasoningLabel.style.minWidth = 90f;
            reasoningLabel.style.marginRight = 6f;
            _reasoningRow.Add(reasoningLabel);

            var reasoningIndex = GetOptionIndex(_selectedReasoningEffort, ReasoningEffortOptions, DefaultReasoningEffort);
            var reasoningPopup = new PopupField<string>(ReasoningEffortOptions, reasoningIndex);
            reasoningPopup.style.flexGrow = 1f;
            ApplyPopupFieldStyle(reasoningPopup, 24f);
            reasoningPopup.RegisterValueChangedCallback(evt =>
            {
                _selectedReasoningEffort = NormalizeOption(evt.newValue, ReasoningEffortOptions, DefaultReasoningEffort);
                SavePrefs();
            });
            _reasoningRow.Add(reasoningPopup);
            panel.Add(_reasoningRow);

            // ── 모델 카탈로그(원격 갱신) ────────────────────────────────────
            var catalogRow = new VisualElement();
            catalogRow.style.flexDirection = FlexDirection.Row;
            catalogRow.style.alignItems = Align.Center;
            catalogRow.style.marginTop = 6f;
            catalogRow.style.marginBottom = 2f;

            var catalogLabel = new Label("Catalog URL");
            ApplyPreferredFont(catalogLabel);
            catalogLabel.style.color = UiTextSecondary;
            catalogLabel.style.width = 90f;
            catalogLabel.style.minWidth = 90f;
            catalogLabel.style.marginRight = 6f;
            catalogRow.Add(catalogLabel);

            var catalogUrlField = new TextField { value = UniAgentModelCatalog.CatalogUrl };
            catalogUrlField.style.flexGrow = 1f;
            ApplyPreferredFont(catalogUrlField);
            StyleTextFieldInput(catalogUrlField, 6f);
            catalogUrlField.tooltip = "모델 목록을 원격에서 갱신할 JSON URL입니다. 비워두면 패키지 내장 기본값을 사용합니다.";
            catalogUrlField.RegisterValueChangedCallback(evt => UniAgentModelCatalog.CatalogUrl = evt.newValue);
            catalogRow.Add(catalogUrlField);

            var catalogRefreshButton = new Button(RefreshModelCatalog) { text = "Refresh" };
            catalogRefreshButton.style.width = 66f;
            catalogRefreshButton.style.marginLeft = 6f;
            catalogRefreshButton.tooltip = "설정한 URL에서 최신 모델 목록/토큰 예산을 내려받습니다.";
            ApplyButtonStyle(catalogRefreshButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 24f, 6f);
            catalogRow.Add(catalogRefreshButton);
            panel.Add(catalogRow);

            _modelCatalogStatusLabel = new Label(string.Empty);
            ApplyPreferredFont(_modelCatalogStatusLabel);
            _modelCatalogStatusLabel.style.color = UiTextSecondary;
            _modelCatalogStatusLabel.style.fontSize = 10f;
            _modelCatalogStatusLabel.style.marginBottom = 4f;
            panel.Add(_modelCatalogStatusLabel);

            UpdateProviderDependentUI();
            return panel;
        }

        /// <summary>
        /// 설정된 카탈로그 URL에서 최신 모델 목록/토큰 예산을 내려받아 팝업과 세션 예산을 갱신합니다.
        /// URL이 비어 있거나 요청이 실패해도 기존 내장/캐시 값으로 계속 동작합니다.
        /// </summary>
        private void RefreshModelCatalog()
        {
            if (_modelCatalogStatusLabel != null)
            {
                _modelCatalogStatusLabel.text = "모델 카탈로그 갱신 중...";
            }

            UniAgentModelCatalog.RequestRefresh((success, message) =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (_modelCatalogStatusLabel != null)
                    {
                        _modelCatalogStatusLabel.text = message;
                    }

                    if (success)
                    {
                        _selectedModel = NormalizeOption(_selectedModel, ModelOptions, DefaultModel);
                        _selectedClaudeModel = NormalizeOption(_selectedClaudeModel, ClaudeModelOptions, DefaultClaudeModel);
                        RefreshSessionTokenBudgetForSelection();
                        SavePrefs();
                        RebuildUI();
                    }
                };
            });
        }

        /// <summary>
        /// CLI 실행 경로를 수동으로 재정의할 수 있는 설정 행을 만듭니다. 값을 비우면 자동 탐지(내장 후보
        /// 목록 + 로그인 셸 <c>command -v</c>/<c>where</c> 해석)로 되돌아갑니다. Homebrew/npm 기본 경로가
        /// 아닌 nvm·volta 등으로 설치했거나 Unity 에디터가 CLI를 찾지 못할 때 직접 경로를 입력하는 우회로입니다.
        /// </summary>
        private VisualElement BuildCliPathRow(string labelText, Func<string> getter, Action<string> setter, string defaultValue, string tooltip)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            var label = new Label(labelText);
            ApplyPreferredFont(label);
            label.style.color = UiTextSecondary;
            label.style.width = 90f;
            label.style.minWidth = 90f;
            label.style.marginRight = 6f;
            row.Add(label);

            var field = new TextField { value = getter() ?? string.Empty };
            field.style.flexGrow = 1f;
            ApplyPreferredFont(field);
            StyleTextFieldInput(field, 6f);
            field.tooltip = tooltip;
            field.RegisterValueChangedCallback(evt =>
            {
                var trimmed = evt.newValue?.Trim() ?? string.Empty;
                setter(string.IsNullOrEmpty(trimmed) ? defaultValue : trimmed);
                SavePrefs();
            });
            row.Add(field);

            return row;
        }

        private VisualElement BuildModeSelector()
        {
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.alignItems = Align.Center;
            modeRow.style.justifyContent = Justify.SpaceBetween;
            modeRow.style.marginBottom = 6f;
            modeRow.style.flexShrink = 0f;

            var modeLeft = new VisualElement();
            modeLeft.style.flexDirection = FlexDirection.Row;
            modeLeft.style.alignItems = Align.Center;
            modeLeft.style.flexShrink = 0f;

            var modeLabel = new Label("Mode:");
            ApplyPreferredFont(modeLabel);
            modeLabel.style.marginRight = 6f;
            modeLabel.style.color = UiTextSecondary;
            modeLeft.Add(modeLabel);

            _planModeButton = new Button(() => SetChatMode(ChatMode.Plan)) { text = "Plan" };
            _planModeButton.style.width = 80f;
            _planModeButton.style.height = 26f;
            _planModeButton.style.minHeight = 26f;
            _planModeButton.style.maxHeight = 26f;
            _planModeButton.style.marginRight = 4f;
            ApplyButtonStyle(_planModeButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 26f, 7f);
            modeLeft.Add(_planModeButton);

            _buildModeButton = new Button(() => SetChatMode(ChatMode.Build)) { text = "Build" };
            _buildModeButton.style.width = 80f;
            _buildModeButton.style.height = 26f;
            _buildModeButton.style.minHeight = 26f;
            _buildModeButton.style.maxHeight = 26f;
            _buildModeButton.style.marginRight = 4f;
            ApplyButtonStyle(_buildModeButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 26f, 7f);
            modeLeft.Add(_buildModeButton);

            _diffModeButton = new Button(ToggleBuildDiffPreviewMode);
            _diffModeButton.style.width = 74f;
            _diffModeButton.style.height = 26f;
            _diffModeButton.style.minHeight = 26f;
            _diffModeButton.style.maxHeight = 26f;
            ApplyButtonStyle(_diffModeButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 26f, 7f);
            modeLeft.Add(_diffModeButton);

            modeRow.Add(modeLeft);
            modeRow.Add(BuildTokenGaugeIndicator(24f, 20f, 8f));

            UpdateModeButtons();
            UpdateTokenGaugeUI();
            return modeRow;
        }

        private void ToggleSettingsPanel()
        {
            if (_settingsPanel == null)
            {
                return;
            }

            var isOpen = _settingsPanel.style.display != DisplayStyle.None;
            _settingsPanel.style.display = isOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>현재 선택된 provider의 사용자 표시 이름을 반환합니다.</summary>
        private string GetProviderDisplayName()
        {
            return _selectedProvider == "Claude Code" ? "Claude" : "Codex";
        }

        private void UpdateProviderDependentUI()
        {
            var isClaudeProvider = _selectedProvider == "Claude Code";
            if (_codexModelRow != null)
            {
                _codexModelRow.style.display = isClaudeProvider ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_claudeModelRow != null)
            {
                _claudeModelRow.style.display = isClaudeProvider ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_reasoningRow != null)
            {
                _reasoningRow.style.display = isClaudeProvider ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_codexCliPathRow != null)
            {
                _codexCliPathRow.style.display = isClaudeProvider ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_claudeCliPathRow != null)
            {
                _claudeCliPathRow.style.display = isClaudeProvider ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void BuildNewSessionPopupPanel()
        {
            _newSessionPopupPanel = new VisualElement();
            _newSessionPopupPanel.style.position = Position.Absolute;
            _newSessionPopupPanel.style.display = DisplayStyle.None;
            _newSessionPopupPanel.style.width = 340f;
            _newSessionPopupPanel.style.minWidth = 340f;
            _newSessionPopupPanel.style.paddingLeft = 12f;
            _newSessionPopupPanel.style.paddingRight = 12f;
            _newSessionPopupPanel.style.paddingTop = 12f;
            _newSessionPopupPanel.style.paddingBottom = 12f;
            ApplyPanelSurfaceStyle(_newSessionPopupPanel, new Color(0.09f, 0.12f, 0.18f, 0.99f), UiPanelBorder, 10f);

            var title = new Label("New Session Name (optional)");
            ApplyPreferredFont(title);
            title.style.fontSize = 13f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = UiTextPrimary;
            title.style.marginBottom = 6f;
            _newSessionPopupPanel.Add(title);

            var hint = new Label("Leave empty and press Create to use default name.");
            ApplyPreferredFont(hint);
            hint.style.fontSize = 11f;
            hint.style.color = UiTextSecondary;
            hint.style.marginBottom = 8f;
            _newSessionPopupPanel.Add(hint);

            _newSessionNameField = new TextField();
            _newSessionNameField.style.height = 28f;
            _newSessionNameField.style.minHeight = 28f;
            _newSessionNameField.style.marginBottom = 10f;
            _newSessionNameField.style.fontSize = 13f;
            _newSessionNameField.tooltip = "Leave empty to use default name.";
            StyleTextFieldInput(_newSessionNameField, 8f);
            _newSessionNameField.RegisterCallback<KeyDownEvent>(OnNewSessionNameFieldKeyDown, TrickleDown.TrickleDown);
            _newSessionPopupPanel.Add(_newSessionNameField);

            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.justifyContent = Justify.FlexEnd;
            actionRow.style.alignItems = Align.Center;

            var cancelButton = new Button(HideNewSessionPopup) { text = "Cancel" };
            cancelButton.style.width = 84f;
            cancelButton.style.height = 28f;
            cancelButton.style.marginRight = 6f;
            ApplyButtonStyle(cancelButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 28f, 8f);
            actionRow.Add(cancelButton);

            _newSessionCreateButton = new Button(CreateSessionFromPopup) { text = "Create" };
            _newSessionCreateButton.style.width = 92f;
            _newSessionCreateButton.style.height = 28f;
            ApplyButtonStyle(_newSessionCreateButton, UiPrimaryButton, UiPrimaryButtonBorder, Color.white, 28f, 8f);
            actionRow.Add(_newSessionCreateButton);

            _newSessionPopupPanel.Add(actionRow);
            rootVisualElement.Add(_newSessionPopupPanel);
        }

        private void ToggleNewSessionPopup()
        {
            if (_isBusy)
            {
                return;
            }

            if (_newSessionPopupPanel == null)
            {
                BuildNewSessionPopupPanel();
            }

            var isOpen = _newSessionPopupPanel.style.display != DisplayStyle.None;
            if (isOpen)
            {
                HideNewSessionPopup();
                return;
            }

            ShowNewSessionPopup();
        }

        private void ShowNewSessionPopup()
        {
            if (_newSessionPopupPanel == null || _newSessionButton == null)
            {
                return;
            }

            var rootRect = rootVisualElement.worldBound;
            var anchorRect = _newSessionButton.worldBound;
            var popupWidth = 340f;
            var popupHeight = 150f;

            var left = anchorRect.xMin - rootRect.xMin;
            var top = anchorRect.yMax - rootRect.yMin + 8f;

            var maxLeft = Mathf.Max(4f, rootVisualElement.resolvedStyle.width - popupWidth - 4f);
            var maxTop = Mathf.Max(4f, rootVisualElement.resolvedStyle.height - popupHeight - 4f);
            left = Mathf.Clamp(left, 4f, maxLeft);
            top = Mathf.Clamp(top, 4f, maxTop);

            _newSessionPopupPanel.style.left = left;
            _newSessionPopupPanel.style.top = top;
            _newSessionPopupPanel.style.display = DisplayStyle.Flex;
            _newSessionPopupPanel.BringToFront();

            if (_newSessionNameField != null)
            {
                _newSessionNameField.SetValueWithoutNotify(string.Empty);
                _newSessionNameField.Focus();
            }
        }

        private void HideNewSessionPopup()
        {
            if (_newSessionPopupPanel != null)
            {
                _newSessionPopupPanel.style.display = DisplayStyle.None;
            }
        }

        private void OnNewSessionNameFieldKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                evt.StopImmediatePropagation();
                HideNewSessionPopup();
                return;
            }

            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            evt.StopPropagation();
            evt.StopImmediatePropagation();
            CreateSessionFromPopup();
        }

        private void CreateSessionFromPopup()
        {
            var preferredName = _newSessionNameField?.value;
            CreateNewChatSession(preferredName);
            HideNewSessionPopup();
        }

        private static string NormalizeSessionName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > 36)
            {
                trimmed = trimmed.Substring(0, 36).TrimEnd();
            }

            return trimmed;
        }

        private void SetChatMode(ChatMode mode)
        {
            if (_chatMode == mode)
            {
                return;
            }

            _chatMode = mode;
            UpdateModeButtons();
            UpdateStatusUI();
            SavePrefs();
        }

        private void UpdateModeButtons()
        {
            ApplyModeButtonStyle(_planModeButton, _chatMode == ChatMode.Plan);
            ApplyModeButtonStyle(_buildModeButton, _chatMode == ChatMode.Build);
            UpdateDiffModeButton();
        }

        private void ToggleBuildDiffPreviewMode()
        {
            _buildDiffPreviewMode = !_buildDiffPreviewMode;
            UpdateDiffModeButton();
            SavePrefs();
        }

        private void UpdateDiffModeButton()
        {
            if (_diffModeButton == null)
            {
                return;
            }

            var buildSelected = _chatMode == ChatMode.Build;
            var active = buildSelected && _buildDiffPreviewMode;
            _diffModeButton.text = _buildDiffPreviewMode ? "Diff On" : "Diff Off";
            _diffModeButton.tooltip = buildSelected
                ? "Build mode diff preview. Shows proposed unified diff in a separate window before apply."
                : "Diff preview is available in Build mode only.";
            _diffModeButton.SetEnabled(buildSelected);
            _diffModeButton.style.backgroundColor = active
                ? UiPrimaryButton
                : UiSecondaryButton;
            _diffModeButton.style.color = buildSelected
                ? UiTextPrimary
                : UiTextSecondary;
            _diffModeButton.style.borderTopColor = active ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            _diffModeButton.style.borderBottomColor = active ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            _diffModeButton.style.borderLeftColor = active ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            _diffModeButton.style.borderRightColor = active ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            _diffModeButton.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        }

    }
}

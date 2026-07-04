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
    /// 토큰 게이지 갱신, 채팅 영역/입력창(Composer) 빌드를 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void UpdateTokenGaugeUI()
        {
            var budget = Mathf.Max(1000, _sessionTokenBudget);
            var used = Mathf.Max(0, _sessionTokenUsed);
            var remaining = Mathf.Max(0, budget - used);
            var ratio = Mathf.Clamp01((float)used / budget);
            var usagePercent = Mathf.Clamp01(ratio) * 100f;
            var tooltipText = BuildTokenGaugeTooltip(used, budget, remaining);

            if (_tokenGauge != null)
            {
                _tokenGauge.Progress = ratio;
                _tokenGauge.FillColor = ratio < 0.6f
                    ? new Color(0.17f, 0.56f, 0.94f, 1f)
                    : (ratio < 0.9f ? new Color(0.93f, 0.65f, 0.20f, 1f) : new Color(0.90f, 0.28f, 0.28f, 1f));
                _tokenGauge.tooltip = tooltipText;
            }

            if (_tokenGaugeHost != null)
            {
                _tokenGaugeHost.tooltip = tooltipText;
            }

            if (_tokenGaugePercentLabel != null)
            {
                _tokenGaugePercentLabel.text = $"{Mathf.RoundToInt(usagePercent)}%";
                _tokenGaugePercentLabel.tooltip = tooltipText;
            }
        }

        private string BuildTokenGaugeTooltip(int used, int budget, int remaining)
        {
            var usageRatio = budget > 0 ? (float)used / budget : 0f;
            var usagePercent = Mathf.Clamp01(usageRatio) * 100f;
            var avgTurn = Mathf.Max(0, GetRecentAverageTurnCost());
            var estimatedTurnsLeft = avgTurn > 0f ? Mathf.FloorToInt(remaining / avgTurn) : -1;

            var sb = new StringBuilder();
            sb.AppendLine($"세션 누적(추정): {FormatTokenCount(used)} / {FormatTokenCount(budget)} ({usagePercent:0.#}%)");
            sb.AppendLine($"남은 토큰(추정): {FormatTokenCount(remaining)}");

            if (estimatedTurnsLeft >= 0)
            {
                sb.AppendLine($"남은 대화 추정: 약 {estimatedTurnsLeft}턴 (최근 {TurnEstimateWindowSize}턴 평균 {FormatTokenCount(Mathf.RoundToInt(avgTurn))}/턴)");
            }
            else
            {
                sb.AppendLine("남은 대화 추정: 계산 중 (턴 데이터 부족)");
            }

            sb.Append($"참고: {GetProviderDisplayName()} 토큰 기반 추정치이며 실제 계정 quota와 다를 수 있습니다.");
            return sb.ToString();
        }

        private float GetRecentAverageTurnCost()
        {
            if (_recentTurnTokenCosts == null || _recentTurnTokenCosts.Count == 0)
            {
                return 0f;
            }

            var sum = 0;
            foreach (var value in _recentTurnTokenCosts)
            {
                sum += Mathf.Max(0, value);
            }

            return sum <= 0 ? 0f : (float)sum / _recentTurnTokenCosts.Count;
        }

        private static string FormatTokenCount(int value)
        {
            var safe = Mathf.Max(0, value);
            if (safe >= 1000000)
            {
                return $"{safe / 1000000f:0.#}M";
            }

            if (safe >= 1000)
            {
                return $"{safe / 1000f:0.#}k";
            }

            return safe.ToString();
        }

        private void AccumulateSessionTokens(UniAgentRunResult result)
        {
            var turnCost = ComputeTurnTokenCost(result);
            if (turnCost <= 0)
            {
                return;
            }

            _sessionTokenUsed = Mathf.Max(0, _sessionTokenUsed + turnCost);
            PushRecentTurnTokenCost(turnCost);
            SavePrefs();
            UpdateTokenGaugeUI();
        }

        private void PushRecentTurnTokenCost(int turnCost)
        {
            if (turnCost <= 0)
            {
                return;
            }

            _recentTurnTokenCosts.Enqueue(turnCost);
            while (_recentTurnTokenCosts.Count > TurnEstimateWindowSize)
            {
                _recentTurnTokenCosts.Dequeue();
            }
        }

        private static int ComputeTurnTokenCost(UniAgentRunResult result)
        {
            if (result == null)
            {
                return 0;
            }

            var input = result.InputTokens.GetValueOrDefault(0);
            var output = result.OutputTokens.GetValueOrDefault(0);
            var ioSum = input + output;
            if (ioSum > 0)
            {
                return ioSum;
            }

            return Mathf.Max(0, result.TotalTokens.GetValueOrDefault(0));
        }

        private static void ApplyModeButtonStyle(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.style.backgroundColor = selected
                ? UiPrimaryButton
                : UiSecondaryButton;
            button.style.color = selected
                ? Color.white
                : UiTextPrimary;
            button.style.borderTopColor = selected ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            button.style.borderBottomColor = selected ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            button.style.borderLeftColor = selected ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            button.style.borderRightColor = selected ? UiPrimaryButtonBorder : UiSecondaryButtonBorder;
            button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private void BuildChatArea()
        {
            _chatScrollView = new ScrollView(ScrollViewMode.Vertical);
            _chatScrollView.style.flexGrow = 1f;
            _chatScrollView.style.flexShrink = 1f;
            _chatScrollView.style.minHeight = 140f;
            _chatScrollView.style.borderBottomColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            _chatScrollView.style.borderBottomWidth = 1f;
            _chatScrollView.style.borderLeftColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            _chatScrollView.style.borderLeftWidth = 1f;
            _chatScrollView.style.borderRightColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            _chatScrollView.style.borderRightWidth = 1f;
            _chatScrollView.style.borderTopColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            _chatScrollView.style.borderTopWidth = 1f;
            _chatScrollView.style.paddingBottom = 6f;
            _chatScrollView.style.paddingTop = 6f;
            rootVisualElement.Add(_chatScrollView);

            var modeRow = BuildModeSelector();
            modeRow.style.marginTop = 8f;
            rootVisualElement.Add(modeRow);

            var composer = BuildComposer();
            composer.style.flexShrink = 0f;
            composer.style.marginTop = 4f;
            rootVisualElement.Add(composer);
        }

        private VisualElement BuildComposer()
        {
            var composer = new VisualElement();
            composer.style.flexDirection = FlexDirection.Row;
            composer.style.alignItems = Align.FlexEnd;
            composer.style.flexShrink = 0f;

            var inputContainer = new VisualElement();
            inputContainer.style.flexDirection = FlexDirection.Column;
            inputContainer.style.flexGrow = 1f;
            inputContainer.style.flexShrink = 1f;
            inputContainer.style.marginRight = 8f;

            _mentionSuggestionPanel = new VisualElement();
            _mentionSuggestionPanel.style.display = DisplayStyle.None;
            _mentionSuggestionPanel.style.flexDirection = FlexDirection.Column;
            _mentionSuggestionPanel.style.maxHeight = 132f;
            _mentionSuggestionPanel.style.marginBottom = 4f;
            _mentionSuggestionPanel.style.overflow = Overflow.Hidden;
            _mentionSuggestionPanel.style.backgroundColor = new Color(0.13f, 0.15f, 0.18f, 0.98f);
            _mentionSuggestionPanel.style.borderTopWidth = 1f;
            _mentionSuggestionPanel.style.borderBottomWidth = 1f;
            _mentionSuggestionPanel.style.borderLeftWidth = 1f;
            _mentionSuggestionPanel.style.borderRightWidth = 1f;
            _mentionSuggestionPanel.style.borderTopColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _mentionSuggestionPanel.style.borderBottomColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _mentionSuggestionPanel.style.borderLeftColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _mentionSuggestionPanel.style.borderRightColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            inputContainer.Add(_mentionSuggestionPanel);

            _inputField = new TextField { multiline = true };
            _inputField.style.flexGrow = 1f;
            _inputField.style.minHeight = 64f;
            _inputField.style.whiteSpace = WhiteSpace.Normal;
            _inputField.style.unityTextAlign = TextAnchor.UpperLeft;
            StyleTextFieldInput(_inputField, 10f);
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            _inputField.RegisterCallback<KeyUpEvent>(OnInputKeyUp, TrickleDown.TrickleDown);
            _inputField.RegisterValueChangedCallback(OnInputValueChanged);
            ConfigureInputFieldWordWrap();
            inputContainer.Add(_inputField);
            inputContainer.schedule.Execute(ConfigureInputFieldWordWrap).ExecuteLater(10);
            composer.Add(inputContainer);

            var rightColumn = new VisualElement();
            rightColumn.style.flexDirection = FlexDirection.Column;
            rightColumn.style.alignItems = Align.Center;
            rightColumn.style.flexShrink = 0f;
            rightColumn.style.width = 96f;

            _sendButton = new Button(SendCurrentInput) { text = "Send" };
            _sendButton.style.width = 96f;
            _sendButton.style.minHeight = 64f;
            _sendButton.style.flexShrink = 0f;
            ApplyButtonStyle(_sendButton, UiPrimaryButton, UiPrimaryButtonBorder, Color.white, 64f, 10f, true);
            rightColumn.Add(_sendButton);

            composer.Add(rightColumn);

            UpdateTokenGaugeUI();

            return composer;
        }

        private VisualElement BuildTokenGaugeIndicator(float hostSize, float gaugeSize, float fontSize)
        {
            _tokenGaugeHost = new VisualElement();
            _tokenGaugeHost.style.width = hostSize;
            _tokenGaugeHost.style.height = hostSize;
            _tokenGaugeHost.style.minWidth = hostSize;
            _tokenGaugeHost.style.minHeight = hostSize;
            _tokenGaugeHost.style.maxWidth = hostSize;
            _tokenGaugeHost.style.maxHeight = hostSize;
            _tokenGaugeHost.style.marginLeft = 8f;
            _tokenGaugeHost.style.alignItems = Align.Center;
            _tokenGaugeHost.style.justifyContent = Justify.Center;
            _tokenGaugeHost.style.position = Position.Relative;
            _tokenGaugeHost.style.backgroundColor = new Color(0.12f, 0.14f, 0.17f, 0.95f);
            _tokenGaugeHost.style.borderTopWidth = 1f;
            _tokenGaugeHost.style.borderBottomWidth = 1f;
            _tokenGaugeHost.style.borderLeftWidth = 1f;
            _tokenGaugeHost.style.borderRightWidth = 1f;
            _tokenGaugeHost.style.borderTopColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _tokenGaugeHost.style.borderBottomColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _tokenGaugeHost.style.borderLeftColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _tokenGaugeHost.style.borderRightColor = new Color(0.28f, 0.33f, 0.40f, 1f);
            _tokenGaugeHost.style.borderTopLeftRadius = hostSize * 0.5f;
            _tokenGaugeHost.style.borderTopRightRadius = hostSize * 0.5f;
            _tokenGaugeHost.style.borderBottomLeftRadius = hostSize * 0.5f;
            _tokenGaugeHost.style.borderBottomRightRadius = hostSize * 0.5f;
            _tokenGaugeHost.style.flexShrink = 0f;

            _tokenGauge = new UniAgentTokenGaugeElement();
            _tokenGauge.style.width = gaugeSize;
            _tokenGauge.style.height = gaugeSize;
            _tokenGauge.pickingMode = PickingMode.Ignore;
            _tokenGaugeHost.Add(_tokenGauge);

            _tokenGaugePercentLabel = new Label("0%");
            ApplyPreferredFont(_tokenGaugePercentLabel);
            _tokenGaugePercentLabel.style.position = Position.Absolute;
            _tokenGaugePercentLabel.style.fontSize = fontSize;
            _tokenGaugePercentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tokenGaugePercentLabel.style.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            _tokenGaugePercentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tokenGaugePercentLabel.style.left = 0f;
            _tokenGaugePercentLabel.style.right = 0f;
            _tokenGaugePercentLabel.style.top = 0f;
            _tokenGaugePercentLabel.style.bottom = 0f;
            _tokenGaugePercentLabel.pickingMode = PickingMode.Ignore;
            _tokenGaugeHost.Add(_tokenGaugePercentLabel);

            return _tokenGaugeHost;
        }

    }
}

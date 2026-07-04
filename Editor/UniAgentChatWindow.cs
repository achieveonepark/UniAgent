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
    /// Codex/Claude Code CLI용 UI Toolkit 채팅 창입니다.
    /// Codex/Claude Code 채팅 창의 필드, 생명주기, 헤더 패널을 담당합니다.
    /// 나머지 책임은 UniAgentChatWindow.*.cs partial 파일로 분리되어 있습니다.
    /// </summary>
    public sealed partial class UniAgentChatWindow : EditorWindow
    {
        private static readonly Regex MarkdownLinkRegex = new Regex("\\[([^\\]]+)\\]\\(([^)]+)\\)", RegexOptions.Compiled);
        private static readonly Regex MarkdownBoldAsteriskRegex = new Regex("(?<!\\*)\\*\\*(.+?)\\*\\*(?!\\*)", RegexOptions.Compiled);
        private static readonly Regex MarkdownBoldUnderscoreRegex = new Regex("(?<!_)__(.+?)__(?!_)", RegexOptions.Compiled);
        private static readonly Regex MarkdownItalicAsteriskRegex = new Regex("(?<!\\*)\\*(?!\\*)(.+?)(?<!\\*)\\*(?!\\*)", RegexOptions.Compiled);
        private static readonly Regex MarkdownItalicUnderscoreRegex = new Regex("(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", RegexOptions.Compiled);
        private static readonly Regex MarkdownCodeRegex = new Regex("`([^`\\n]+)`", RegexOptions.Compiled);
        private static readonly Regex MentionPathRegex = new Regex("@([^\\s@]+)", RegexOptions.Compiled);
        private static readonly Queue<UniAgentDeferredRunResult> DeferredRunResults = new Queue<UniAgentDeferredRunResult>();
        private static readonly object DeferredRunLock = new object();
        private static readonly object ActiveRunCountLock = new object();
        private static int ActiveRunCount;

        private readonly List<UniAgentChatMessage> _messages = new List<UniAgentChatMessage>();
        private readonly List<UniAgentChatSessionInfo> _chatSessions = new List<UniAgentChatSessionInfo>();
        private readonly List<string> _sessionOptionLabels = new List<string>();
        private readonly List<string> _sessionOptionIds = new List<string>();
        private readonly List<string> _projectFileIndex = new List<string>();
        private readonly List<string> _mentionSuggestions = new List<string>();
        private readonly List<Button> _mentionSuggestionButtonPool = new List<Button>();

        // UI references.
        private ScrollView _chatScrollView;
        private TextField _inputField;
        private PopupField<string> _sessionPopup;
        private Button _newSessionButton;
        private VisualElement _newSessionPopupPanel;
        private TextField _newSessionNameField;
        private Button _newSessionCreateButton;
        private Label _availabilityLabel;
        private Label _settingsStateLabel;
        private Button _sendButton;
        private Button _settingsButton;
        private Button _planModeButton;
        private Button _buildModeButton;
        private Button _diffModeButton;
        private UniAgentTokenGaugeElement _tokenGauge;
        private VisualElement _tokenGaugeHost;
        private Label _tokenGaugePercentLabel;
        private VisualElement _availabilityDot;
        private VisualElement _settingsStateDot;
        private VisualElement _settingsPanel;
        private VisualElement _mentionSuggestionPanel;
        private VisualElement _codexModelRow;
        private VisualElement _claudeModelRow;
        private VisualElement _reasoningRow;
        private Label _modelCatalogStatusLabel;

        // Persisted/runtime options.
        private string _cliPath = UniAgentCliConstants.DefaultCliPath;
        private string _claudeCliPath = UniAgentCliConstants.DefaultClaudeCliPath;
        private readonly bool _useProjectCodexHome = false;
        private readonly string _projectCodexHomeRelative = UniAgentCliConstants.DefaultCodexHomeRelative;
        private readonly bool _fullAuto = true;
        private string _markdownFiles = UniAgentCliConstants.DefaultMarkdownFiles;
        private int _maxMarkdownChars = UniAgentCliConstants.DefaultMaxMarkdownChars;
        private bool _disableDomainReloadOnPlay = true;
        private bool _disableSceneReloadOnPlay;
        private bool _manualRefreshMode = true;
        private bool _buildDiffPreviewMode;
        private string _selectedModel = DefaultModel;
        private string _selectedReasoningEffort = DefaultReasoningEffort;
        private string _selectedProvider = DefaultProvider;
        private string _selectedClaudeModel = DefaultClaudeModel;

        // UI/session state.
        private bool _isBusy;
        private bool _autoRefreshLocked;
        private string _sessionId = string.Empty;
        private string _activeChatSessionId = string.Empty;
        private string _statusText = "Ready";
        private bool _codexInstalled;
        private bool _codexLoggedIn;
        private string _codexVersionText = "Unknown";
        private string _loginStatusText = "Unknown";
        private string _lastTokenUsageText = "-";
        private ChatMode _chatMode = ChatMode.Build;
        private int _sessionTokenUsed;
        private int _sessionTokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget;
        private readonly Queue<int> _recentTurnTokenCosts = new Queue<int>();
        /// <summary>세션 토큰 사용량이 예산의 이 비율을 넘으면 대화를 요약하고 새 CLI 스레드로 이어갑니다.</summary>
        private const float AutoCompactThresholdRatio = 0.8f;
        /// <summary>자동 압축으로 만든 대화 요약입니다. 다음 프롬프트에 한 번 삽입된 뒤 비워집니다.</summary>
        private string _pendingCompactedContext = string.Empty;

        // Pending assistant animation state.
        private UniAgentChatMessage _pendingAssistantMessage;
        private IVisualElementScheduledItem _pendingAnimationItem;
        private int _pendingDotCount;
        private double _pendingStartRealtime;
        private string _pendingProgressText = "Preparing request";
        private readonly List<string> _pendingProgressLines = new List<string>();
        private readonly object _progressUpdateLock = new object();
        private string _queuedProgressText;
        private bool _progressDispatchPending;
        private int _activeMentionStartIndex = -1;
        private string _activeMentionQuery = string.Empty;
        private int _selectedMentionSuggestionIndex = -1;
        private bool _suppressMentionRefresh;
        private bool _suppressNextMentionSubmitKeyUp;
        private bool _deferredMentionCommitPending;
        private bool _suppressSessionChangeEvent;
        private bool _projectFileIndexDirty = true;
        private double _nextProjectFileIndexRefreshAt;

        // Font fallback for Korean/Unicode rendering.
        private static Font _preferredUiFont;
        private static Texture2D _settingsIconTexture;
        private const string DefaultUiFontFamily = "SUIT Variable";
        private static readonly string[] PreferredUiFontCandidates =
        {
            DefaultUiFontFamily,
            "Pretendard Variable",
            "Pretendard",
            "SUIT",
            "Apple SD Gothic Neo",
            "SF Pro Text",
            "SF Pro Display",
            "Noto Sans KR",
            "Noto Sans CJK KR",
            "Malgun Gothic",
            "Arial Unicode MS"
        };
        private const int TurnEstimateWindowSize = 5;
        private const int MaxMentionSuggestions = 6;
        private const int MaxTargetedFilesPerTurn = 5;
        private const int MaxTargetedFileChars = 2800;
        private const float MentionSuggestionItemHeight = 22f;
        private const string BaseFieldInputClassName = "unity-base-field__input";
        private const string TextInputClassName = "unity-text-input";
        private const string BaseTextFieldInputClassName = "unity-base-text-field__input";
        private static string DefaultModel => UniAgentModelCatalog.GetDefaultModel(CliProvider.Codex);
        private const string DefaultReasoningEffort = "xhigh";
        private const string DefaultProvider = "Codex";
        private static string DefaultClaudeModel => UniAgentModelCatalog.GetDefaultModel(CliProvider.ClaudeCode);
        private static readonly Color UiTextPrimary = new Color(0.93f, 0.95f, 0.98f, 1f);
        private static readonly Color UiTextSecondary = new Color(0.73f, 0.78f, 0.86f, 1f);
        private static readonly Color UiPanelBackground = new Color(0.10f, 0.13f, 0.18f, 0.95f);
        private static readonly Color UiPanelBorder = new Color(0.30f, 0.37f, 0.49f, 1f);
        private static readonly Color UiControlBackground = new Color(0.16f, 0.19f, 0.25f, 0.96f);
        private static readonly Color UiControlBorder = new Color(0.33f, 0.41f, 0.54f, 1f);
        private static readonly Color UiSecondaryButton = new Color(0.19f, 0.23f, 0.30f, 1f);
        private static readonly Color UiSecondaryButtonBorder = new Color(0.35f, 0.44f, 0.58f, 1f);
        private static readonly Color UiPrimaryButton = new Color(0.15f, 0.45f, 0.87f, 1f);
        private static readonly Color UiPrimaryButtonBorder = new Color(0.29f, 0.59f, 1f, 1f);
        private static readonly Color UiDangerButton = new Color(0.60f, 0.23f, 0.23f, 1f);
        private static readonly Color UiDangerButtonBorder = new Color(0.76f, 0.33f, 0.33f, 1f);
        private static readonly List<string> ProviderOptions = new List<string>
        {
            "Codex",
            "Claude Code"
        };
        /// <summary>Codex 모델 선택 옵션입니다. <see cref="UniAgentModelCatalog"/>에서 조회하며, 원격/캐시 갱신 시 최신 값을 반영합니다.</summary>
        private static List<string> ModelOptions => UniAgentModelCatalog.GetModels(CliProvider.Codex);
        /// <summary>Claude Code 모델 선택 옵션입니다. <see cref="UniAgentModelCatalog"/>에서 조회하며, 원격/캐시 갱신 시 최신 값을 반영합니다.</summary>
        private static List<string> ClaudeModelOptions => UniAgentModelCatalog.GetModels(CliProvider.ClaudeCode);
        private static readonly List<string> ReasoningEffortOptions = new List<string>
        {
            "none",
            "minimal",
            "low",
            "medium",
            "high",
            "xhigh"
        };

        /// <summary>
        /// Codex 채팅 창을 엽니다.
        /// </summary>
        [MenuItem("Tools/UniAgent/UniAgent Chat")]
        public static void OpenWindow()
        {
            var window = GetWindow<UniAgentChatWindow>();
            window.titleContent = new GUIContent("UniAgent Chat");
            window.minSize = new Vector2(600f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            LoadChatHistory();
            MarkProjectFileIndexDirty();

            if (HasActiveRuns())
            {
                _isBusy = true;
                _statusText = $"{GetProviderDisplayName()} is thinking...";
                UniAgentToolbarShortcut.SetBusyState();
            }

            ApplyDeferredRunResults();
            SynchronizeBusyState();

            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseAutoRefreshLock;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAutoRefreshLock;

            EditorApplication.quitting -= ReleaseAutoRefreshLock;
            EditorApplication.quitting += ReleaseAutoRefreshLock;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;

            ApplyEnterPlayModeSettings(false);
            if (_manualRefreshMode)
            {
                SetAutoRefreshLock(true);
            }
        }

        private void OnDisable()
        {
            SavePrefs();
            SaveChatHistory();
            ReleaseAutoRefreshLock();
            StopPendingAssistantAnimation();

            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseAutoRefreshLock;
            EditorApplication.quitting -= ReleaseAutoRefreshLock;
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        /// <summary>
        /// 에디터가 창 시각 요소를 만들 때 UI Toolkit 트리를 구성합니다.
        /// </summary>
        public void CreateGUI()
        {
            RebuildUI();
            SynchronizeBusyState();
            EnsurePendingAssistantForActiveRun();
            RefreshEnvironmentState(true);
        }

        private void RebuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.style.paddingBottom = 8f;
            rootVisualElement.style.paddingLeft = 8f;
            rootVisualElement.style.paddingRight = 8f;
            rootVisualElement.style.paddingTop = 8f;
            ApplyPreferredFont(rootVisualElement);

            BuildHeaderPanel();
            BuildChatArea();
            RefreshChatUI();
            UpdateEnvironmentUI();
            UpdateStatusUI();
        }

        private void BuildHeaderPanel()
        {
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.SpaceBetween;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 6f;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems = Align.Center;
            left.style.flexShrink = 1f;

            var title = new Label("UniAgent Chat");
            ApplyPreferredFont(title);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14f;
            title.style.marginRight = 8f;
            left.Add(title);

            _sessionPopup = new PopupField<string>(new List<string> { "1. Session 1" }, 0);
            _sessionPopup.style.width = 168f;
            _sessionPopup.style.minWidth = 148f;
            _sessionPopup.style.marginRight = 4f;
            ApplyPopupFieldStyle(_sessionPopup, 24f);
            _sessionPopup.RegisterValueChangedCallback(OnSessionPopupChanged);
            left.Add(_sessionPopup);

            _newSessionButton = new Button(ToggleNewSessionPopup) { text = "+" };
            _newSessionButton.tooltip = "Create a new chat session";
            _newSessionButton.style.width = 24f;
            _newSessionButton.style.height = 24f;
            _newSessionButton.style.minWidth = 24f;
            _newSessionButton.style.minHeight = 24f;
            _newSessionButton.style.paddingLeft = 0f;
            _newSessionButton.style.paddingRight = 0f;
            _newSessionButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            ApplyButtonStyle(_newSessionButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 24f, 6f);
            left.Add(_newSessionButton);

            var right = new VisualElement();
            right.style.flexDirection = FlexDirection.Row;
            right.style.alignItems = Align.Center;

            _availabilityDot = CreateStatusDot(10f);
            _availabilityDot.style.marginRight = 6f;
            right.Add(_availabilityDot);

            _availabilityLabel = new Label("Not Ready");
            ApplyPreferredFont(_availabilityLabel);
            _availabilityLabel.style.color = UiTextPrimary;
            _availabilityLabel.style.marginRight = 8f;
            right.Add(_availabilityLabel);

            _settingsButton = new Button(ToggleSettingsPanel) { tooltip = "Settings" };
            _settingsButton.text = string.Empty;
            _settingsButton.style.width = 26f;
            _settingsButton.style.height = 24f;
            _settingsButton.style.minWidth = 26f;
            _settingsButton.style.minHeight = 24f;
            _settingsButton.style.paddingLeft = 0f;
            _settingsButton.style.paddingRight = 0f;
            _settingsButton.style.flexShrink = 0f;
            _settingsButton.style.justifyContent = Justify.Center;
            _settingsButton.style.alignItems = Align.Center;
            ApplyButtonStyle(_settingsButton, UiSecondaryButton, UiSecondaryButtonBorder, UiTextPrimary, 24f, 6f);

            var settingsIcon = GetSettingsIconTexture();
            if (settingsIcon != null)
            {
                var icon = new Image
                {
                    image = settingsIcon,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                icon.style.width = 14f;
                icon.style.height = 14f;
                icon.style.unityBackgroundImageTintColor = UiTextPrimary;
                _settingsButton.Add(icon);
            }
            else
            {
                _settingsButton.text = "S";
            }

            right.Add(_settingsButton);

            topRow.Add(left);
            topRow.Add(right);
            rootVisualElement.Add(topRow);

            _settingsPanel = BuildSettingsPanel();
            _settingsPanel.style.display = DisplayStyle.None;
            rootVisualElement.Add(_settingsPanel);
            BuildNewSessionPopupPanel();
            RefreshSessionPopupChoices();
        }

    }
}

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
    /// 채팅 이력 저장/복원, UniAgent.Client 실행 브리지, 옵션 정규화 헬퍼를 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void LoadChatHistory()
        {
            _chatSessions.Clear();
            _messages.Clear();
            var historyPath = GetChatHistoryPath();
            if (!File.Exists(historyPath))
            {
                EnsureSessionCollectionInitialized();
                var firstSession = _chatSessions.Count > 0 ? _chatSessions[0] : null;
                if (firstSession != null)
                {
                    _activeChatSessionId = firstSession.Id;
                    ApplySessionToRuntime(firstSession);
                }
                return;
            }

            try
            {
                var json = File.ReadAllText(historyPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    EnsureSessionCollectionInitialized();
                    var first = _chatSessions.Count > 0 ? _chatSessions[0] : null;
                    if (first != null)
                    {
                        _activeChatSessionId = first.Id;
                        ApplySessionToRuntime(first);
                    }
                    return;
                }

                var state = JsonUtility.FromJson<UniAgentChatHistoryState>(json);
                if (state == null)
                {
                    EnsureSessionCollectionInitialized();
                    var first = _chatSessions.Count > 0 ? _chatSessions[0] : null;
                    if (first != null)
                    {
                        _activeChatSessionId = first.Id;
                        ApplySessionToRuntime(first);
                    }
                    return;
                }

                var loadedFromSessions = state.Sessions != null && state.Sessions.Count > 0;
                if (loadedFromSessions)
                {
                    for (var i = 0; i < state.Sessions.Count; i++)
                    {
                        var session = state.Sessions[i];
                        if (session == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(session.Id))
                        {
                            session.Id = Guid.NewGuid().ToString("N");
                        }

                        if (string.IsNullOrWhiteSpace(session.Name))
                        {
                            session.Name = $"Session {_chatSessions.Count + 1}";
                        }

                        if (session.Messages == null)
                        {
                            session.Messages = new List<UniAgentChatHistoryItem>();
                        }

                        if (session.RecentTurnTokenCosts == null)
                        {
                            session.RecentTurnTokenCosts = new List<int>();
                        }

                        _chatSessions.Add(session);
                    }
                }

                if (!loadedFromSessions)
                {
                    var fallback = new UniAgentChatSessionInfo
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = "Session 1",
                        Messages = state.Messages ?? new List<UniAgentChatHistoryItem>()
                    };
                    _chatSessions.Add(fallback);
                }

                EnsureSessionCollectionInitialized();
                var requestedActiveId = !string.IsNullOrWhiteSpace(_activeChatSessionId)
                    ? _activeChatSessionId
                    : state.ActiveSessionId;
                _activeChatSessionId = string.IsNullOrWhiteSpace(requestedActiveId) ? _chatSessions[0].Id : requestedActiveId;
                var active = GetActiveChatSession() ?? _chatSessions[0];
                _activeChatSessionId = active.Id;
                ApplySessionToRuntime(active);
            }
            catch
            {
                // Ignore history parse/read failures and start with an empty chat.
                _chatSessions.Clear();
                _messages.Clear();
                EnsureSessionCollectionInitialized();
                var session = _chatSessions.Count > 0 ? _chatSessions[0] : null;
                if (session != null)
                {
                    _activeChatSessionId = session.Id;
                    ApplySessionToRuntime(session);
                }
            }
        }

        /// <summary>
        /// 창을 다시 열었을 때 이전 메시지를 복원할 수 있도록 채팅 이력을 저장합니다.
        /// </summary>
        private void SaveChatHistory()
        {
            EnsureSessionCollectionInitialized();
            SaveActiveSessionSnapshot();

            var state = new UniAgentChatHistoryState();
            state.ActiveSessionId = _activeChatSessionId;
            for (var i = 0; i < _chatSessions.Count; i++)
            {
                var session = _chatSessions[i];
                if (session == null)
                {
                    continue;
                }

                var copy = new UniAgentChatSessionInfo
                {
                    Id = session.Id,
                    Name = session.Name,
                    CodexSessionId = session.CodexSessionId,
                    TokenUsed = session.TokenUsed,
                    Messages = new List<UniAgentChatHistoryItem>(),
                    RecentTurnTokenCosts = new List<int>()
                };

                if (session.Messages != null)
                {
                    for (var msgIndex = 0; msgIndex < session.Messages.Count; msgIndex++)
                    {
                        var item = session.Messages[msgIndex];
                        if (item == null)
                        {
                            continue;
                        }

                        copy.Messages.Add(new UniAgentChatHistoryItem
                        {
                            Role = item.Role,
                            Text = item.Text,
                            Time = item.Time,
                            TokenSummary = item.TokenSummary
                        });
                    }
                }

                if (session.RecentTurnTokenCosts != null)
                {
                    for (var costIndex = 0; costIndex < session.RecentTurnTokenCosts.Count; costIndex++)
                    {
                        copy.RecentTurnTokenCosts.Add(Mathf.Max(0, session.RecentTurnTokenCosts[costIndex]));
                    }
                }

                state.Sessions.Add(copy);
            }

            state.Messages = new List<UniAgentChatHistoryItem>();
            var active = GetActiveChatSession();
            if (active?.Messages != null)
            {
                for (var i = 0; i < active.Messages.Count; i++)
                {
                    var item = active.Messages[i];
                    if (item == null)
                    {
                        continue;
                    }

                    state.Messages.Add(new UniAgentChatHistoryItem
                    {
                        Role = item.Role,
                        Text = item.Text,
                        Time = item.Time,
                        TokenSummary = item.TokenSummary
                    });
                }
            }

            try
            {
                var historyPath = GetChatHistoryPath();
                var directory = Path.GetDirectoryName(historyPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonUtility.ToJson(state);
                File.WriteAllText(historyPath, json);
            }
            catch
            {
                // Ignore write failures; chat still works without persistence.
            }
        }

        private string GetChatHistoryPath()
        {
            return Path.Combine(UniAgentChatHelper.GetProjectRootPath(), "Library", UniAgentCliConstants.ChatHistoryFileName);
        }

        private static ChatRole ParseChatRole(int rawValue)
        {
            switch (rawValue)
            {
                case (int)ChatRole.User:
                    return ChatRole.User;
                case (int)ChatRole.Assistant:
                    return ChatRole.Assistant;
                case (int)ChatRole.System:
                    return ChatRole.System;
                case (int)ChatRole.Error:
                    return ChatRole.Error;
                default:
                    return ChatRole.Assistant;
            }
        }

        private static int GetOptionIndex(string selected, List<string> options, string fallback)
        {
            if (options == null || options.Count == 0)
            {
                return 0;
            }

            var resolved = NormalizeOption(selected, options, fallback);
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], resolved, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private static string NormalizeOption(string selected, List<string> options, string fallback)
        {
            if (options == null || options.Count == 0)
            {
                return fallback ?? string.Empty;
            }

            var trimmed = string.IsNullOrWhiteSpace(selected) ? string.Empty : selected.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                for (var i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i], trimmed, StringComparison.Ordinal))
                    {
                        return options[i];
                    }
                }
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], fallback, StringComparison.Ordinal))
                {
                    return options[i];
                }
            }

            return options[0];
        }

        // -------------------------
        // Local utility wrappers
        // -------------------------

        private UniAgent.IClient ConfigureUniAgentClient(bool? fullAutoOverride = null)
        {
            UniAgent.ConfigureClient(new UniAgentEditorCliClientAdapter(() => CreateCliService(fullAutoOverride)));
            return UniAgent.Client;
        }

        private Task<UniAgentRunResult> RunThroughUniAgentClient(string prompt, bool? fullAutoOverride = null)
        {
            var client = ConfigureUniAgentClient(fullAutoOverride);
            var fullAutoForCurrentMode = _chatMode == ChatMode.Build && _fullAuto;
            if (fullAutoOverride.HasValue)
            {
                fullAutoForCurrentMode = fullAutoOverride.Value;
            }

            var request = new UniAgentClientRunRequest
            {
                Prompt = prompt,
                SessionId = _sessionId ?? string.Empty,
                Model = NormalizeOption(_selectedModel, ModelOptions, DefaultModel),
                ReasoningEffort = NormalizeOption(_selectedReasoningEffort, ReasoningEffortOptions, DefaultReasoningEffort),
                FullAuto = fullAutoForCurrentMode,
                ProgressCallback = QueueCliProgressUpdate
            };

            return client.RunAsync(request).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    return UniAgentRunResult.FromError(task.Exception?.GetBaseException().Message ?? "Unknown execution error");
                }

                return ToLegacyRunResult(task.Result);
            });
        }

        private static UniAgentCommandResult ToLegacyCommandResult(UniAgentResult result)
        {
            return new UniAgentCommandResult
            {
                Success = result != null && result.Success,
                Message = result == null ? "Unknown command result." : result.Message
            };
        }

        private static UniAgentRunResult ToLegacyRunResult(UniAgentClientRunResult result)
        {
            if (result == null)
            {
                return UniAgentRunResult.FromError("Run result is null.");
            }

            return new UniAgentRunResult
            {
                Success = result.Success,
                Message = result.Message,
                ThreadId = result.SessionId,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                TotalTokens = result.TotalTokens
            };
        }

        private UniAgentCliService CreateCliService(bool? fullAutoOverride = null)
        {
            var fullAutoForCurrentMode = _chatMode == ChatMode.Build && _fullAuto;
            if (fullAutoOverride.HasValue)
            {
                fullAutoForCurrentMode = fullAutoOverride.Value;
            }

            var provider = _selectedProvider == "Claude Code" ? CliProvider.ClaudeCode : CliProvider.Codex;
            var cliPath = provider == CliProvider.ClaudeCode ? _claudeCliPath : _cliPath;
            var model = provider == CliProvider.ClaudeCode
                ? NormalizeOption(_selectedClaudeModel, ClaudeModelOptions, DefaultClaudeModel)
                : NormalizeOption(_selectedModel, ModelOptions, DefaultModel);
            var reasoningEffort = provider == CliProvider.ClaudeCode
                ? string.Empty
                : NormalizeOption(_selectedReasoningEffort, ReasoningEffortOptions, DefaultReasoningEffort);

            return new UniAgentCliService(
                cliPath,
                UniAgentChatHelper.GetProjectRootPath(),
                _useProjectCodexHome,
                GetProjectCodexHome(),
                fullAutoForCurrentMode,
                provider == CliProvider.ClaudeCode && _claudeAutoAcceptEdits,
                model,
                reasoningEffort,
                UniAgentCliConstants.DefaultExecTimeoutMs,
                provider);
        }

        private string GetProjectCodexHome()
        {
            return UniAgentChatHelper.GetProjectCodexHome(_projectCodexHomeRelative);
        }

    }
}

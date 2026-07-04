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
    /// 다중 채팅 세션 생성/전환/저장 로직을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void CreateNewChatSession(string preferredName = null)
        {
            if (_isBusy)
            {
                AddMessage(ChatRole.System, "Cannot create a new session while a request is running.");
                return;
            }

            SaveActiveSessionSnapshot();

            var normalizedName = NormalizeSessionName(preferredName);
            var session = new UniAgentChatSessionInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(normalizedName) ? $"Session {_chatSessions.Count + 1}" : normalizedName
            };

            _chatSessions.Add(session);
            SwitchToSession(session.Id);
            SetStatus("Ready");
        }

        private void OnSessionPopupChanged(ChangeEvent<string> evt)
        {
            if (_suppressSessionChangeEvent || _sessionPopup == null)
            {
                return;
            }

            var selectedLabel = evt.newValue ?? string.Empty;
            var index = _sessionOptionLabels.IndexOf(selectedLabel);
            if (index < 0 || index >= _sessionOptionIds.Count)
            {
                return;
            }

            var sessionId = _sessionOptionIds[index];
            if (string.IsNullOrWhiteSpace(sessionId) || string.Equals(sessionId, _activeChatSessionId, StringComparison.Ordinal))
            {
                return;
            }

            SwitchToSession(sessionId);
        }

        private void SwitchToSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var target = _chatSessions.Find(s => string.Equals(s?.Id, sessionId, StringComparison.Ordinal));
            if (target == null)
            {
                return;
            }

            SaveActiveSessionSnapshot();
            _activeChatSessionId = target.Id;
            ApplySessionToRuntime(target);
            StopPendingAssistantAnimation();
            HideMentionSuggestions();
            HideNewSessionPopup();
            RefreshSessionPopupChoices();
            RefreshChatUI();
            UpdateTokenGaugeUI();
            UpdateStatusUI();
            SaveChatHistory();
            SavePrefs();
        }

        private void RefreshSessionPopupChoices()
        {
            if (_sessionPopup == null)
            {
                return;
            }

            _sessionOptionLabels.Clear();
            _sessionOptionIds.Clear();

            for (var i = 0; i < _chatSessions.Count; i++)
            {
                var session = _chatSessions[i];
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
                    session.Name = $"Session {i + 1}";
                }

                _sessionOptionIds.Add(session.Id);
                _sessionOptionLabels.Add($"{i + 1}. {session.Name}");
            }

            if (_sessionOptionLabels.Count == 0)
            {
                var fallback = new UniAgentChatSessionInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Session 1"
                };
                _chatSessions.Add(fallback);
                _sessionOptionIds.Add(fallback.Id);
                _sessionOptionLabels.Add("1. Session 1");
            }

            if (string.IsNullOrWhiteSpace(_activeChatSessionId))
            {
                _activeChatSessionId = _sessionOptionIds[0];
            }

            var activeIndex = _sessionOptionIds.IndexOf(_activeChatSessionId);
            if (activeIndex < 0)
            {
                activeIndex = 0;
                _activeChatSessionId = _sessionOptionIds[0];
            }

            _suppressSessionChangeEvent = true;
            _sessionPopup.choices.Clear();
            for (var i = 0; i < _sessionOptionLabels.Count; i++)
            {
                _sessionPopup.choices.Add(_sessionOptionLabels[i]);
            }
            _sessionPopup.SetValueWithoutNotify(_sessionOptionLabels[Mathf.Clamp(activeIndex, 0, _sessionOptionLabels.Count - 1)]);
            _suppressSessionChangeEvent = false;
        }

        private void EnsureSessionCollectionInitialized()
        {
            if (_chatSessions.Count > 0)
            {
                return;
            }

            var session = new UniAgentChatSessionInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Session 1"
            };

            for (var i = 0; i < _messages.Count; i++)
            {
                var message = _messages[i];
                if (message == null || message.IsLoading)
                {
                    continue;
                }

                session.Messages.Add(new UniAgentChatHistoryItem
                {
                    Role = (int)message.Role,
                    Text = message.Text,
                    Time = message.Time,
                    TokenSummary = message.TokenSummary
                });
            }

            session.CodexSessionId = _sessionId ?? string.Empty;
            session.TokenUsed = _sessionTokenUsed;
            foreach (var turnCost in _recentTurnTokenCosts)
            {
                session.RecentTurnTokenCosts.Add(Mathf.Max(0, turnCost));
            }

            _chatSessions.Add(session);
            _activeChatSessionId = session.Id;
        }

        private UniAgentChatSessionInfo GetActiveChatSession()
        {
            if (string.IsNullOrWhiteSpace(_activeChatSessionId))
            {
                return null;
            }

            return _chatSessions.Find(s => s != null && string.Equals(s.Id, _activeChatSessionId, StringComparison.Ordinal));
        }

        private void SaveActiveSessionSnapshot()
        {
            var active = GetActiveChatSession();
            if (active == null)
            {
                return;
            }

            active.CodexSessionId = _sessionId ?? string.Empty;
            active.TokenUsed = Mathf.Max(0, _sessionTokenUsed);
            active.Messages.Clear();
            active.RecentTurnTokenCosts.Clear();

            foreach (var turnCost in _recentTurnTokenCosts)
            {
                active.RecentTurnTokenCosts.Add(Mathf.Max(0, turnCost));
            }

            for (var i = 0; i < _messages.Count; i++)
            {
                var message = _messages[i];
                if (message == null || message.IsLoading)
                {
                    continue;
                }

                active.Messages.Add(new UniAgentChatHistoryItem
                {
                    Role = (int)message.Role,
                    Text = message.Text,
                    Time = message.Time,
                    TokenSummary = message.TokenSummary
                });
            }
        }

        private void ApplySessionToRuntime(UniAgentChatSessionInfo session)
        {
            if (session == null)
            {
                return;
            }

            _messages.Clear();
            var sourceMessages = session.Messages ?? new List<UniAgentChatHistoryItem>();
            for (var i = 0; i < sourceMessages.Count; i++)
            {
                var item = sourceMessages[i];
                if (item == null)
                {
                    continue;
                }

                _messages.Add(new UniAgentChatMessage
                {
                    Role = ParseChatRole(item.Role),
                    Text = item.Text ?? string.Empty,
                    Time = string.IsNullOrWhiteSpace(item.Time) ? DateTime.Now.ToString("HH:mm:ss") : item.Time,
                    TokenSummary = item.TokenSummary,
                    IsLoading = false
                });
            }

            _sessionId = session.CodexSessionId ?? string.Empty;
            _sessionTokenUsed = Mathf.Max(0, session.TokenUsed);
            _recentTurnTokenCosts.Clear();
            if (session.RecentTurnTokenCosts != null)
            {
                for (var i = 0; i < session.RecentTurnTokenCosts.Count; i++)
                {
                    _recentTurnTokenCosts.Enqueue(Mathf.Max(0, session.RecentTurnTokenCosts[i]));
                }
            }
        }

        // -------------------------
        // Status + prefs
        // -------------------------

    }
}

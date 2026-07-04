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
    /// Codex/Claude CLI 설치·로그인 상태 확인 및 Device Auth 로그인/로그아웃을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private void RefreshEnvironmentState()
        {
            RefreshEnvironmentState(false);
        }

        private void RefreshEnvironmentState(bool allowWhileBusy)
        {
            if (_isBusy && !allowWhileBusy)
            {
                return;
            }

            if (!_isBusy)
            {
                SetStatus("Checking CLI and login status...");
            }
            Task.Run(() =>
            {
                var service = CreateCliService();
                return service.RefreshEnvironmentState();
            }).ContinueWith(task =>
            {
                var state = task.IsFaulted
                    ? new UniAgentEnvironmentState
                    {
                        Installed = false,
                        LoggedIn = false,
                        VersionText = "Unknown",
                        LoginText = task.Exception?.GetBaseException().Message ?? "Status check failed."
                    }
                    : task.Result;

                EditorApplication.delayCall += () =>
                {
                    _codexInstalled = state.Installed;
                    _codexLoggedIn = state.LoggedIn;
                    _codexVersionText = state.VersionText;
                    _loginStatusText = state.LoginText;
                    if (!string.IsNullOrWhiteSpace(state.ResolvedCliPath))
                    {
                        if (_selectedProvider == "Claude Code")
                        {
                            if (!string.Equals(_claudeCliPath, state.ResolvedCliPath, StringComparison.Ordinal))
                            {
                                _claudeCliPath = state.ResolvedCliPath;
                                SavePrefs();
                            }
                        }
                        else if (!string.Equals(_cliPath, state.ResolvedCliPath, StringComparison.Ordinal))
                        {
                            _cliPath = state.ResolvedCliPath;
                            SavePrefs();
                        }
                    }

                    UpdateEnvironmentUI();
                    if (!_isBusy)
                    {
                        SetStatus("Ready");
                    }
                };
            });
        }

        private void RefreshLoginState()
        {
            if (!_codexInstalled)
            {
                RefreshEnvironmentState();
                return;
            }

            if (_isBusy)
            {
                return;
            }

            SetStatus("Checking login status...");
            Task.Run(() =>
            {
                var service = CreateCliService();
                var loginResult = service.QueryLoginStatus();
                return new UniAgentEnvironmentState
                {
                    Installed = _codexInstalled,
                    LoggedIn = loginResult.Success,
                    VersionText = _codexVersionText,
                    LoginText = loginResult.Message
                };
            }).ContinueWith(task =>
            {
                var state = task.IsFaulted
                    ? new UniAgentEnvironmentState
                    {
                        Installed = _codexInstalled,
                        LoggedIn = false,
                        VersionText = _codexVersionText,
                        LoginText = task.Exception?.GetBaseException().Message ?? "Login check failed."
                    }
                    : task.Result;

                EditorApplication.delayCall += () =>
                {
                    _codexLoggedIn = state.LoggedIn;
                    _loginStatusText = state.LoginText;
                    UpdateEnvironmentUI();
                    SetStatus("Ready");
                };
            });
        }

        private void LoginWithDeviceAuth()
        {
            if (_isBusy)
            {
                return;
            }

            var isClaudeProvider = _selectedProvider == "Claude Code";
            var cliDisplayName = isClaudeProvider ? "Claude Code CLI" : "Codex CLI";
            var loginTerminalCmd = isClaudeProvider ? "claude auth login" : "codex login --device-auth";

            if (!_codexInstalled)
            {
                AddMessage(ChatRole.Error, $"{cliDisplayName} was not found. Install it, then click Refresh in Settings.");
                return;
            }

            AddMessage(ChatRole.System, $"Starting {cliDisplayName} login. Complete browser auth, then click Refresh in Settings.");
            SetStatus("Starting login...");
            ConfigureUniAgentClient();
            UniAgent.Client.LoginAsync(new UniAgentLoginRequest { UseDeviceAuth = true }).ContinueWith(task =>
            {
                var result = task.IsFaulted
                    ? new UniAgentCommandResult
                    {
                        Success = false,
                        Message = task.Exception?.GetBaseException().Message ?? "Login failed."
                    }
                    : ToLegacyCommandResult(task.Result);

                EditorApplication.delayCall += () =>
                {
                    if (result.Success)
                    {
                        AddMessage(ChatRole.System, $"{cliDisplayName} login completed.");
                    }
                    else
                    {
                        AddMessage(ChatRole.Error, $"{cliDisplayName} login failed: {result.Message}\nYou can also run `{loginTerminalCmd}` in terminal.");
                    }

                    SetStatus("Ready");
                    RefreshLoginState();
                };
            });
        }

        private void LogoutCodex()
        {
            if (_isBusy || !_codexInstalled)
            {
                return;
            }

            SetBusy(true, "Logging out...");
            ConfigureUniAgentClient();
            UniAgent.Client.LogoutAsync().ContinueWith(task =>
            {
                var result = task.IsFaulted
                    ? new UniAgentCommandResult
                    {
                        Success = false,
                        Message = task.Exception?.GetBaseException().Message ?? "Logout failed."
                    }
                    : ToLegacyCommandResult(task.Result);

                EditorApplication.delayCall += () =>
                {
                    var logoutProviderName = GetProviderDisplayName();
                    if (result.Success)
                    {
                        AddMessage(ChatRole.System, $"{logoutProviderName} logout completed.");
                    }
                    else
                    {
                        AddMessage(ChatRole.Error, $"{logoutProviderName} logout failed: {result.Message}");
                    }

                    SetBusy(false, "Ready");
                    RefreshLoginState();
                };
            });
        }

        private void UpdateEnvironmentUI()
        {
            UpdateStatusUI();
        }

        // -------------------------
        // Chat request flow
        // -------------------------

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (HandleMentionInputKeyDown(evt))
            {
                return;
            }

            if (_isBusy)
            {
                return;
            }

            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            if (evt.shiftKey)
            {
                return;
            }

            evt.StopPropagation();
            evt.StopImmediatePropagation();
            SendCurrentInput();
        }

    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Achieve.UniAgent;

namespace Achieve.UniAgent.Editor
{
    /// <summary>
    /// 에디터의 codex CLI 구현을 UniAgent 런타임 Client 계약으로 노출하는 어댑터입니다.
    /// </summary>
    internal sealed class UniAgentEditorCliClientAdapter : UniAgent.IClient
    {
        private readonly Func<UniAgentCliService> _serviceFactory;
        private readonly object _authLock = new object();
        private UniAgentAuthState _authState = new UniAgentAuthState
        {
            IsLoggedIn = false,
            UserId = string.Empty,
            DisplayName = string.Empty,
            Provider = "codex-cli"
        };

        public UniAgentEditorCliClientAdapter(Func<UniAgentCliService> serviceFactory)
        {
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        }

        public bool IsAvailable => true;

        public UniAgentAuthState AuthState
        {
            get
            {
                lock (_authLock)
                {
                    return new UniAgentAuthState
                    {
                        IsLoggedIn = _authState.IsLoggedIn,
                        UserId = _authState.UserId,
                        DisplayName = _authState.DisplayName,
                        Provider = _authState.Provider
                    };
                }
            }
        }

        public Task<UniAgentResult> LoginAsync(UniAgentLoginRequest request, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var service = _serviceFactory();
                var loginRequest = request ?? new UniAgentLoginRequest();
                UniAgentCommandResult command;

                // Editor는 CLI Device Auth가 로그인 표준입니다.
                if (loginRequest.UseDeviceAuth || string.IsNullOrWhiteSpace(loginRequest.BackendSessionToken))
                {
                    command = service.LoginWithDeviceAuth();
                }
                else
                {
                    command = new UniAgentCommandResult
                    {
                        Success = false,
                        Message = "BackendSessionToken login is not supported by the editor CLI adapter."
                    };
                }

                var status = service.QueryLoginStatus();
                SetAuthState(status.Success);

                return new UniAgentResult
                {
                    Success = command.Success,
                    Message = command.Message,
                    ErrorCode = command.Success ? UniAgentErrorCode.None : MapErrorCode(command.Message)
                };
            }, ct);
        }

        public Task<UniAgentResult> LogoutAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var service = _serviceFactory();
                var result = service.Logout();
                SetAuthState(false);

                return new UniAgentResult
                {
                    Success = result.Success,
                    Message = result.Message,
                    ErrorCode = result.Success ? UniAgentErrorCode.None : MapErrorCode(result.Message)
                };
            }, ct);
        }

        public Task<UniAgentClientRunResult> RunAsync(UniAgentClientRunRequest request, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return new UniAgentClientRunResult
                    {
                        Success = false,
                        Message = "Prompt is required.",
                        SessionId = string.Empty,
                        ErrorCode = UniAgentErrorCode.InvalidRequest
                    };
                }

                var service = _serviceFactory();
                var result = service.Run(
                    request.Prompt,
                    string.IsNullOrWhiteSpace(request.SessionId) ? string.Empty : request.SessionId,
                    request.ProgressCallback);

                if (!result.Success)
                {
                    var code = MapErrorCode(result.Message);
                    if (code == UniAgentErrorCode.Unauthorized)
                    {
                        SetAuthState(false);
                    }

                    return new UniAgentClientRunResult
                    {
                        Success = false,
                        Message = result.Message,
                        SessionId = result.ThreadId ?? string.Empty,
                        InputTokens = result.InputTokens,
                        OutputTokens = result.OutputTokens,
                        TotalTokens = result.TotalTokens,
                        ErrorCode = code
                    };
                }

                return new UniAgentClientRunResult
                {
                    Success = true,
                    Message = result.Message,
                    SessionId = result.ThreadId ?? string.Empty,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    TotalTokens = result.TotalTokens,
                    ErrorCode = UniAgentErrorCode.None
                };
            }, ct);
        }

        private void SetAuthState(bool isLoggedIn)
        {
            lock (_authLock)
            {
                _authState = new UniAgentAuthState
                {
                    IsLoggedIn = isLoggedIn,
                    UserId = string.Empty,
                    DisplayName = isLoggedIn ? "Codex CLI User" : string.Empty,
                    Provider = "codex-cli"
                };
            }
        }

        private static UniAgentErrorCode MapErrorCode(string message)
        {
            var text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return UniAgentErrorCode.Unknown;
            }

            if (text.IndexOf("not logged in", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UniAgentErrorCode.Unauthorized;
            }

            if (text.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UniAgentErrorCode.Timeout;
            }

            if (text.IndexOf("not installed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UniAgentErrorCode.NotSupportedPlatform;
            }

            if (text.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UniAgentErrorCode.InvalidRequest;
            }

            return UniAgentErrorCode.Unknown;
        }
    }
}

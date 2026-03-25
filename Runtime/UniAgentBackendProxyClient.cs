using System;
using System.Threading;
using System.Threading.Tasks;

namespace Achieve.UniAgent
{
    /// <summary>
    /// 모바일/런타임 백엔드 연동용 전송 계약입니다.
    /// 실제 HTTP 호출은 프로젝트별로 구현해 주입합니다.
    /// </summary>
    public interface IUniAgentBackendGateway
    {
        /// <summary>세션 토큰 로그인(검증)을 수행합니다.</summary>
        Task<UniAgentResult> LoginWithSessionTokenAsync(string backendSessionToken, CancellationToken ct = default);
        /// <summary>로그아웃을 수행합니다.</summary>
        Task<UniAgentResult> LogoutAsync(string backendSessionToken, CancellationToken ct = default);
        /// <summary>프롬프트 실행을 수행합니다.</summary>
        Task<UniAgentClientRunResult> RunAsync(string backendSessionToken, UniAgentClientRunRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// 백엔드 세션 토큰 기반 UniAgent 클라이언트 구현입니다.
    /// </summary>
    public sealed class UniAgentBackendProxyClient : UniAgent.IClient
    {
        private readonly IUniAgentBackendGateway _gateway;
        private readonly object _stateLock = new object();
        private UniAgentAuthState _authState = new UniAgentAuthState
        {
            IsLoggedIn = false,
            UserId = string.Empty,
            DisplayName = string.Empty,
            Provider = "backend-proxy"
        };

        private string _backendSessionToken = string.Empty;

        public UniAgentBackendProxyClient(IUniAgentBackendGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public bool IsAvailable => true;

        public UniAgentAuthState AuthState
        {
            get
            {
                lock (_stateLock)
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

        public async Task<UniAgentResult> LoginAsync(UniAgentLoginRequest request, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.BackendSessionToken))
            {
                return new UniAgentResult
                {
                    Success = false,
                    Message = "BackendSessionToken is required.",
                    ErrorCode = UniAgentErrorCode.InvalidRequest
                };
            }

            var token = request.BackendSessionToken.Trim();
            var result = await _gateway.LoginWithSessionTokenAsync(token, ct).ConfigureAwait(false);
            if (result != null && result.Success)
            {
                lock (_stateLock)
                {
                    _backendSessionToken = token;
                    _authState = new UniAgentAuthState
                    {
                        IsLoggedIn = true,
                        UserId = string.Empty,
                        DisplayName = "UniAgent User",
                        Provider = "backend-proxy"
                    };
                }
            }

            return result ?? new UniAgentResult
            {
                Success = false,
                Message = "Gateway returned null login result.",
                ErrorCode = UniAgentErrorCode.Unknown
            };
        }

        public async Task<UniAgentResult> LogoutAsync(CancellationToken ct = default)
        {
            var token = GetSessionToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                return new UniAgentResult
                {
                    Success = true,
                    Message = "Already logged out.",
                    ErrorCode = UniAgentErrorCode.None
                };
            }

            var result = await _gateway.LogoutAsync(token, ct).ConfigureAwait(false);
            if (result != null && result.Success)
            {
                lock (_stateLock)
                {
                    _backendSessionToken = string.Empty;
                    _authState = new UniAgentAuthState
                    {
                        IsLoggedIn = false,
                        UserId = string.Empty,
                        DisplayName = string.Empty,
                        Provider = "backend-proxy"
                    };
                }
            }

            return result ?? new UniAgentResult
            {
                Success = false,
                Message = "Gateway returned null logout result.",
                ErrorCode = UniAgentErrorCode.Unknown
            };
        }

        public async Task<UniAgentClientRunResult> RunAsync(UniAgentClientRunRequest request, CancellationToken ct = default)
        {
            var token = GetSessionToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                return new UniAgentClientRunResult
                {
                    Success = false,
                    Message = "Not logged in. Call LoginAsync with BackendSessionToken first.",
                    SessionId = string.Empty,
                    ErrorCode = UniAgentErrorCode.Unauthorized
                };
            }

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

            var result = await _gateway.RunAsync(token, request, ct).ConfigureAwait(false);
            return result ?? new UniAgentClientRunResult
            {
                Success = false,
                Message = "Gateway returned null run result.",
                SessionId = string.Empty,
                ErrorCode = UniAgentErrorCode.Unknown
            };
        }

        private string GetSessionToken()
        {
            lock (_stateLock)
            {
                return _backendSessionToken;
            }
        }
    }
}

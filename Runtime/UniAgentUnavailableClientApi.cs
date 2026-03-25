using System.Threading;
using System.Threading.Tasks;

namespace Achieve.UniAgent
{
    /// <summary>
    /// Client Provider가 설정되지 않았을 때 사용하는 기본 구현입니다.
    /// </summary>
    internal sealed class UniAgentUnavailableClientApi : UniAgent.IClient
    {
        internal static readonly UniAgentUnavailableClientApi Instance = new UniAgentUnavailableClientApi();

        private static readonly UniAgentAuthState NotLoggedInState = new UniAgentAuthState
        {
            IsLoggedIn = false,
            UserId = string.Empty,
            DisplayName = string.Empty,
            Provider = "none"
        };

        public bool IsAvailable => false;

        public UniAgentAuthState AuthState => NotLoggedInState;

        public Task<UniAgentResult> LoginAsync(UniAgentLoginRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(NotConfiguredResult());
        }

        public Task<UniAgentResult> LogoutAsync(CancellationToken ct = default)
        {
            return Task.FromResult(NotConfiguredResult());
        }

        public Task<UniAgentClientRunResult> RunAsync(UniAgentClientRunRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new UniAgentClientRunResult
            {
                Success = false,
                Message = "UniAgent client provider is not configured. Configure UniAgent.ConfigureClient(...) first.",
                SessionId = string.Empty,
                InputTokens = null,
                OutputTokens = null,
                TotalTokens = null,
                ErrorCode = UniAgentErrorCode.NotConfigured
            });
        }

        private static UniAgentResult NotConfiguredResult()
        {
            return new UniAgentResult
            {
                Success = false,
                Message = "UniAgent client provider is not configured. Configure UniAgent.ConfigureClient(...) first.",
                ErrorCode = UniAgentErrorCode.NotConfigured
            };
        }
    }
}

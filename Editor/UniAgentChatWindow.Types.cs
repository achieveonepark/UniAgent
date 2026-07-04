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
    /// 채팅 모드/역할 열거형과 세션·메시지 내부 데이터 모델을 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private enum ChatMode
        {
            Plan,
            Build
        }

        private enum ChatRole
        {
            User,
            Assistant,
            System,
            Error
        }

        [Serializable]
        private sealed class UniAgentChatHistoryState
        {
            /// <summary>현재 활성 채팅 세션 ID입니다.</summary>
            public string ActiveSessionId;
            /// <summary>저장된 채팅 세션 목록입니다.</summary>
            public List<UniAgentChatSessionInfo> Sessions = new List<UniAgentChatSessionInfo>();
            /// <summary>레거시 호환용 최상위 메시지 목록입니다.</summary>
            public List<UniAgentChatHistoryItem> Messages = new List<UniAgentChatHistoryItem>();
        }

        [Serializable]
        private sealed class UniAgentChatSessionInfo
        {
            /// <summary>내부 고유 세션 ID입니다.</summary>
            public string Id;
            /// <summary>사용자 표시용 세션 이름입니다.</summary>
            public string Name;
            /// <summary>연결된 Codex CLI 세션 ID입니다.</summary>
            public string CodexSessionId;
            /// <summary>세션 누적 추정 토큰 사용량입니다.</summary>
            public int TokenUsed;
            /// <summary>추정 계산용 최근 턴별 토큰 비용입니다.</summary>
            public List<int> RecentTurnTokenCosts = new List<int>();
            /// <summary>이 세션에 저장된 메시지 목록입니다.</summary>
            public List<UniAgentChatHistoryItem> Messages = new List<UniAgentChatHistoryItem>();
        }

        [Serializable]
        private sealed class UniAgentChatHistoryItem
        {
            /// <summary>직렬화된 <see cref="ChatRole"/> 값입니다.</summary>
            public int Role;
            /// <summary>메시지 본문 텍스트입니다.</summary>
            public string Text;
            /// <summary>표시용 시각 텍스트입니다.</summary>
            public string Time;
            /// <summary>선택적 토큰 사용량 요약 텍스트입니다.</summary>
            public string TokenSummary;
        }

        private sealed class UniAgentDeferredRunResult
        {
            /// <summary>Codex 실행 결과 페이로드입니다.</summary>
            public UniAgentRunResult Result;
            /// <summary>이 실행이 Diff Preview 모드 요청인지 여부입니다.</summary>
            public bool DiffPreviewTurn;
        }

        private sealed class UniAgentChatMessage
        {
            /// <summary>채팅 타임라인에서의 메시지 역할입니다.</summary>
            public ChatRole Role;
            /// <summary>메시지 본문입니다.</summary>
            public string Text;
            /// <summary>UI 렌더링용 시각 텍스트입니다.</summary>
            public string Time;
            /// <summary>메시지 하단에 표시할 선택적 토큰 요약입니다.</summary>
            public string TokenSummary;
            /// <summary>대기/로딩용 플레이스홀더 메시지인지 여부입니다.</summary>
            public bool IsLoading;
        }
    }
}

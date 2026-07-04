using System;

namespace Achieve.UniAgent.Editor
{
    /// <summary>
    /// 모델 카탈로그의 개별 모델 항목입니다. 원격/로컬 캐시 JSON과 1:1로 매핑됩니다.
    /// </summary>
    [Serializable]
    internal sealed class UniAgentModelCatalogEntry
    {
        public string id;
        /// <summary>해당 모델의 세션 토큰 예산(컨텍스트 윈도우 추정치)입니다. 0 이하면 기본값을 사용합니다.</summary>
        public int tokenBudget;
    }

    /// <summary>
    /// 공급자(Codex/Claude Code) 하나에 대한 모델 목록입니다.
    /// </summary>
    [Serializable]
    internal sealed class UniAgentProviderCatalog
    {
        public string defaultModel;
        public UniAgentModelCatalogEntry[] models;
    }

    /// <summary>
    /// 원격/로컬 캐시로 주고받는 모델 카탈로그 전체 문서입니다.
    /// </summary>
    [Serializable]
    internal sealed class UniAgentModelCatalogDocument
    {
        public UniAgentProviderCatalog codex;
        public UniAgentProviderCatalog claude;
    }
}

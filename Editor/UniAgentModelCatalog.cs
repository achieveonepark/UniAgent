using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Achieve.UniAgent.Editor
{
    /// <summary>
    /// Codex/Claude Code 모델 목록과 모델별 토큰 예산을 관리합니다.
    /// 패키지 내장 기본값에서 시작해, 프로젝트에서 설정한 URL이 있으면 원격 JSON으로 갱신하고
    /// 결과를 <c>Library/UniAgentModelCatalogCache.json</c>에 캐시해 다음 에디터 세션에서도 재사용합니다.
    /// 원격 갱신은 항상 선택 사항이며 실패해도 내장 기본값으로 계속 동작합니다.
    /// </summary>
    internal static class UniAgentModelCatalog
    {
        private const string CatalogUrlPrefKey = UniAgentCliConstants.PrefPrefix + "ModelCatalogUrl";
        private const string CacheFileName = "UniAgentModelCatalogCache.json";

        private static UniAgentModelCatalogDocument _document;
        private static bool _refreshInFlight;

        static UniAgentModelCatalog()
        {
            _document = LoadCachedDocument() ?? BuildEmbeddedDefaults();
        }

        /// <summary>원격 모델 카탈로그를 내려받을 URL입니다. 비어 있으면 원격 갱신을 시도하지 않습니다.</summary>
        public static string CatalogUrl
        {
            get => EditorPrefs.GetString(CatalogUrlPrefKey, string.Empty);
            set => EditorPrefs.SetString(CatalogUrlPrefKey, value ?? string.Empty);
        }

        /// <summary>현재 원격 갱신 요청이 진행 중인지 여부입니다.</summary>
        public static bool IsRefreshing => _refreshInFlight;

        /// <summary>지정한 공급자의 선택 가능한 모델 ID 목록을 반환합니다.</summary>
        public static List<string> GetModels(CliProvider provider)
        {
            var catalog = provider == CliProvider.ClaudeCode ? _document.claude : _document.codex;
            var list = new List<string>();
            if (catalog?.models != null)
            {
                foreach (var entry in catalog.models)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.id))
                    {
                        list.Add(entry.id);
                    }
                }
            }

            if (list.Count == 0)
            {
                var fallback = provider == CliProvider.ClaudeCode ? BuildEmbeddedClaudeCatalog() : BuildEmbeddedCodexCatalog();
                foreach (var entry in fallback.models)
                {
                    list.Add(entry.id);
                }
            }

            return list;
        }

        /// <summary>지정한 공급자의 기본 모델 ID를 반환합니다.</summary>
        public static string GetDefaultModel(CliProvider provider)
        {
            var catalog = provider == CliProvider.ClaudeCode ? _document.claude : _document.codex;
            if (!string.IsNullOrWhiteSpace(catalog?.defaultModel))
            {
                return catalog.defaultModel;
            }

            var models = GetModels(provider);
            if (models.Count > 0)
            {
                return models[0];
            }

            return provider == CliProvider.ClaudeCode ? "claude-sonnet-5" : "gpt-5.5-codex";
        }

        /// <summary>지정한 모델 ID의 토큰 예산을 반환합니다. 목록에 없으면 <paramref name="fallback"/>을 반환합니다.</summary>
        public static int GetTokenBudget(string modelId, int fallback)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return fallback;
            }

            foreach (var catalog in new[] { _document.codex, _document.claude })
            {
                if (catalog?.models == null)
                {
                    continue;
                }

                foreach (var entry in catalog.models)
                {
                    if (entry != null && entry.tokenBudget > 0 &&
                        string.Equals(entry.id, modelId, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.tokenBudget;
                    }
                }
            }

            return fallback;
        }

        /// <summary>
        /// <see cref="CatalogUrl"/>에서 최신 모델 카탈로그를 비동기로 내려받습니다.
        /// URL이 비어 있으면 즉시 실패를 콜백합니다. 완료 시 성공 여부와 메시지를 전달합니다.
        /// </summary>
        public static void RequestRefresh(Action<bool, string> onCompleted = null)
        {
            var url = CatalogUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                onCompleted?.Invoke(false, "모델 카탈로그 URL이 설정되어 있지 않습니다.");
                return;
            }

            if (_refreshInFlight)
            {
                onCompleted?.Invoke(false, "이미 갱신을 진행 중입니다.");
                return;
            }

            _refreshInFlight = true;
            var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                _refreshInFlight = false;
                try
                {
                    var failed = request.result != UnityWebRequest.Result.Success;
                    if (failed)
                    {
                        onCompleted?.Invoke(false, $"모델 카탈로그 요청 실패: {request.error}");
                        return;
                    }

                    var json = request.downloadHandler.text;
                    var parsed = JsonUtility.FromJson<UniAgentModelCatalogDocument>(json);
                    if (parsed == null || (parsed.codex == null && parsed.claude == null))
                    {
                        onCompleted?.Invoke(false, "모델 카탈로그 응답 형식이 올바르지 않습니다.");
                        return;
                    }

                    _document = MergeWithDefaults(parsed);
                    WriteCache(json);
                    onCompleted?.Invoke(true, "모델 카탈로그를 갱신했습니다.");
                }
                catch (Exception ex)
                {
                    onCompleted?.Invoke(false, $"모델 카탈로그 파싱 실패: {ex.Message}");
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        private static UniAgentModelCatalogDocument MergeWithDefaults(UniAgentModelCatalogDocument parsed)
        {
            var defaults = BuildEmbeddedDefaults();
            return new UniAgentModelCatalogDocument
            {
                codex = parsed.codex ?? defaults.codex,
                claude = parsed.claude ?? defaults.claude
            };
        }

        private static string GetCachePath()
        {
            return Path.Combine(UniAgentChatHelper.GetProjectRootPath(), "Library", CacheFileName);
        }

        private static UniAgentModelCatalogDocument LoadCachedDocument()
        {
            try
            {
                var path = GetCachePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path);
                var parsed = JsonUtility.FromJson<UniAgentModelCatalogDocument>(json);
                return parsed != null ? MergeWithDefaults(parsed) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteCache(string json)
        {
            try
            {
                File.WriteAllText(GetCachePath(), json);
            }
            catch
            {
                // 캐시 저장 실패는 무시합니다. 다음 갱신 시 다시 시도됩니다.
            }
        }

        private static UniAgentModelCatalogDocument BuildEmbeddedDefaults()
        {
            return new UniAgentModelCatalogDocument
            {
                codex = BuildEmbeddedCodexCatalog(),
                claude = BuildEmbeddedClaudeCatalog()
            };
        }

        private static UniAgentProviderCatalog BuildEmbeddedCodexCatalog()
        {
            return new UniAgentProviderCatalog
            {
                defaultModel = "gpt-5.5-codex",
                models = new[]
                {
                    new UniAgentModelCatalogEntry { id = "gpt-5.5-codex", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget },
                    new UniAgentModelCatalogEntry { id = "gpt-5.4-codex", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget },
                    new UniAgentModelCatalogEntry { id = "gpt-5.3-codex", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget },
                    new UniAgentModelCatalogEntry { id = "gpt-5.2-codex", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget }
                }
            };
        }

        private static UniAgentProviderCatalog BuildEmbeddedClaudeCatalog()
        {
            return new UniAgentProviderCatalog
            {
                defaultModel = "claude-sonnet-5",
                models = new[]
                {
                    new UniAgentModelCatalogEntry { id = "claude-opus-4-8", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget },
                    new UniAgentModelCatalogEntry { id = "claude-sonnet-5", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget },
                    new UniAgentModelCatalogEntry { id = "claude-haiku-4-5-20251001", tokenBudget = UniAgentCliConstants.DefaultSessionTokenBudget }
                }
            };
        }
    }
}

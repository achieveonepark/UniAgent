namespace Achieve.UniAgent
{
    /// <summary>
    /// UniAgent 기본 CSV 데이터 API 구현입니다.
    /// </summary>
    public sealed class UniAgentDefaultDataApi : UniAgent.IData
    {
        /// <summary>
        /// CSV 테이블을 로드합니다.
        /// </summary>
        public UniAgentCsvTable LoadCsv(string tableName, bool reload = false)
        {
            return UniAgentCsvDataTableProvider.Load(tableName, reload);
        }

        /// <summary>
        /// CSV 테이블 로드를 시도합니다.
        /// </summary>
        public bool TryLoadCsv(string tableName, out UniAgentCsvTable table, bool reload = false)
        {
            return UniAgentCsvDataTableProvider.TryLoad(tableName, out table, reload);
        }

        /// <summary>
        /// CSV 캐시를 비웁니다.
        /// </summary>
        public void ClearCsvCache()
        {
            UniAgentCsvDataTableProvider.ClearCache();
        }
    }
}

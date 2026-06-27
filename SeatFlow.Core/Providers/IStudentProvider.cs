namespace A_Pair.Core.Providers
{
    /// <summary>
    /// 学生数据提供器接口，定义从不同数据源加载学生列表的契约。
    /// 实现类包括 CSV、Excel (XLSX)、JSON 和内存数据源。
    /// </summary>
    public interface IStudentProvider
    {
        /// <summary>
        /// 从指定数据源加载学生列表。
        /// </summary>
        /// <param name="source">数据源路径或连接字符串，具体格式由实现类决定。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>学生列表。</returns>
        Task<List<Models.Student>> LoadAsync (string source , CancellationToken cancellationToken = default);
    }
}

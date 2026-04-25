namespace A_Pair.Core.Providers
{
    public interface IStudentProvider
    {
        /// <summary>
        /// Load students from the given source (path or connection string depending on provider).
        /// </summary>
        Task<List<Models.Student>> LoadAsync (string source , CancellationToken cancellationToken = default);
    }
}

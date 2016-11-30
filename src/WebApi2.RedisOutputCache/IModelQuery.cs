namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Interface for time queries.
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    public interface IModelQuery<in TModel, out TResult>
    {
        /// <summary>
        /// Execute the query.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        TResult Execute(TModel model);
    }
}
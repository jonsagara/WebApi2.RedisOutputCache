namespace WebApi2.RedisOutputCache.Core
{
    public interface IModelQuery<in TModel, out TResult>
    {
        TResult Execute(TModel model);
    }
}
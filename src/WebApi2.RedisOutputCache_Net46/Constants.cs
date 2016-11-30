namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Constants for the library.
    /// </summary>
    public sealed class Constants
    {
        /// <summary>
        /// Appended to the full cache key to separately store the content type of the output.
        /// </summary>
        public const string ContentTypeKey = ":response-ct";

        /// <summary>
        /// Appended to the full cache key to separately store the content's etag.
        /// </summary>
        public const string EtagKey = ":response-etag";
    }
}

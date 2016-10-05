using System;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// When present, suppresses output caching for the class or action.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class IgnoreOutputCacheAttribute : Attribute
    {
    }
}
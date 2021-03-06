﻿namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Helper class for creating common cache keys.
    /// </summary>
    public static class CacheKey
    {
        /// <summary>
        /// Creates a cache key for referencing the controller/action version id, e.g., &quot;controller-action-version&quot;
        /// </summary>
        /// <param name="controllerLowered"></param>
        /// <param name="actionLowered"></param>
        /// <returns></returns>
        public static string ControllerActionVersion(string controllerLowered, string actionLowered)
        {
            return $"{controllerLowered}-{actionLowered}-version";
        }

        /// <summary>
        /// Creates a cache key for referencing an action's argument name/value version id, e.g., &quot;controller-action-argname=argVal-version&quot;.
        /// </summary>
        /// <param name="controllerLowered"></param>
        /// <param name="actionLowered"></param>
        /// <param name="argumentNameLowered"></param>
        /// <param name="argumentValue"></param>
        /// <returns></returns>
        public static string ControllerActionArgumentVersion(string controllerLowered, string actionLowered, string argumentNameLowered, string argumentValue)
        {
            return $"{controllerLowered}-{actionLowered}-{argumentNameLowered}={argumentValue}-version";
        }

        /// <summary>
        /// Creates a string like &quot;argname=argVal_v1&quot;. This is used for arguments that are part of the action's method
        /// signature.
        /// </summary>
        /// <param name="argumentNameLowered"></param>
        /// <param name="argumentValue"></param>
        /// <param name="argumentVersion"></param>
        /// <returns></returns>
        public static string VersionedArgumentNameAndValue(string argumentNameLowered, string argumentValue, long argumentVersion)
        {
            return $"{argumentNameLowered}={argumentValue}_v{argumentVersion}";
        }
    }
}

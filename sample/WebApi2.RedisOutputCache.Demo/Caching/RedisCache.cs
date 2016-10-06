using System.Configuration;
using NLog;
using StackExchange.Redis;

namespace WebApi2.RedisOutputCache.Demo.Caching
{
    public static class RedisCache
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static readonly ConnectionMultiplexer Multiplexer;

        static RedisCache()
        {
            Multiplexer = ConnectionMultiplexer.Connect(ConfigurationManager.ConnectionStrings["RedisCache"].ConnectionString);

            Multiplexer.ConnectionFailed += (o, e) =>
            {
                Logger.Error(e.Exception, $"ConnectionFailed: ConnectionType = {e.ConnectionType} reported FailureType = {e.FailureType} on EndPoint = {e.EndPoint}.");
            };

            Multiplexer.ConnectionRestored += (o, e) =>
            {
                Logger.Info($"ConnectionRestored: ConnectionType = {e.ConnectionType} reported FailureType = {e.FailureType} on EndPoint = {e.EndPoint}.");
            };
        }
    }
}
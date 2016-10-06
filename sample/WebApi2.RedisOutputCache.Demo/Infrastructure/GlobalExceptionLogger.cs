using System.Web.Http.ExceptionHandling;
using NLog;

namespace WebApi2.RedisOutputCache.Demo.Infrastructure
{
    /// <summary>
    /// Why an exception logger instead of an exception handler? Exception loggers always get called,
    /// and we don't care about controlling error responses.
    /// See: https://www.asp.net/web-api/overview/error-handling/web-api-global-error-handling
    /// </summary>
    public class GlobalExceptionLogger : ExceptionLogger
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public override void Log(ExceptionLoggerContext context)
        {
            Logger.Error(context.Exception, "Unhandled exception caught and propagated by GlobalExceptionLogger.");
        }
    }
}
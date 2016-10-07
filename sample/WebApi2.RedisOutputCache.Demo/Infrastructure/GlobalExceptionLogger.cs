using System.Text;
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
            var msg = new StringBuilder().AppendLine("*** Unhandled exception caught and propagated by GlobalExceptionLogger ***");

            string controllerFullName = "(unvailable)";
            string action = "(unvailable)";

            if (context.ExceptionContext?.ControllerContext != null)
            {
                controllerFullName = context.ExceptionContext.ControllerContext.Controller.GetType().FullName;
            }

            if (context.ExceptionContext?.ActionContext != null)
            {
                action = context.ExceptionContext.ActionContext.ActionDescriptor.ActionName;
            }

            msg.AppendLine($"   Location: {controllerFullName}.{action}");

            if (context.ExceptionContext?.Request != null)
            {
                msg.AppendLine($"   Method: {context.ExceptionContext.Request.Method.ToString()}");
                msg.AppendLine($"   RequestUri: {context.Request.RequestUri}");
            }

            Logger.Error(context.Exception, msg.ToString());
        }
    }
}
using System;

namespace WebApi2.RedisOutputCache.Time
{
    /// <summary>
    /// Creates a <see cref="CacheTime"/> from the specified DateTime and the provided time components.
    /// </summary>
    public class ThisMonth : IModelQuery<DateTime, CacheTime>
    {
        private readonly int _day;
        private readonly int _hour;
        private readonly int _minute;
        private readonly int _second;

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="day"></param>
        /// <param name="hour"></param>
        /// <param name="minute"></param>
        /// <param name="second"></param>
        public ThisMonth(int day, int hour, int minute, int second)
        {
            _day = day;
            _hour = hour;
            _minute = minute;
            _second = second;
        }

        /// <summary>
        /// Create the CacheTime object.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public CacheTime Execute(DateTime model)
        {
            var cacheTime = new CacheTime
                {
                    AbsoluteExpiration = new DateTime(model.Year, model.Month, _day, _hour, _minute, _second),
                };

            if (cacheTime.AbsoluteExpiration <= model)
                cacheTime.AbsoluteExpiration = cacheTime.AbsoluteExpiration.AddMonths(1);

            cacheTime.ClientTimeSpan = cacheTime.AbsoluteExpiration.Subtract(model);

            return cacheTime;
        }
    }
}
using System;

namespace WebApi2.RedisOutputCache.Time
{
    /// <summary>
    /// Creates a <see cref="CacheTime"/> from the specified DateTime and the provided date/time components.
    /// </summary>
    public class SpecificTime : IModelQuery<DateTime, CacheTime>
    {
        private readonly int _year;
        private readonly int _month;
        private readonly int _day;
        private readonly int _hour;
        private readonly int _minute;
        private readonly int _second;

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="year"></param>
        /// <param name="month"></param>
        /// <param name="day"></param>
        /// <param name="hour"></param>
        /// <param name="minute"></param>
        /// <param name="second"></param>
        public SpecificTime(int year, int month, int day, int hour, int minute, int second)
        {
            _year = year;
            _month = month;
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
                    AbsoluteExpiration = new DateTime(_year, _month, _day, _hour, _minute, _second),
                };

            cacheTime.ClientTimeSpan = cacheTime.AbsoluteExpiration.Subtract(model);

            return cacheTime;
        }
    }
}
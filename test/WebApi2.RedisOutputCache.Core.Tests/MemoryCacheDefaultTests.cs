﻿using System;
using NUnit.Framework;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache.Core.Tests
{
    [TestFixture]
    public class MemoryCacheDefaultTests
    {
        [Test]
        public void returns_all_keys_in_cache()
        {
            IApiOutputCache cache = new MemoryCacheDefault();
            cache.Add("base", "abc", DateTime.Now.AddSeconds(60));
            cache.Add("key1", "abc", DateTime.Now.AddSeconds(60), "base");
            cache.Add("key2", "abc", DateTime.Now.AddSeconds(60), "base");
            cache.Add("key3", "abc", DateTime.Now.AddSeconds(60), "base");

            var result = cache.AllKeys;

            CollectionAssert.AreEquivalent(new[] { "base", "key1", "key2", "key3" }, result);
        }

        [Test]
        public void remove_startswith_cascades_to_all_dependencies()
        {
            IApiOutputCache cache = new MemoryCacheDefault();
            cache.Add("base", "abc", DateTime.Now.AddSeconds(60));
            cache.Add("key1","abc", DateTime.Now.AddSeconds(60), "base");
            cache.Add("key2", "abc", DateTime.Now.AddSeconds(60), "base");
            cache.Add("key3", "abc", DateTime.Now.AddSeconds(60), "base");
            Assert.IsNotNull(cache.Get<string>("key1"));
            Assert.IsNotNull(cache.Get<string>("key2"));
            Assert.IsNotNull(cache.Get<string>("key3"));

            cache.RemoveStartsWith("base");

            Assert.IsNull(cache.Get<string>("base"));
            Assert.IsNull(cache.Get<string>("key1"));
            Assert.IsNull(cache.Get<string>("key2"));
            Assert.IsNull(cache.Get<string>("key3"));
        }
    }
}

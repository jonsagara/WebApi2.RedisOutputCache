using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace WebApi2.RedisOutputCache.Demo.EndToEndTests.Values
{
    public class ValuesTests : IClassFixture<HostFixture>
    {
        private readonly HostFixture _hostFixture;

        public ValuesTests(HostFixture hostFixture)
        {
            _hostFixture = hostFixture;
        }


        [Fact]
        public async Task Test()
        {
            var result = await _hostFixture.Client.GetAsync("http://localhost/api/values");

            Assert.True(result.IsSuccessStatusCode);
            var resultString = await result.Content.ReadAsStringAsync();
        }
    }
}

using Xunit;

namespace A_Pair.Infrastructure.Tests
{
    public class JsonProviderTests
    {
        [Fact]
        public async Task JsonProvider_ReturnsEmptyForMissingFile ()
        {
            var p = new A_Pair.Infrastructure.Providers.JsonStudentProvider();
            var list = await p.LoadAsync("nonexistent.json");
            Assert.Empty(list);
        }
    }
}

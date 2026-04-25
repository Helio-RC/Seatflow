using Xunit;

namespace A_Pair.Application.Plugins.Tests
{
    public class PluginManagerTests
    {
        [Fact]
        public void LoadPlugins_NoPlugins_ReturnsEmpty ()
        {
            var dir = Path.Combine(Path.GetTempPath() , "apair_plugins_test");
            if (Directory.Exists(dir)) Directory.Delete(dir , true);
            var pm = new A_Pair.Application.Plugins.PluginManager(dir);
            var list = pm.LoadPlugins();
            Assert.Empty(list);
        }
    }
}

using System.Reflection;
using System.Runtime.Loader;

namespace A_Pair.Application.Plugins
{
    public class PluginLoadContext (string pluginPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load (AssemblyName assemblyName)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
            return null;
        }
    }
}

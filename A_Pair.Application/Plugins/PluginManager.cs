using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using A_Pair.Contracts.Interfaces;

namespace A_Pair.Application.Plugins
{
    public class PluginManager
    {
        private readonly string _pluginsPath;
        private readonly List<PluginLoadContext> _contexts = new();

        public PluginManager(string pluginsPath)
        {
            _pluginsPath = pluginsPath;
            Directory.CreateDirectory(_pluginsPath);
        }

        public IEnumerable<IPluginSeatingStrategy> LoadPlugins()
        {
            var list = new List<IPluginSeatingStrategy>();
            foreach (var d in Directory.EnumerateDirectories(_pluginsPath))
            {
                var dll = Directory.GetFiles(d, "*.dll").FirstOrDefault();
                if (dll == null) continue;
                var plc = new PluginLoadContext(dll);
                _contexts.Add(plc);
                try
                {
                    var asm = plc.LoadFromAssemblyPath(dll);
                    var types = asm.GetTypes().Where(t => typeof(IPluginSeatingStrategy).IsAssignableFrom(t) && !t.IsAbstract);
                    foreach (var t in types)
                    {
                        if (Activator.CreateInstance(t) is IPluginSeatingStrategy strat)
                        {
                            list.Add(strat);
                        }
                    }
                }
                catch
                {
                    // ignore plugin load errors
                }
            }

            return list;
        }

        public void UnloadAll()
        {
            foreach (var c in _contexts)
            {
                c.Unload();
            }
            _contexts.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}

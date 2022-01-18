using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using FlyleafLib.Plugins;

namespace FlyleafLib
{
    public class PluginsEngine
    {
        public Dictionary<string, PluginType>
                        Types       { get; private set; } = new Dictionary<string, PluginType>();

        public string   Folder      { get; private set; }

        internal PluginsEngine()
        {
            LoadAssemblies();
        }

        internal void LoadAssemblies()
        {
            string path = string.IsNullOrEmpty(Engine.Config.PluginsPath) ? null : Utils.GetFolderPath(Engine.Config.PluginsPath);

            // Load .dll Assemblies
            if (path != null && Directory.Exists(path))
            {
                string[] dirs = Directory.GetDirectories(path);

                foreach(string dir in dirs)
                    foreach(string file in Directory.GetFiles(dir, "*.dll"))
                        try { Assembly.LoadFrom(Path.GetFullPath(file));}
                        catch (Exception e) { Engine.Log.Error($"[PluginHandler] [Error] Failed to load assembly ({e.Message} {Utils.GetRecInnerException(e)})"); }
            }
            else
            {
                Engine.Log.Info($"[PluginHandler] No external plugins found");
            }

            // Load PluginBase Types
            Type pluginBaseType = typeof(PluginBase);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
                try
                {
                    Type[] types = assembly.GetTypes();
                    foreach (var type in types)
                        if (pluginBaseType.IsAssignableFrom(type) && type.IsClass && !type.IsAbstract)
                        {
                            // Force static constructors to execute (For early load, will be useful with c# 8.0 and static properties for interfaces eg. DefaultOptions)
                            // System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                            if (!Types.ContainsKey(type.Name))
                            {
                                Types.Add(type.Name, new PluginType() { Name = type.Name, Type = type, Version = assembly.GetName().Version});
                                Engine.Log.Info($"Plugin loaded ({type.Name} - {assembly.GetName().Version})");
                            }
                            else
                                Engine.Log.Info($"Plugin already exists ({type.Name} - {assembly.GetName().Version})");
                        }
                } catch (Exception e) { Engine.Log.Error($"Plugin failed to load plugin type ({e.Message})"); }

            Folder = path;
        }
    }
}

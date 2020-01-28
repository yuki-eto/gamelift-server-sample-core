using System;
using System.IO;
using System.Reflection;
using System.Xml;
using log4net;

namespace gamelift_server_sample_core
{
    public static class Logger
    {
        private static bool _initialized;
        private static readonly object LockObj = new object();
        private const string LogConfigName = "log4net.config";

        public static ILog GetLogger(Type type)
        {
            SetLog4NetConfiguration();
            return LogManager.GetLogger(type);
        }

        private static void SetLog4NetConfiguration()
        {
            lock (LockObj)
            {
                if (_initialized) return;
                var log4NetConfig = new XmlDocument();
                log4NetConfig.Load(File.OpenRead(LogConfigName));

                var executePath = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
                GlobalContext.Properties["LogPath"] = Path.Combine(executePath, "logs");

                var repo = LogManager.CreateRepository(Assembly.GetEntryAssembly(),
                    typeof(log4net.Repository.Hierarchy.Hierarchy));
                log4net.Config.XmlConfigurator.Configure(repo, log4NetConfig["log4net"]);
                _initialized = true;
            }
        }
    }
}
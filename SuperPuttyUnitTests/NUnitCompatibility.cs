global using Assert = NUnit.Framework.Legacy.ClassicAssert;
global using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace SuperPuttyUnitTests
{
    internal static class Program
    {
        private static readonly object LoggingLock = new object();
        private static bool initialized;

        public static void InitLoggingForUnitTests()
        {
            lock (LoggingLock)
            {
                if (!initialized)
                {
                    log4net.Config.BasicConfigurator.Configure();
                    initialized = true;
                }
            }
        }
    }
}

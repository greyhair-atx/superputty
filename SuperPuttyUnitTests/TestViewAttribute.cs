using System;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// Marks interactive test views retained for manual diagnostics. These
    /// methods are intentionally not discovered as automated NUnit tests.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestViewAttribute : Attribute
    {
    }
}

﻿namespace Microsoft.Extensions.Logging
{
    public interface ILoggerFactory
    {
        ILogger CreateLogger<T>();
        ILogger CreateLogger(string name);
    }
}

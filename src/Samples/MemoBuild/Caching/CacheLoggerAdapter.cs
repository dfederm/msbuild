// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Microsoft.Build.Experimental.ProjectCache;
using Microsoft.Build.Framework;

namespace MemoBuild.Caching
{
    internal sealed class CacheLoggerAdapter : BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger
    {
        private PluginLoggerBase _logger;

        public CacheLoggerAdapter(PluginLoggerBase logger)
        {
            _logger = logger;
        }

        public Severity CurrentSeverity => Severity.Debug;

        public int ErrorCount => 0;

        private string FormatIfNeeded(string messageFormat, params object[] messageArgs)
            => messageArgs == null || messageArgs.Length == 0
                ? messageFormat
                : string.Format(messageFormat, messageArgs);

        // More severe has higher numeric value.
        private bool IsLoggingEnabled(Severity severity) => severity >= CurrentSeverity;

        private bool IsLoggingDisabled(Severity severity) => !IsLoggingEnabled(severity);

        public void Flush()
        {
        }

        public void Always(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Always))
            {
                return;
            }

            _logger.LogMessage(FormatIfNeeded(messageFormat, messageArgs));
        }

        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Fatal))
            {
                return;
            }

            _logger.LogError(FormatIfNeeded(messageFormat, messageArgs));
        }

        public void Error(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Error))
            {
                return;
            }

            _logger.LogError(FormatIfNeeded(messageFormat, messageArgs));
        }

        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Error))
            {
                return;
            }

            _logger.LogError(FormatIfNeeded(messageFormat, messageArgs) + exception);
        }

        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            Error(exception, messageFormat, messageArgs);
            throw exception;
        }

        public void Warning(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Warning))
            {
                return;
            }

            _logger.LogWarning(FormatIfNeeded(messageFormat, messageArgs));
        }

        public void Normal(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Info))
            {
                return;
            }

            _logger.LogMessage(FormatIfNeeded(messageFormat, messageArgs));
        }

        public void Info(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Info))
            {
                return;
            }

            _logger.LogMessage(FormatIfNeeded(messageFormat, messageArgs));
        }

        public void Debug(Exception exception)
        {
            if (IsLoggingDisabled(Severity.Debug))
            {
                return;
            }

            _logger.LogMessage(exception.ToString());
        }

        public void Debug(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Debug))
            {
                return;
            }

            _logger.LogMessage(FormatIfNeeded(messageFormat, messageArgs));

        }

        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(Severity.Diagnostic))
            {
                return;
            }

            _logger.LogMessage(FormatIfNeeded(messageFormat, messageArgs), MessageImportance.Low);
        }

        public void Log(Severity severity, string message)
        {
            if (IsLoggingDisabled(severity))
            {
                return;
            }

            LogFormat(severity, message);
        }

        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            if (IsLoggingDisabled(severity))
            {
                return;
            }

            switch (severity)
            {
                case Severity.Diagnostic:
                    Diagnostic(messageFormat, messageArgs);
                    break;
                case Severity.Debug:
                    Debug(messageFormat, messageArgs);
                    break;
                case Severity.Info:
                    Info(messageFormat, messageArgs);
                    break;
                case Severity.Unknown:
                    Info(messageFormat, messageArgs);
                    break;
                case Severity.Warning:
                    Warning(messageFormat, messageArgs);
                    break;
                case Severity.Error:
                    Error(messageFormat, messageArgs);
                    break;
                case Severity.Fatal:
                    Fatal(messageFormat, messageArgs);
                    break;
            }
        }

        public void Dispose()
        {
        }
    }
}

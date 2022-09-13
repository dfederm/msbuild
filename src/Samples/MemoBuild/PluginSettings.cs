// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.Build.Experimental.ProjectCache;

namespace MemoBuild
{
    internal sealed class PluginSettings
    {
        /// <summary>
        /// Base directory to use for logging. If a relative path, it's relative to the repo root
        /// </summary>
        public string LogDirectory { get; set; } = "MemoBuildLogs";

        public HashType HashType { get; set; } = HashType.Murmur;

        public string CacheUniverse { get; set; } = "default";

        public static PluginSettings Create(
            IReadOnlyDictionary<string, string> settings,
            PluginLoggerBase logger)
        {
            PluginSettings pluginSettings = new();

            if (settings.TryGetValue(nameof(LogDirectory), out string logDirectory)
                && !string.IsNullOrEmpty(logDirectory))
            {
                pluginSettings.LogDirectory = logDirectory;
            }

            if (settings.TryGetValue(nameof(HashType), out string hashTypeStr)
                && !string.IsNullOrEmpty(hashTypeStr))
            {
                if (Enum.TryParse(hashTypeStr, out HashType hashType))
                {
                    pluginSettings.HashType = hashType;
                }
                else
                {
                    logger.LogWarning($"Setting '{nameof(HashType)}' has invalid value '{hashTypeStr}'. Ignoring and using the default.");
                }
            }

            if (settings.TryGetValue(nameof(CacheUniverse), out string cacheUniverse)
                && !string.IsNullOrEmpty(cacheUniverse))
            {
                pluginSettings.CacheUniverse = cacheUniverse;
            }

            return pluginSettings;
        }
    }
}

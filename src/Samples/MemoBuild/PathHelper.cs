// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace MemoBuild
{
    internal static class PathHelper
    {
        public static string? MakePathRelative(string path, string basePath)
        {
            ReadOnlySpan<char> pathSpan = Path.GetFullPath(path).AsSpan();
            ReadOnlySpan<char> basePathSpan = Path.GetFullPath(basePath).AsSpan();

            basePathSpan = basePathSpan.TrimEnd(Path.DirectorySeparatorChar);

            if (pathSpan.StartsWith(basePathSpan, StringComparison.OrdinalIgnoreCase))
            {
                // Relative path.
                if (basePathSpan.Length == pathSpan.Length)
                {
                    return string.Empty;
                }
                else if (pathSpan[basePathSpan.Length] == '\\')
                {
                    return new string(pathSpan.Slice(basePathSpan.Length + 1).ToArray());
                }
            }

            return null;
        }

        public static bool IsUnderDirectory(string filePath, string directoryPath)
        {
            filePath = Path.GetFullPath(filePath);
            directoryPath = Path.GetFullPath(directoryPath);

            if (!filePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (directoryPath[directoryPath.Length - 1] == Path.DirectorySeparatorChar)
            {
                return true;
            }
            else
            {
                return filePath.Length > directoryPath.Length
                    && filePath[directoryPath.Length] == Path.DirectorySeparatorChar;
            }
        }
    }
}

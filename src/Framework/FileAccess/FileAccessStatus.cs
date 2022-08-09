// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Build.Framework.FileAccess
{
    /// <summary>
    /// Flags indicating the status of a file access.
    /// </summary>
    [Flags]
    public enum FileAccessStatus : byte
    {
        /// <summary>
        /// Unknown status.
        /// </summary>
        None = 0,

        /// <summary>
        /// File access was allowed according to manifest.
        /// </summary>
        Allowed = 1,

        /// <summary>
        /// File access was denied according to manifest.
        /// </summary>
        Denied = 2,

        /// <summary>
        /// File access policy couldn't be determined as path couldn't be canonicalized.
        /// </summary>
        CannotDeterminePolicy = 3,
    }
}

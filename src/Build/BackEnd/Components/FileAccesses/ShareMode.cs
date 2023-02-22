// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Build.FileAccesses
{
    /// <summary>
    /// The requested sharing mode of the file or device.
    /// </summary>
    [Flags]

    // TODO dshepelev: Fix suppression.
#pragma warning disable CS3009 // Base type is not CLS-compliant.
    public enum ShareMode : uint
#pragma warning restore CS3009 // Base type is not CLS-compliant.
    {
        /// <summary>
        /// Prevents other processes from opening a file or device if they request delete, read, or write access.
        /// </summary>
        FILE_SHARE_NONE = 0x0,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request read access.
        /// </summary>
        /// <remarks>
        /// Otherwise, other processes cannot open the file or device if they request read access.
        /// If this flag is not specified, but the file or device has been opened for read access, the function fails.
        /// </remarks>
        FILE_SHARE_READ = 0x1,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request write access.
        /// </summary>
        /// <remarks>
        /// Otherwise, other processes cannot open the file or device if they request write access.
        /// If this flag is not specified, but the file or device has been opened for write access or has a file mapping with write
        /// access, the function fails.
        /// </remarks>
        FILE_SHARE_WRITE = 0x2,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request delete access.
        /// </summary>
        /// <remarks>
        /// Otherwise, other processes cannot open the file or device if they request delete access.
        /// If this flag is not specified, but the file or device has been opened for delete access, the function fails.
        /// Note: Delete access allows both delete and rename operations.
        /// </remarks>
        FILE_SHARE_DELETE = 0x4,
    }
}

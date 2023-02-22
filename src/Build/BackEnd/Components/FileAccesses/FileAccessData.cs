// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Build.FileAccesses
{
    /// <summary>
    /// File access data.
    /// </summary>
    /// <param name="PipId">Pip id.</param>
    /// <param name="PipDescription">Pip description.</param>
    /// <param name="Operation">Operation that performed the file access.</param>
    /// <param name="RequestedAccess">Requested access.</param>
    /// <param name="Status">File access status.</param>
    /// <param name="ExplicitlyReported"><see langword="true"/> iff file access is explicitly reported.</param>
    /// <param name="ProcessId">Process id.</param>
    /// <param name="Id">Id of file access.</param>
    /// <param name="CorrelationId">Correlation id of file access.</param>
    /// <param name="Error">Error code of the operation.</param>
    /// <param name="DesiredAccess">Desired access.</param>
    /// <param name="ShareMode">Create disposition, i.e., action to take on file that exists or does not exist.</param>
    /// <param name="CreationDisposition">Requested sharing mode.</param>
    /// <param name="FlagsAndAttributes">File flags and attributes.</param>
    /// <param name="OpenedFileOrDirectoryAttributes">Computed attributes for this file access.</param>
    /// <param name="Path">Path being accessed.</param>
    /// <param name="ProcessArgs">Process arguments.</param>
    /// <param name="IsAnAugmentedFileAccess">Whether the file access is augmented.</param>
    public readonly record struct FileAccessData(
        long PipId,
        string PipDescription,
        ReportedFileOperation Operation,
        RequestedAccess RequestedAccess,
        FileAccessStatus Status,
        bool ExplicitlyReported,

        // TODO dshepelev: Fix suppression.
#pragma warning disable CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
        uint ProcessId,
        uint Id,
        uint CorrelationId,
        uint Error,
#pragma warning restore CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
        DesiredAccess DesiredAccess,
        ShareMode ShareMode,
        CreationDisposition CreationDisposition,
        FlagsAndAttributes FlagsAndAttributes,
        FlagsAndAttributes OpenedFileOrDirectoryAttributes,
        string Path,
        string ProcessArgs,
        bool IsAnAugmentedFileAccess);
}

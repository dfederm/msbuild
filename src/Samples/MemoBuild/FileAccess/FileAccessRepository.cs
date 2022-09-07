// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Processes;
using Microsoft.Build.Execution;
using static BuildXL.Processes.IDetoursEventListener;

namespace MemoBuild.FileAccess
{
    // TODO dfederm: Make disposable to flush all logs.
    internal sealed class FileAccessRepository : IFileAccessRepository
    {
        // error codes that are considered success
        // error code 183 means cannot create a file when that file already exists. Tracker started reporting this error code in November 18 due to which some files that were considered writes earlier are now ignored.
        // If such files are later read, they are considered inputs resulting in new IMDs. Since we cannot update the 30+ repos affected by this, ignore errors in this list for the time being
        private const uint SuccessCode = 0;
        private const uint ErrorAlreadyExists = 183;

        private readonly ConcurrentDictionary<ProjectInstance, FileAccessesState> _fileAccessStates = new();

        private readonly string _logDirectory;

        public FileAccessRepository(string logDirectory)
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(_logDirectory);
        }

        public void AddFileAccess(ProjectInstance projectInstance, FileAccessData fileAccessData)
            => GetFileAccessesState(projectInstance).AddFileAccess(fileAccessData);

        public void AddProcess(ProjectInstance projectInstance, ProcessData processData)
            => GetFileAccessesState(projectInstance).AddProcess(processData);

        public FileAccesses FinishProject(ProjectInstance projectInstance)
            => GetFileAccessesState(projectInstance).FinishProject();

        private FileAccessesState GetFileAccessesState(ProjectInstance projectInstance)
            => _fileAccessStates.GetOrAdd(projectInstance, projectInstance => new FileAccessesState(GetLogFilePath(projectInstance)));

        private string GetLogFilePath(ProjectInstance projectInstance)
            => Path.Combine(_logDirectory, projectInstance.GetNodeId(), "fileAccesses.log");

        private sealed class FileAccessesState
        {
            private readonly object _stateLock = new();

            private StreamWriter _logFileStream;

            private Dictionary<string, FileAccessInfo>? _fileTable = new(StringComparer.OrdinalIgnoreCase);

            private List<RemoveDirectoryOperation>? _deletedDirectories = new();

            private long _fileAccessCounter;

            private bool _isFinished;

            public FileAccessesState(string logFilePath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                _logFileStream = File.CreateText(logFilePath);
            }

            public void AddFileAccess(FileAccessData fileAccessData)
            {
                lock (_stateLock)
                {
                    // TODO dfederm: Reenable this? We get this for requests with targets like GetTargetFramewoks, which is fine.
                    ////EnsureNotFinished();
                    if (_isFinished)
                    {
                        return;
                    }

                    uint processId = fileAccessData.ProcessId;
                    RequestedAccess requestedAccess = fileAccessData.RequestedAccess;
                    ReportedFileOperation operation = fileAccessData.Operation;
                    string path = fileAccessData.Path;

                    // Ignore these operations as they're a bit too spammy for what we need
                    if (operation == ReportedFileOperation.FindFirstFileEx
                        || operation == ReportedFileOperation.GetFileAttributes
                        || operation == ReportedFileOperation.GetFileAttributesEx)
                    {
                        return;
                    }

                    if (operation == ReportedFileOperation.Process)
                    {
                        _logFileStream.WriteLine($"New process: PId {processId}, process name {path}, arguments {fileAccessData.ProcessArgs}");
                    }

                    // Used to identify file accesses reconstructed from breakaway processes, such as csc.exe when using shared compilation.
                    bool isAnAugmentedFileAccess = fileAccessData.IsAnAugmentedFileAccess;

                    if (fileAccessData.Error == SuccessCode || fileAccessData.Error == ErrorAlreadyExists)
                    {
                        string trimmedPath = RemoveUnwantedPrefixes(path);
                        _logFileStream.WriteLine(isAnAugmentedFileAccess
                            ? $"{processId}, {requestedAccess}, {operation}, {trimmedPath}, Augmented"
                            : $"{processId}, {requestedAccess}, {operation}, {trimmedPath}");

                        if (operation == ReportedFileOperation.RemoveDirectory)
                        {
                            // if a file is created under this path in future, it will have a higher file counter
                            // this file counter can be used to determine whether the file should be considered deleted or not
                            _deletedDirectories!.Add(new RemoveDirectoryOperation(_fileAccessCounter, trimmedPath));
                        }
                        else if (requestedAccess == RequestedAccess.Enumerate
                                || requestedAccess == RequestedAccess.EnumerationProbe
                                || requestedAccess == RequestedAccess.Probe)
                        {
                            // Don't add enumerations and probes to fileAccessInfo as they are not needed for QuickBuild.
                            // We still want to log them for debugging though which is why they're not filtered earlier.
                        }
                        else
                        {
                            if (!_fileTable!.TryGetValue(trimmedPath, out FileAccessInfo access))
                            {
                                access = new FileAccessInfo(trimmedPath);
                                _fileTable.Add(trimmedPath, access);
                            }

                            access.AccessAndOperations.Add(new AccessAndOperation(
                                _fileAccessCounter,
                                fileAccessData.DesiredAccess,
                                fileAccessData.FlagsAndAttributes,
                                requestedAccess,
                                operation,
                                isAnAugmentedFileAccess));
                        }

                        _fileAccessCounter++;
                    }
                    else
                    {
                        // we don't want to process failing file accesses- logging them with the error code
                        // Common error codes are ERROR_FILE_NOT_FOUND 2,  ERROR_PATH_NOT_FOUND 3, ERROR_INVALID_NAME 123
                        // Reference: https://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
                        _logFileStream.WriteLine(isAnAugmentedFileAccess
                            ? $"{processId}, {requestedAccess}, {operation}, {path}, Augmented, {fileAccessData.Error}"
                            : $"{processId}, {requestedAccess}, {operation}, {path}, {fileAccessData.Error}");
                    }
                }
            }

            public void AddProcess(ProcessData processData)
            {
                lock (_stateLock)
                {
                    // TODO dfederm: Reenable this? We get this for requests with targets like GetTargetFramewoks, which is fine.
                    ////EnsureNotFinished();
                    if (_isFinished)
                    {
                        return;
                    }

                    _logFileStream.WriteLine(
                        "Process exited. PId: {0}, Parent: {1}, Name: {2}, ExitCode: {3}, CreationTime: {4}, ExitTime: {5}",
                        processData.ProcessId,
                        processData.ParentProcessId,
                        processData.ProcessName,
                        processData.ExitCode,
                        processData.CreationDateTime,
                        processData.ExitDateTime);
                }
            }

            public FileAccesses FinishProject()
            {
                Dictionary<string, FileAccessInfo> fileTable;
                List<RemoveDirectoryOperation> deletedDirectories;
                lock (_stateLock)
                {
                    _isFinished = true;

                    fileTable = _fileTable!;
                    deletedDirectories = _deletedDirectories!;

                    _logFileStream.Dispose();

                    // Allow memory to be reclaimed
                    _fileTable = null;
                    _deletedDirectories = null;
                }

                return ProcessFileAccesses(fileTable, deletedDirectories);
            }

            private void EnsureNotFinished()
            {
                if (_isFinished)
                {
                    throw new InvalidOperationException("File access reported after the project finished");
                }
            }

            /// <summary>
            /// File paths returned by Detours have some prefixes that need to be removed:
            /// \\?\ - removes the file name limit of 260 chars. It makes it 32735 (+ a null terminator)
            /// \??\ - this is a native Win32 FS path WinNt32
            /// </summary>
            private static string RemoveUnwantedPrefixes(string absolutePath)
            {
                const string pattern1 = @"\\?\";
                const string pattern2 = @"\??\";

                if (absolutePath.StartsWith(pattern1, StringComparison.OrdinalIgnoreCase))
                {
                    return absolutePath.Substring(pattern1.Length);
                }

                if (absolutePath.StartsWith(pattern2, StringComparison.OrdinalIgnoreCase))
                {
                    return absolutePath.Substring(pattern2.Length);
                }

                return absolutePath;
            }

            private FileAccesses ProcessFileAccesses(
                Dictionary<string, FileAccessInfo> fileTable,
                List<RemoveDirectoryOperation> deletedDirectories)
            {
                var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                IEnumerable<FileAccessInfo> outputFileInfos = fileTable
                    .Select(fileInfoKvp => fileInfoKvp.Value)
                    .Where(IsOutput);
                IEnumerable<FileAccessInfo> inputFileInfos = fileTable
                    .Select(fileInfoKvp => fileInfoKvp.Value)
                    .Where(IsInput);

                foreach (FileAccessInfo fileInfo in outputFileInfos)
                {
                    string filePath = fileInfo.FilePath;

                    if (!ParentFolderExists(fileInfo, deletedDirectories))
                    {
                        continue;
                    }

                    outputs.Add(filePath);
                }

                foreach (FileAccessInfo fileInfo in inputFileInfos)
                {
                    string filePath = fileInfo.FilePath;

                    // Don't consider reads of an output as an input
                    if (outputs.Contains(filePath))
                    {
                        continue;
                    }

                    inputs.Add(filePath);
                }

                return new FileAccesses(inputs, outputs);
            }

            private static bool IsOutput(FileAccessInfo fileInfo)
            {
                // Ignore temporary files: files that were created but deleted later
                // Ignore directories: for output collection, we only care about files
                return EverWritten(fileInfo) && FileExists(fileInfo) && !IsDirectory(fileInfo);
            }

            private static bool IsInput(FileAccessInfo fileInfo)
            {
                // Ignore temporary files: files that were created but deleted later
                // Ignore directories: for fingerprinting, we only care about files
                return !EverWritten(fileInfo) && FileExists(fileInfo) && !IsDirectory(fileInfo);
            }

            private static bool FileExists(FileAccessInfo fileInfo)
            {
                // Augmented operations come out of order, so look backwards until we find one which is not an augmented read.
                // This covers the case of a file being generated, included in compilation, then deleted, but the augmented read
                // comes after the delete. This should be safe since the read should have failed if the file was missing.
                // This does *not* cover the case of an augmented write, as that case is ambiguous since we can't know which happened
                // first: the write or the delete. However, this scenario is very unlikely.
                int lastAccessIndex = fileInfo.AccessAndOperations.Count;
                AccessAndOperation lastAccess;
                do
                {
                    lastAccessIndex--;
                    lastAccess = fileInfo.AccessAndOperations[lastAccessIndex];
                }
                while (lastAccessIndex > 0 && lastAccess.IsAugmented && lastAccess.RequestedAccess == RequestedAccess.Read);

                ReportedFileOperation operation = lastAccess.ReportedFileOperation;

                bool isDeleteOperation =
                    operation == ReportedFileOperation.DeleteFile
                    // FileDispositionInformation is used to request to delete a file or cancel a previously requested deletion. For the latter, BuildXL doesn't detour it. So ZwSetDispositionInfomartionFile can be treated as deletion.
                    || operation == ReportedFileOperation.ZwSetDispositionInformationFile && (lastAccess.DesiredAccess & DesiredAccess.DELETE) != 0
                    // FileModeInformation can be used for a number of operations, but BuildXL only detours FILE_DELETE_ON_CLOSE. So, ZwSetModeInformationFile can be treated as deletion.
                    || operation == ReportedFileOperation.ZwSetModeInformationFile && (lastAccess.DesiredAccess & DesiredAccess.DELETE) != 0
                    || (lastAccess.FlagsAndAttributes & FlagsAndAttributes.FILE_FLAG_DELETE_ON_CLOSE) != 0;
                bool fileSourceMoved =
                    operation == ReportedFileOperation.MoveFileSource
                    || operation == ReportedFileOperation.MoveFileWithProgressSource
                    || operation == ReportedFileOperation.SetFileInformationByHandleSource
                    || operation == ReportedFileOperation.ZwSetRenameInformationFileSource
                    || operation == ReportedFileOperation.ZwSetFileNameInformationFileSource;

                return !isDeleteOperation && !fileSourceMoved;
            }

            private static bool IsDirectory(FileAccessInfo fileInfo)
                => fileInfo.AccessAndOperations.Any(access =>
                    access.ReportedFileOperation == ReportedFileOperation.CreateDirectory
                    || (access.FlagsAndAttributes & FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY) != 0)
                || fileInfo.FilePath.EndsWith("\\", StringComparison.Ordinal);

            private static bool ParentFolderExists(FileAccessInfo fileInfo, List<RemoveDirectoryOperation> deletedDirectories)
            {
                foreach (RemoveDirectoryOperation deletedDirectory in deletedDirectories)
                {
                    // GlobalAccessId is an increasing sequence of all accesses for a target (across all processes) and is guaranteed to be unique and in order.
                    // If the last access of the file was before the directory was deleted, the file is effectively deleted.
                    // If the last access of the file was after the directory was deleted, the directory must have been later recreated.
                    if (PathHelper.IsUnderDirectory(deletedDirectory.DirectoryPath, fileInfo.FilePath)
                        && fileInfo.AccessAndOperations[fileInfo.AccessAndOperations.Count - 1].GlobalAccessId < deletedDirectory.GlobalAccessId)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool EverWritten(FileAccessInfo fileInfo)
                => fileInfo.AccessAndOperations.Any(access => (access.RequestedAccess & RequestedAccess.Write) != 0);

            private sealed class FileAccessInfo
            {
                public FileAccessInfo(string filePath)
                {
                    FilePath = filePath;
                }

                public string FilePath { get; }

                public List<AccessAndOperation> AccessAndOperations { get; } = new List<AccessAndOperation>();
            }

            private sealed class AccessAndOperation
            {
                public AccessAndOperation(
                    long globalAccessId,
                    DesiredAccess desiredAccess,
                    FlagsAndAttributes flagsAndAndAttributes,
                    RequestedAccess requestedAccess,
                    ReportedFileOperation reportedFileOperation,
                    bool isAugmented)
                {
                    GlobalAccessId = globalAccessId;
                    DesiredAccess = desiredAccess;
                    FlagsAndAttributes = flagsAndAndAttributes;
                    RequestedAccess = requestedAccess;
                    ReportedFileOperation = reportedFileOperation;
                    IsAugmented = isAugmented;
                }

                public long GlobalAccessId { get; }

                public DesiredAccess DesiredAccess { get; }

                public FlagsAndAttributes FlagsAndAttributes { get; }

                public RequestedAccess RequestedAccess { get; }

                public ReportedFileOperation ReportedFileOperation { get; }

                public bool IsAugmented { get; }
            }

            private sealed class RemoveDirectoryOperation
            {
                public RemoveDirectoryOperation(long globalAccessId, string path)
                {
                    GlobalAccessId = globalAccessId;
                    DirectoryPath = path;
                }

                public long GlobalAccessId { get; }

                public string DirectoryPath { get; }
            }
        }
    }
}

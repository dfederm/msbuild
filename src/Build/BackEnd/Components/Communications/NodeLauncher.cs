// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using BuildXL.Processes;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Microsoft.Build.Exceptions;
using Microsoft.Build.FileAccesses;
using Microsoft.Build.Framework;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.FileSystem;
using BackendNativeMethods = Microsoft.Build.BackEnd.NativeMethods;

#nullable disable

namespace Microsoft.Build.BackEnd
{
    internal static class NodeLauncher
    {
        // TODO dfederm: This is bad. Make NodeLauncher a component?
        private static List<ISandboxedProcess> SandboxedProcesses = new();

        /// <summary>
        /// Creates a new MSBuild process
        /// </summary>
        public static Process Start(string msbuildLocation, string commandLineArgs, IBuildComponentHost componentHost, int nodeId)
        {
            // Should always have been set already.
            ErrorUtilities.VerifyThrowInternalLength(msbuildLocation, nameof(msbuildLocation));

            if (!FileSystems.Default.FileExists(msbuildLocation))
            {
                throw new BuildAbortedException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("CouldNotFindMSBuildExe", msbuildLocation));
            }

            // Disable MSBuild server for a child process.
            // In case of starting msbuild server it prevents an infinite recurson. In case of starting msbuild node we also do not want this variable to be set.
            return DisableMSBuildServer(() => StartInternal(msbuildLocation, commandLineArgs, componentHost, nodeId));
        }

        private static Process StartInternal(string msbuildLocation, string commandLineArgs, IBuildComponentHost componentHost, int nodeId)
        {
            CommunicationsUtilities.Trace("Launching node from {0}", msbuildLocation);

            // Repeat the executable name as the first token of the command line because the command line
            // parser logic expects it and will otherwise skip the first argument
            commandLineArgs = $"\"{msbuildLocation}\" {commandLineArgs}";

            string msbuildExe = msbuildLocation;

#if RUNTIME_TYPE_NETCORE || MONO
            // Mono automagically uses the current mono, to execute a managed assembly
            if (!NativeMethodsShared.IsMono)
            {
                // Run the child process with the same host as the currently-running process.
                msbuildExe = CurrentHost.GetCurrentHost();
            }
#endif

            return componentHost?.BuildParameters?.ReportFileAccesses ?? false
                ? StartDetouredProcess(msbuildExe, commandLineArgs, componentHost, nodeId)
                : StartProcess(msbuildLocation, msbuildExe, commandLineArgs);
        }

        /// <summary>
        /// Creates a new MSBuild process
        /// </summary>
        private static Process StartProcess(string msbuildLocation, string msbuildExe, string commandLineArgs)
        {
            BackendNativeMethods.STARTUP_INFO startInfo = new();
            startInfo.cb = Marshal.SizeOf<BackendNativeMethods.STARTUP_INFO>();

            // Null out the process handles so that the parent process does not wait for the child process
            // to exit before it can exit.
            uint creationFlags = 0;
            if (Traits.Instance.EscapeHatches.EnsureStdOutForChildNodesIsPrimaryStdout)
            {
                creationFlags = BackendNativeMethods.NORMALPRIORITYCLASS;
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDNODEWINDOW")))
            {
                if (!Traits.Instance.EscapeHatches.EnsureStdOutForChildNodesIsPrimaryStdout)
                {
                    // Redirect the streams of worker nodes so that this MSBuild.exe's
                    // parent doesn't wait on idle worker nodes to close streams
                    // after the build is complete.
                    startInfo.hStdError = BackendNativeMethods.InvalidHandle;
                    startInfo.hStdInput = BackendNativeMethods.InvalidHandle;
                    startInfo.hStdOutput = BackendNativeMethods.InvalidHandle;
                    startInfo.dwFlags = BackendNativeMethods.STARTFUSESTDHANDLES;
                    creationFlags |= BackendNativeMethods.CREATENOWINDOW;
                }
            }
            else
            {
                creationFlags |= BackendNativeMethods.CREATE_NEW_CONSOLE;
            }

            if (!NativeMethodsShared.IsWindows)
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo();
                processStartInfo.FileName = msbuildExe;
                processStartInfo.Arguments = commandLineArgs;
                if (!Traits.Instance.EscapeHatches.EnsureStdOutForChildNodesIsPrimaryStdout)
                {
                    // Redirect the streams of worker nodes so that this MSBuild.exe's
                    // parent doesn't wait on idle worker nodes to close streams
                    // after the build is complete.
                    processStartInfo.RedirectStandardInput = true;
                    processStartInfo.RedirectStandardOutput = true;
                    processStartInfo.RedirectStandardError = true;
                    processStartInfo.CreateNoWindow = (creationFlags | BackendNativeMethods.CREATENOWINDOW) == BackendNativeMethods.CREATENOWINDOW;
                }
                processStartInfo.UseShellExecute = false;

                Process process;
                try
                {
                    process = Process.Start(processStartInfo);
                }
                catch (Exception ex)
                {
                    CommunicationsUtilities.Trace(
                           "Failed to launch node from {0}. CommandLine: {1}" + Environment.NewLine + "{2}",
                           msbuildLocation,
                           commandLineArgs,
                           ex.ToString());

                    throw new NodeFailedToLaunchException(ex);
                }

                CommunicationsUtilities.Trace("Successfully launched {1} node with PID {0}", process.Id, msbuildExe);
                return process;
            }
            else
            {
#if RUNTIME_TYPE_NETCORE
                // Repeat the executable name in the args to suit CreateProcess
                commandLineArgs = $"\"{msbuildExe}\" {commandLineArgs}";
#endif

                BackendNativeMethods.PROCESS_INFORMATION processInfo = new();
                BackendNativeMethods.SECURITY_ATTRIBUTES processSecurityAttributes = new();
                BackendNativeMethods.SECURITY_ATTRIBUTES threadSecurityAttributes = new();
                processSecurityAttributes.nLength = Marshal.SizeOf<BackendNativeMethods.SECURITY_ATTRIBUTES>();
                threadSecurityAttributes.nLength = Marshal.SizeOf<BackendNativeMethods.SECURITY_ATTRIBUTES>();

                bool result = BackendNativeMethods.CreateProcess(
                        msbuildExe,
                        commandLineArgs,
                        ref processSecurityAttributes,
                        ref threadSecurityAttributes,
                        false,
                        creationFlags,
                        BackendNativeMethods.NullPtr,
                        null,
                        ref startInfo,
                        out processInfo);

                if (!result)
                {
                    // Creating an instance of this exception calls GetLastWin32Error and also converts it to a user-friendly string.
                    System.ComponentModel.Win32Exception e = new System.ComponentModel.Win32Exception();

                    CommunicationsUtilities.Trace(
                            "Failed to launch node from {0}. System32 Error code {1}. Description {2}. CommandLine: {2}",
                            msbuildLocation,
                            e.NativeErrorCode.ToString(CultureInfo.InvariantCulture),
                            e.Message,
                            commandLineArgs);

                    throw new NodeFailedToLaunchException(e.NativeErrorCode.ToString(CultureInfo.InvariantCulture), e.Message);
                }

                int childProcessId = processInfo.dwProcessId;

                if (processInfo.hProcess != IntPtr.Zero && processInfo.hProcess != NativeMethods.InvalidHandle)
                {
                    NativeMethodsShared.CloseHandle(processInfo.hProcess);
                }

                if (processInfo.hThread != IntPtr.Zero && processInfo.hThread != NativeMethods.InvalidHandle)
                {
                    NativeMethodsShared.CloseHandle(processInfo.hThread);
                }

                CommunicationsUtilities.Trace("Successfully launched {1} node with PID {0}", childProcessId, msbuildExe);
                return Process.GetProcessById(childProcessId);
            }
        }

        private static Process StartDetouredProcess(string msbuildExe, string commandLineArgs, IBuildComponentHost componentHost, int nodeId)
        {
            IFileAccessManager fileAccessManager = (IFileAccessManager)componentHost.GetComponent(BuildComponentType.FileAccessManager);

            var eventListener = new DetoursEventListener(fileAccessManager, nodeId);
            eventListener.SetMessageHandlingFlags(MessageHandlingFlags.DebugMessageNotify | MessageHandlingFlags.FileAccessNotify | MessageHandlingFlags.ProcessDataNotify | MessageHandlingFlags.ProcessDetoursStatusNotify);

            var info = new SandboxedProcessInfo(
                fileStorage: null, // Don't write stdout/stderr to files
                fileName: msbuildExe,
                disableConHostSharing: false,
                detoursEventListener: eventListener,
                createJobObjectForCurrentProcess: false)
            {
                SandboxKind = SandboxKind.Default,
                PipDescription = "MSBuild",
                PipSemiStableHash = 0,
                Arguments = commandLineArgs,
                EnvironmentVariables = EnvironmentalBuildParameters.Instance,
                MaxLengthInMemory = 0, // Don't buffer any output
            };

            // FileAccessManifest.AddScope is used to define the list of files which the running process is allowed to access and what kinds of file accesses are allowed
            // Tracker internally uses AbsolutePath.Invalid to represent the root, just like Unix '/' root.
            // this code allows all types of accesses for all files
            info.FileAccessManifest.AddScope(
                AbsolutePath.Invalid,
                FileAccessPolicy.MaskNothing,
                FileAccessPolicy.AllowAll | FileAccessPolicy.ReportAccess);

            // Support shared compilation
            info.FileAccessManifest.ChildProcessesToBreakawayFromSandbox = new string[] { NativeMethodsShared.IsWindows ? "VBCSCompiler.exe" : "VBCSCompiler" };
            info.FileAccessManifest.MonitorChildProcesses = true;
            info.FileAccessManifest.IgnoreReparsePoints = true;
            info.FileAccessManifest.UseExtraThreadToDrainNtClose = false;
            info.FileAccessManifest.UseLargeNtClosePreallocatedList = true;
            info.FileAccessManifest.LogProcessData = true;

            // needed for logging process arguments when a new process is invoked; see DetoursEventListener.cs
            info.FileAccessManifest.ReportProcessArgs = true;

            // By default, Domino sets the timestamp of all input files to January 1, 1970
            // This breaks some tools like Robocopy which will not copy a file to the destination if the file exists at the destination and has a timestamp that is more recent than the source file
            info.FileAccessManifest.NormalizeReadTimestamps = false;

            // If a process exits but its child processes survive, Tracker waits 30 seconds by default to wait for this process to exit.
            // This slows down C++ builds in which mspdbsrv.exe doesn't exit when it's parent exits. Set this time to 0.
            info.NestedProcessTerminationTimeout = TimeSpan.Zero;

            // TODO dfederm: Disposal?
            ISandboxedProcess sp = SandboxedProcessFactory.StartAsync(info, forceSandboxing: false).GetAwaiter().GetResult();
            lock (SandboxedProcesses)
            {
                SandboxedProcesses.Add(sp);
            }

            CommunicationsUtilities.Trace("Successfully launched {1} node with PID {0}", sp.ProcessId, msbuildExe);
            return Process.GetProcessById(sp.ProcessId);
        }

        private static Process DisableMSBuildServer(Func<Process> func)
        {
            string useMSBuildServerEnvVarValue = Environment.GetEnvironmentVariable(Traits.UseMSBuildServerEnvVarName);
            try
            {
                if (useMSBuildServerEnvVarValue is not null)
                {
                    Environment.SetEnvironmentVariable(Traits.UseMSBuildServerEnvVarName, "0");
                }
                return func();
            }
            finally
            {
                if (useMSBuildServerEnvVarValue is not null)
                {
                    Environment.SetEnvironmentVariable(Traits.UseMSBuildServerEnvVarName, useMSBuildServerEnvVarValue);
                }
            }
        }

        private sealed class EnvironmentalBuildParameters : BuildParameters.IBuildParameters
        {
            private readonly Dictionary<string, string> _envVars;

            private EnvironmentalBuildParameters()
            {
                var envVars = new Dictionary<string, string>();
                foreach (DictionaryEntry baseVar in Environment.GetEnvironmentVariables())
                {
                    envVars.Add((string)baseVar.Key, (string)baseVar.Value);
                }

                _envVars = envVars;
            }

            private EnvironmentalBuildParameters(Dictionary<string, string> envVars)
            {
                _envVars = envVars;
            }

            public static EnvironmentalBuildParameters Instance { get; } = new EnvironmentalBuildParameters();

            public string this[string key] => _envVars[key];

            public BuildParameters.IBuildParameters Select(IEnumerable<string> keys)
                => new EnvironmentalBuildParameters(keys.ToDictionary(key => key, key => _envVars[key]));

            public BuildParameters.IBuildParameters Override(IEnumerable<KeyValuePair<string, string>> parameters)
            {
                var copy = new Dictionary<string, string>(_envVars);
                foreach (KeyValuePair<string, string> param in parameters)
                {
                    copy[param.Key] = param.Value;
                }

                return new EnvironmentalBuildParameters(copy);
            }

            public IReadOnlyDictionary<string, string> ToDictionary() => _envVars;

            public bool ContainsKey(string key) => _envVars.ContainsKey(key);
        }

        private sealed class DetoursEventListener : IDetoursEventListener
        {
            private readonly IFileAccessManager _fileAccessManager;
            private readonly int _nodeId;

            public DetoursEventListener(IFileAccessManager fileAccessManager, int nodeId)
            {
                _fileAccessManager = fileAccessManager;
                _nodeId = nodeId;
            }

            public override void HandleDebugMessage(DebugData debugData)
            {
            }

            public override void HandleFileAccess(FileAccessData fileAccessData) => _fileAccessManager.ReportFileAccess(
                new Framework.FileAccess.FileAccessData(
                    (Framework.FileAccess.ReportedFileOperation)fileAccessData.Operation,
                    (Framework.FileAccess.RequestedAccess)fileAccessData.RequestedAccess,
                    fileAccessData.ProcessId,
                    fileAccessData.Error,
                    (Framework.FileAccess.DesiredAccess)fileAccessData.DesiredAccess,
                    (Framework.FileAccess.FlagsAndAttributes)fileAccessData.FlagsAndAttributes,
                    fileAccessData.Path,
                    fileAccessData.ProcessArgs,
                    fileAccessData.IsAnAugmentedFileAccess),
                _nodeId);

            public override void HandleProcessData(ProcessData processData) => _fileAccessManager.ReportProcess(
                new Framework.FileAccess.ProcessData(
                    processData.ProcessName,
                    processData.ProcessId,
                    processData.ParentProcessId,
                    processData.CreationDateTime,
                    processData.ExitDateTime,
                    processData.ExitCode),
                _nodeId);

            public override void HandleProcessDetouringStatus(ProcessDetouringStatusData data)
            {
            }
        }
    }
}

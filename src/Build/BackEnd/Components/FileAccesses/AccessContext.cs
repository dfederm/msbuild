// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.Build.Execution;

namespace Microsoft.Build.FileAccesses
{
    // TODO dfederm: Add own FileAccessData/ProcessData which augments BXL's with this information
    public readonly struct AccessContext
    {
        public AccessContext(
            ProjectInstance? projectInstance,
            string projectFullPath,
            IReadOnlyDictionary<string, string> globalProperties,
            IReadOnlyList<string> targets)
        {
            ProjectInstance = projectInstance;
            ProjectFullPath = projectFullPath;
            GlobalProperties = globalProperties;
            Targets = targets;
        }

        /// <summary>
        /// The project instance that is being built. May be null if the project is not loaded on the main node.
        /// </summary>
        public ProjectInstance? ProjectInstance { get; }

        /// <summary>
        /// The full path to the project file.
        /// </summary>
        public string ProjectFullPath { get; }

        /// <summary>
        /// The global properties used to build the project.
        /// </summary>
        public IReadOnlyDictionary<string, string> GlobalProperties { get; }

        /// <summary>
        /// The targets that are being built.
        /// </summary>
        public IReadOnlyList<string> Targets { get; }
    }
}

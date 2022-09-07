// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace MemoBuild.Fingerprinting
{
    internal sealed class PathSet
    {
        public IList<string>? FilesRead { get; set; }
    }
}

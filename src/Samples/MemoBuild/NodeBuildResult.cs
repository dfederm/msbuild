// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Hashing;

namespace MemoBuild
{
    internal sealed class NodeBuildResult
    {
        [JsonConstructor]
        public NodeBuildResult(IReadOnlyDictionary<string, ContentHash> outputs, DateTime creationTimeUtc)
        {
            Outputs = outputs;
            CreationTimeUtc = creationTimeUtc;
        }

        public IReadOnlyDictionary<string, ContentHash> Outputs { get; }

        public DateTime CreationTimeUtc { get; }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace MemoBuild.Fingerprinting
{
    internal interface IFingerprintFactory
    {
        byte[]? GetWeakFingerprint(NodeContext nodeContext);

        PathSet? GetPathSet(NodeContext nodeContext, IEnumerable<string> observedInputs);

        byte[]? GetStrongFingerprint(PathSet? pathSet);
    }
}

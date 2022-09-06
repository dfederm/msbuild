// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace MemoBuild.Parsing
{
    internal sealed class PredictedInput
    {
        public PredictedInput(string relativePath)
        {
            RelativePath = relativePath;
        }

        public string RelativePath { get; }

        public HashSet<string> PredictorNames { get; } = new HashSet<string>(1, StringComparer.OrdinalIgnoreCase);

        public void AddPredictorName(string predictorName)
        {
            // Only need to lock on add, not get.
            // Parsing populates this collection and it's only read for diagnostic purposes after.
            lock (PredictorNames)
            {
                PredictorNames.Add(predictorName);
            }
        }
    }
}

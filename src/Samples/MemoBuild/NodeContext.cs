// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MemoBuild.Parsing;
using Microsoft.Build.Graph;

namespace MemoBuild
{
    internal sealed class NodeContext
    {
        public NodeContext(
            ProjectGraphNode node,
            ParserInfo parserInfo)
        {
            Id = node.ProjectInstance.GetNodeId();
            Node = node;
            ParserInfo = parserInfo;
        }

        public string Id { get; }

        public ProjectGraphNode Node { get; }

        public ParserInfo ParserInfo { get; }

        public NodeBuildResult? BuildResult { get; private set; }

        public void AddBuildResult(NodeBuildResult buildResult)
        {
            // TODO dfederm: Manage state changes
            BuildResult = buildResult;
        }
    }
}

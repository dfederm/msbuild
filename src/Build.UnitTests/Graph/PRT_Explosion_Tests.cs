// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// TEMP-DIAG: Repro for ProjectGraph explosion (investigate-projectgraph-num-explosion session).
// Drop in src/Build.UnitTests/Graph/. Do NOT land on PR branch.
//
// Hypothesis: when a project has TWO <ProjectReferenceTargets Include="Build" ...> items, where
// one item's Targets metadata contains the .projectReferenceTargetsOrDefaultTargets marker once
// and the other contains it twice (the post-PR-#13427 vcxproj state produced by combining
// Microsoft.Common.CurrentVersion.targets + Microsoft.Devdiv.Cpp.targets), each visit triples
// the count of "Build" entries propagated. Net: 3^n growth in graph-depth.
//
// Empirical signal: wall-clock time of GetTargetLists("Build") on a chain of depth N grows
// exponentially under DuplicatePRT, but stays linear under SinglePRT.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Build.Execution;
using Microsoft.Build.UnitTests;
using Shouldly;
using Xunit;

namespace Microsoft.Build.Graph.UnitTests
{
    public class PRT_Explosion_Tests
    {
        private readonly ITestOutputHelper _output;
        private readonly TestEnvironment _env;

        public PRT_Explosion_Tests(ITestOutputHelper output)
        {
            _output = output;
            _env = TestEnvironment.Create(output);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void SinglePRT_Build_StaysFast(int depth)
        {
            long elapsedMs = RunChain(depth, SinglePRT_Devdiv);
            _output.WriteLine($"[SINGLE PRT] depth={depth}  elapsed={elapsedMs}ms");
            elapsedMs.ShouldBeLessThan(2000, $"single-PRT depth={depth} should remain fast (linear)");
        }

        [Theory]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void DuplicatePRT_Build_ExplodesAsThreePowerN(int depth)
        {
            long elapsedMs = RunChain(depth, DuplicatePRT_CommonPlusDevdiv);
            _output.WriteLine($"[DUP PRT]    depth={depth}  elapsed={elapsedMs}ms");
            // Assertion intentionally loose: we just want to see times in the test log
            // and demonstrate the curve. The actual confirmation is the log output below.
        }

        // ---- Helpers ----

        private long RunChain(int depth, string extraContent)
        {
            var edges = new Dictionary<int, int[]>();
            for (int i = 1; i < depth; i++)
            {
                edges[i] = new[] { i + 1 };
            }
            edges[depth] = Array.Empty<int>();

            var graph = Helpers.CreateProjectGraph(
                env: _env,
                dependencyEdges: edges,
                extraContentForAllNodes: extraContent);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            IReadOnlyDictionary<ProjectGraphNode, ImmutableList<string>> targetLists =
                graph.GetTargetLists(new[] { "Build" });
            sw.Stop();

            int totalAcrossNodes = targetLists.Values.Sum(l => l.Count);
            _output.WriteLine($"  total deduped targets across all nodes: {totalAcrossNodes}");

            // Also write to a file so we can see results outside the test runner's stdout capture.
            string logPath = Environment.GetEnvironmentVariable("PRT_EXPLOSION_LOG")
                ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prt_explosion.log");
            System.IO.File.AppendAllText(logPath,
                $"{DateTime.UtcNow:O} depth={depth} elapsedMs={sw.ElapsedMilliseconds} dedupedTotal={totalAcrossNodes} pattern={(extraContent == DuplicatePRT_CommonPlusDevdiv ? "DUP" : "SINGLE")}\n");

            return sw.ElapsedMilliseconds;
        }

        // Single PRT(Build) — pre-PR-#13427 vcxproj approximation. Targets has ONE marker.
        // Identity PRTs for BGS/BC/BL so chain stays "C++-ish".
        private const string SinglePRT_Devdiv = """
            <PropertyGroup>
              <IsGraphBuild>true</IsGraphBuild>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReferenceTargets Include="Build" Targets="BuildGenerateSources;.projectReferenceTargetsOrDefaultTargets;GetNativeManifest;BuildCompile;BuildLink" />
              <ProjectReferenceTargets Include="BuildGenerateSources" Targets="BuildGenerateSources" />
              <ProjectReferenceTargets Include="BuildCompile" Targets="BuildCompile" />
              <ProjectReferenceTargets Include="BuildLink" Targets="BuildLink" />
            </ItemGroup>
            <Target Name="Build" />
            <Target Name="BuildGenerateSources" />
            <Target Name="BuildCompile" />
            <Target Name="BuildLink" />
            <Target Name="GetNativeManifest" />
            """;

        // Duplicate PRT(Build) — post-PR-#13427 vcxproj state.
        // Item #1 has 1 marker  (Microsoft.Common.CurrentVersion.targets value).
        // Item #2 has 2 markers (Microsoft.Devdiv.Cpp.targets value, since Devdiv prepends to
        // $(ProjectReferenceTargetsForBuild) which Common had already populated with a marker).
        private const string DuplicatePRT_CommonPlusDevdiv = """
            <PropertyGroup>
              <IsGraphBuild>true</IsGraphBuild>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReferenceTargets Include="Build" Targets=".projectReferenceTargetsOrDefaultTargets;GetNativeManifest;_GetCopyToOutputDirectoryItemsFromThisProject" />
              <ProjectReferenceTargets Include="Build" Targets="BuildGenerateSources;.projectReferenceTargetsOrDefaultTargets;GetNativeManifest;BuildCompile;BuildLink;.projectReferenceTargetsOrDefaultTargets;GetNativeManifest;_GetCopyToOutputDirectoryItemsFromThisProject" />
              <ProjectReferenceTargets Include="BuildGenerateSources" Targets="BuildGenerateSources" />
              <ProjectReferenceTargets Include="BuildCompile" Targets="BuildCompile" />
              <ProjectReferenceTargets Include="BuildLink" Targets="BuildLink" />
            </ItemGroup>
            <Target Name="Build" />
            <Target Name="BuildGenerateSources" />
            <Target Name="BuildCompile" />
            <Target Name="BuildLink" />
            <Target Name="GetNativeManifest" />
            <Target Name="_GetCopyToOutputDirectoryItemsFromThisProject" />
            """;
    }
}

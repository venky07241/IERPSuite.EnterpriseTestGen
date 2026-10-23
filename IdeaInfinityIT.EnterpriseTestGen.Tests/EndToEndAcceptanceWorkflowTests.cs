using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using IdeaInfinityIT.EnterpriseTestGen.Core.Security;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn.Quality;
using IdeaInfinityIT.EnterpriseTestGen.Generator;
using IdeaInfinityIT.EnterpriseTestGen.Runner;
using IdeaInfinityIT.EnterpriseTestGen.Business;
using IdeaInfinityIT.EnterpriseTestGen.Business.Ai;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests
{
    public class EndToEndAcceptanceWorkflowTests
    {
        private readonly RoslynCodeAnalyzer _analyzer;
        private readonly XunitMoqGenerator _generator;
        private readonly TestQualityVerifier _qualityVerifier;
        private readonly DotnetTestRunner _runner;
        private readonly BusinessDocParser _docParser;
        private readonly AiTestEnhancerService _aiEnhancer;

        public EndToEndAcceptanceWorkflowTests()
        {
            _analyzer = new RoslynCodeAnalyzer();
            _generator = new XunitMoqGenerator();
            _qualityVerifier = new TestQualityVerifier();
            _runner = new DotnetTestRunner();
            _docParser = new BusinessDocParser();
            _aiEnhancer = new AiTestEnhancerService();
        }

        [Fact]
        public async Task CompleteEndToEndWorkflow_OnSampleFixture_ExecutesAll12StepsSuccessfully()
        {
            // Step 1: Import Project Safely (Canonical path validation)
            string fixtureDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp");
            string canonicalDir = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(fixtureDir, mustExist: true);
            canonicalDir.Should().NotBeNullOrWhiteSpace();

            string sourceFile = Path.Combine(canonicalDir, "SampleOrderService.cs");
            string testFile = Path.Combine(canonicalDir, "ExistingOrderServiceTests.cs");

            // Step 2: Analyze Source Code (AST inspection of types, methods, dependencies, branches, return types)
            var classes = _analyzer.AnalyzeFile(sourceFile);
            classes.Should().HaveCount(1);
            var sutClass = classes.First();
            sutClass.ClassName.Should().Be("SampleOrderService");
            sutClass.ConstructorDependencies.Should().HaveCount(2);
            sutClass.Methods.Should().HaveCount(3); // ProcessOrderAsync, ValidateOrder, CancelOrderAsync

            var processMethod = sutClass.Methods.First(m => m.MethodName == "ProcessOrderAsync");
            processMethod.IsAsync.Should().BeTrue();
            processMethod.InvokedDependencies.Should().NotBeEmpty();

            // Step 3: Identify Existing Tests
            var existingTestProject = _analyzer.AnalyzeExistingTestProject(testFile);
            existingTestProject.TotalTestMethods.Should().Be(2);
            existingTestProject.ExistingTests.Should().Contain(t => t.TargetMethodName == "ValidateOrder");

            // Step 4: Identify Missing Test Scenarios (Methods lacking tests)
            var testedMethods = existingTestProject.ExistingTests.Select(t => t.TargetMethodName).Distinct().ToHashSet();
            var untestedMethods = sutClass.Methods
                .Where(m => !testedMethods.Contains(m.MethodName) && !m.MethodName.StartsWith("get_"))
                .Select(m => m.MethodName)
                .ToList();

            untestedMethods.Should().Contain("ProcessOrderAsync");
            untestedMethods.Should().Contain("CancelOrderAsync");

            // Step 5: Produce a Structured Test Plan
            var plannedSpecs = _generator.PlanTestSpecifications(sutClass, selectedMethods: untestedMethods);
            plannedSpecs.Should().NotBeEmpty();
            plannedSpecs.Should().Contain(s => s.TargetMethod == "ProcessOrderAsync");
            plannedSpecs.Should().Contain(s => s.Category == "HappyPath");

            // Step 6: Generate Tests with Valid C# Syntax and Return Types
            var (testCode, summaries) = _generator.GenerateTestClassWithMetadata(sutClass, selectedMethods: untestedMethods);
            testCode.Should().NotBeNullOrWhiteSpace();
            summaries.Should().NotBeEmpty();

            // Step 7: Compile and Repair Validation
            var diagnostics = _analyzer.ValidateGeneratedTest(testCode, $"{sutClass.ClassName}GeneratedTests.cs");
            diagnostics.Should().BeEmpty(); // Must compile cleanly with 0 syntax errors

            var (repairedCode, attempts, remainingDiags) = _analyzer.RepairGeneratedTest(testCode, $"{sutClass.ClassName}GeneratedTests.cs");
            remainingDiags.Should().BeEmpty();

            // Step 8: Verify Test Quality (Detect tautological assertions, ensure meaningful checks)
            var qualityReport = _qualityVerifier.VerifyTestQuality(testCode, $"{sutClass.ClassName}GeneratedTests.cs");
            qualityReport.QualityScore.Should().BeGreaterThanOrEqualTo(80.0);
            qualityReport.WeakAssertionCount.Should().Be(0, because: string.Join("; ", qualityReport.Findings.Select(f => $"{f.RuleId}: {f.Message} ({f.CodeSnippet})")));

            // Step 9: Parse Real Code Coverage (Zero fabrication)
            string sampleCoberturaXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<coverage line-rate=""0.88"" branch-rate=""0.75"" lines-covered=""44"" lines-valid=""50"" branches-covered=""6"" branches-valid=""8"">
  <packages />
</coverage>";
            var tempXml = Path.Combine(Path.GetTempPath(), $"cobertura_test_{Guid.NewGuid():N}.xml");
            File.WriteAllText(tempXml, sampleCoberturaXml);
            try
            {
                var runSummary = new TestRunSummary();
                _runner.ParseCoberturaXml(tempXml, runSummary);
                runSummary.CoverageCollected.Should().BeTrue();
                runSummary.LineCoveragePercentage.Should().Be(88.0);
                runSummary.BranchCoveragePercentage.Should().Be(75.0);
                runSummary.LinesCovered.Should().Be(44);
                runSummary.LinesTotal.Should().Be(50);
            }
            finally
            {
                if (File.Exists(tempXml)) File.Delete(tempXml);
            }

            // Step 10: Map Traceability States
            var brdRules = new List<BusinessRuleModel>
            {
                new BusinessRuleModel
                {
                    RuleId = "BRD-ORDER-001",
                    Condition = "When orderId <= 0",
                    ExpectedOutcome = "Must throw ArgumentException",
                    SourceDocument = "OrderProcessingBRD.md"
                }
            };
            var traceability = await _aiEnhancer.AnalyzeTraceabilityAsync(sutClass, brdRules);
            traceability.Should().NotBeEmpty();
            traceability.First().RuleId.Should().Be("BRD-ORDER-001");
            traceability.First().TraceabilityState.Should().Be("TestSuggested");
            traceability.First().CoverageStatus.Should().Be("Requires Review");
            traceability.First().IsVerified.Should().BeFalse();

            // Step 11: Missing Scenarios Reporting
            var missing = untestedMethods.Where(m => !summaries.Any(s => s.TargetMethod == m)).ToList();
            missing.Should().BeEmpty(); // All previously untested methods now have generated tests

            // Step 12: Final Result Presentation & File Verification
            summaries.Count.Should().BeGreaterThanOrEqualTo(2);
        }

        [Fact]
        public void SecurityValidation_RejectsDirectoryTraversalAndSystemPaths()
        {
            // Test 1: Directory Traversal Rejected
            Action actTraversal = () => WorkspaceSecurityValidator.ValidateRelativePath(AppContext.BaseDirectory, @"..\..\..\..\..\Windows\System32");
            actTraversal.Should().Throw<SecurityException>();

            // Test 2: System Root Alone Rejected
            var root = Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\";
            Action actRoot = () => WorkspaceSecurityValidator.ValidateAndCanonicalizePath(root, mustExist: false);
            actRoot.Should().Throw<SecurityException>();

            // Test 3: Invalid Non-.cs file save rejected
            Action actNonCs = () => WorkspaceSecurityValidator.ValidateTestSavePath(@"C:\Projects\malicious.exe");
            actNonCs.Should().Throw<SecurityException>();
        }

        [Fact]
        public void BusinessDocParser_RejectsUnsupportedBinaryFormatsWithoutCorrupting()
        {
            // PDF binary header stream: %PDF-1.4
            byte[] pdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 Fake Binary Content");
            using var stream = new MemoryStream(pdfBytes);

            var rules = _docParser.ParseStream(stream, "Specs.pdf");
            rules.Should().HaveCount(1);
            rules.First().RuleId.Should().Be("DOC-UNSUPPORTED-001");
            rules.First().Condition.Should().Contain("Unsupported binary file format");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using IdeaInfinityIT.EnterpriseTestGen.Runner.History;
using Xunit;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests
{
    public class RunHistoryAndComparisonTests
    {
        private readonly string _testHistoryDir;
        private readonly TestRunHistoryService _historyService;

        public RunHistoryAndComparisonTests()
        {
            _testHistoryDir = Path.Combine(Path.GetTempPath(), "HistoryTest_" + Guid.NewGuid().ToString("N"));
            _historyService = new TestRunHistoryService(_testHistoryDir);
        }

        [Fact]
        public void SaveRun_PersistsAndRetrievesHistoricalRuns()
        {
            // Arrange
            var summary = new TestRunSummary
            {
                RunId = "RUN-12345",
                TotalTests = 25,
                Passed = 23,
                Failed = 2,
                LineCoveragePercentage = 84.5,
                BranchCoveragePercentage = 78.0,
                DurationSeconds = 4.25
            };

            // Act
            _historyService.SaveRun(summary, "BillingService");
            var runs = _historyService.GetRecentRuns(10);

            // Assert
            runs.Should().NotBeEmpty();
            var item = runs.FirstOrDefault(r => r.RunId == "RUN-12345");
            item.Should().NotBeNull();
            item!.TotalTests.Should().Be(25);
            item.Passed.Should().Be(23);
            item.LineCoveragePercentage.Should().Be(84.5);
        }

        [Fact]
        public void CompareWithBaseline_CalculatesAccurateDeltas()
        {
            // Arrange
            var baseline = new TestRunHistoryItem
            {
                RunId = "BASE-001",
                TotalTests = 20,
                Passed = 20,
                Failed = 0,
                LineCoveragePercentage = 80.0,
                BranchCoveragePercentage = 70.0
            };

            var current = new TestRunSummary
            {
                RunId = "CURR-002",
                TotalTests = 25,
                Passed = 24,
                Failed = 1,
                LineCoveragePercentage = 85.5,
                BranchCoveragePercentage = 73.0,
                TestFailures = new List<string> { "Payment_WhenZeroAmount_Throws" }
            };

            // Act
            var comparison = _historyService.CompareWithBaseline(current, baseline);

            // Assert
            comparison.TestsDelta.Should().Be(5);
            comparison.LineCoverageDelta.Should().Be(5.5);
            comparison.BranchCoverageDelta.Should().Be(3.0);
            comparison.NewFailures.Should().ContainSingle();
        }

        [Fact]
        public void ExportTestMatrixToCsv_GeneratesValidCsvRows()
        {
            // Arrange
            var items = new List<TestCaseReviewItem>
            {
                new TestCaseReviewItem
                {
                    Id = "TC-001",
                    TargetProjectRole = "Web MVC",
                    TargetClass = "InvoiceController",
                    TargetMethod = "GetInvoices",
                    Category = "HappyPath",
                    Priority = "High",
                    Status = "Approved",
                    TestName = "GetInvoices_WhenCalled_ReturnsOk",
                    ExpectedResult = "Returns 200 OK with list",
                    Source = "CodeAnalysis"
                }
            };

            // Act
            var csv = _historyService.ExportTestMatrixToCsv(items);

            // Assert
            csv.Should().Contain("TestId,ProjectRole,TargetClass");
            csv.Should().Contain("\"TC-001\",\"Web MVC\",\"InvoiceController\",\"GetInvoices\"");
        }

        [Fact]
        public void ExportTestReportToMarkdown_GeneratesCompleteReport()
        {
            // Arrange
            var summary = new TestRunSummary
            {
                RunId = "RUN-999",
                TotalTests = 10,
                Passed = 10,
                Failed = 0,
                LineCoveragePercentage = 92.5,
                BranchCoveragePercentage = 88.0,
                CoverageCollected = true,
                DurationSeconds = 1.85
            };

            var quality = new TestQualityReport
            {
                QualityScore = 95.0,
                WeakAssertionCount = 0,
                MissingAssertionCount = 0
            };

            // Act
            var md = _historyService.ExportTestReportToMarkdown(summary, quality, "OrderModule");

            // Assert
            md.Should().Contain("# Automated Test Execution & Quality Report");
            md.Should().Contain("OrderModule");
            md.Should().Contain("92.5%");
            md.Should().Contain("Quality Score");
        }
    }
}

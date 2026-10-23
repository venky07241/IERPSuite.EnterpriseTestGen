using System.Collections.Generic;
using FluentAssertions;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn.Quality;
using Xunit;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests
{
    public class TestQualityAndTraceabilityTests
    {
        private readonly TestQualityVerifier _verifier = new();

        [Fact]
        public void VerifyTestQuality_DetectsTautologicalAssertions()
        {
            // Arrange
            var testCode = @"
using Xunit;

public class SampleTests
{
    [Fact]
    public void Test1()
    {
        Assert.True(true);
    }
}";

            // Act
            var report = _verifier.VerifyTestQuality(testCode);

            // Assert
            report.TotalTestsAnalyzed.Should().Be(1);
            report.WeakAssertionCount.Should().Be(1);
            report.Findings.Should().Contain(f => f.RuleId == "TQ-001");
            report.QualityScore.Should().BeLessThan(100.0);
        }

        [Fact]
        public void VerifyTestQuality_DetectsMissingAssertions()
        {
            // Arrange
            var testCode = @"
using Xunit;

public class MissingAssertTests
{
    [Fact]
    public void DoSomething_ExecutesWithoutAssert()
    {
        var x = 1 + 1;
        // No assert
    }
}";

            // Act
            var report = _verifier.VerifyTestQuality(testCode);

            // Assert
            report.TotalTestsAnalyzed.Should().Be(1);
            report.MissingAssertionCount.Should().Be(1);
            report.Findings.Should().Contain(f => f.RuleId == "TQ-002");
            report.QualityScore.Should().BeLessThan(80.0);
        }

        [Fact]
        public void VerifyTestQuality_DetectsDuplicateTestMethods()
        {
            // Arrange
            var testCode = @"
using Xunit;

public class DuplicateTests
{
    [Fact]
    public void MyTest()
    {
        Assert.Equal(1, 1);
    }

    [Fact]
    public void MyTest()
    {
        Assert.Equal(2, 2);
    }
}";

            // Act
            var report = _verifier.VerifyTestQuality(testCode);

            // Assert
            report.DuplicateTestCount.Should().Be(1);
            report.Findings.Should().Contain(f => f.RuleId == "TQ-003");
        }

        [Fact]
        public void BuildRequirementTraceability_MapsCoveredAndUncoveredRequirements()
        {
            // Arrange
            var requirements = new List<StructuredRequirement>
            {
                new StructuredRequirement { RequirementId = "REQ-001", Title = "Bill Calculation", TargetMethod = "CalculateBill" },
                new StructuredRequirement { RequirementId = "REQ-002", Title = "Penalty Assessment", TargetMethod = "AssessPenalty" }
            };

            var testCases = new List<TestCaseReviewItem>
            {
                new TestCaseReviewItem { Id = "TC-001", TestName = "CalculateBill_WithValidInputs_Succeeds", TargetMethod = "CalculateBill" }
            };

            // Act
            var matrix = _verifier.BuildRequirementTraceability(requirements, testCases);

            // Assert
            matrix.Should().HaveCount(2);

            var req1 = matrix.First(m => m.RuleId == "REQ-001");
            req1.CoverageStatus.Should().Be("Covered");
            req1.GeneratedTestName.Should().Be("CalculateBill_WithValidInputs_Succeeds");

            var req2 = matrix.First(m => m.RuleId == "REQ-002");
            req2.CoverageStatus.Should().Be("MissingCoverage");
            req2.GeneratedTestName.Should().Be("NOT COVERED");
        }
    }
}

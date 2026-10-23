using System;
using System.Collections.Generic;
using System.Linq;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IdeaInfinityIT.EnterpriseTestGen.Roslyn.Quality
{
    public class TestQualityVerifier
    {
        public TestQualityReport VerifyTestQuality(string testCode, string fileName = "GeneratedTests.cs")
        {
            var report = new TestQualityReport();
            if (string.IsNullOrWhiteSpace(testCode))
            {
                report.QualityScore = 0;
                return report;
            }

            var tree = CSharpSyntaxTree.ParseText(testCode);
            var root = tree.GetRoot();
            var testMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.AttributeLists.Any(al => al.Attributes.Any(a => 
                    a.Name.ToString().Contains("Fact") || a.Name.ToString().Contains("Test"))))
                .ToList();

            report.TotalTestsAnalyzed = testMethods.Count;
            if (report.TotalTestsAnalyzed == 0)
            {
                return report;
            }

            var seenMethodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in testMethods)
            {
                var methodName = method.Identifier.Text;

                // 1. Duplicate Test Detection
                if (!seenMethodNames.Add(methodName))
                {
                    report.DuplicateTestCount++;
                    report.Findings.Add(new TestQualityFinding
                    {
                        RuleId = "TQ-003",
                        Severity = "Error",
                        TestName = methodName,
                        Message = $"Duplicate test method detected: '{methodName}'.",
                        CodeSnippet = method.Identifier.ToString(),
                        Recommendation = "Ensure all test method names and scenario goals are unique."
                    });
                }

                // 2. Extract Assertions inside method
                var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
                var assertions = invocations.Where(inv =>
                {
                    var s = inv.ToString();
                    return s.Contains("Assert.") || s.Contains(".Should()") || s.Contains(".Verify(");
                }).ToList();

                // 3. Missing Assertions Check
                if (!assertions.Any())
                {
                    report.MissingAssertionCount++;
                    report.Findings.Add(new TestQualityFinding
                    {
                        RuleId = "TQ-002",
                        Severity = "Error",
                        TestName = methodName,
                        Message = $"Test '{methodName}' executes code without asserting any outcomes or interactions.",
                        CodeSnippet = method.GetLocation().GetLineSpan().StartLinePosition.Line.ToString(),
                        Recommendation = "Add specific FluentAssertions or Moq.Verify to validate expected outcome."
                    });
                    continue;
                }

                // 4. Weak / Tautological Assertions Check
                bool hasConcreteAssertion = false;
                foreach (var assert in assertions)
                {
                    var text = assert.ToString();

                    if (text.Contains("Assert.True(true)") || text.Contains("Assert.Equal(1, 1)"))
                    {
                        report.WeakAssertionCount++;
                        report.Findings.Add(new TestQualityFinding
                        {
                            RuleId = "TQ-001",
                            Severity = "Warning",
                            TestName = methodName,
                            Message = $"Tautological assertion in '{methodName}': always passes without testing system behavior.",
                            CodeSnippet = text,
                            Recommendation = "Assert actual returned object state or response properties."
                        });
                    }
                    else if (text.Contains(".Should().NotBeNull()") && !assertions.Any(a => a.ToString().Contains(".Be") || a.ToString().Contains(".Verify(") || a.ToString().Contains("Assert.")))
                    {
                        report.WeakAssertionCount++;
                        report.Findings.Add(new TestQualityFinding
                        {
                            RuleId = "TQ-001",
                            Severity = "Warning",
                            TestName = methodName,
                            Message = $"Weak assertion in '{methodName}': checking only '.NotBeNull()' might mask incorrect business values.",
                            CodeSnippet = text,
                            Recommendation = "Enhance assertion to check status codes, payload counts, or specific domain fields."
                        });
                    }
                    else
                    {
                        hasConcreteAssertion = true;
                    }
                }

                // 5. Unverified Mock Interactions
                var mockSetups = invocations.Where(i => i.ToString().Contains(".Setup(")).ToList();
                var mockVerifies = invocations.Where(i => i.ToString().Contains(".Verify(")).ToList();
                if (mockSetups.Count > 0 && mockVerifies.Count == 0 && !hasConcreteAssertion)
                {
                    report.Findings.Add(new TestQualityFinding
                    {
                        RuleId = "TQ-004",
                        Severity = "Info",
                        TestName = methodName,
                        Message = $"Test '{methodName}' configures mock dependencies but does not verify if they were called.",
                        Recommendation = "Add _mock.Verify(x => x.Method(...), Times.Once) to confirm expected interaction contract."
                    });
                }
            }

            // Compute Overall Quality Score (0-100)
            int penalties = (report.MissingAssertionCount * 30) + (report.WeakAssertionCount * 10) + (report.DuplicateTestCount * 25);
            report.QualityScore = Math.Max(0.0, Math.Min(100.0, 100.0 - penalties));

            return report;
        }

        public List<BusinessTraceabilityItem> BuildRequirementTraceability(
            List<StructuredRequirement> requirements,
            List<TestCaseReviewItem> testCases)
        {
            var matrix = new List<BusinessTraceabilityItem>();

            foreach (var req in requirements)
            {
                var matchedTest = testCases.FirstOrDefault(t =>
                    t.TargetMethod.Equals(req.TargetMethod, StringComparison.OrdinalIgnoreCase) ||
                    t.TestName.Contains(req.RequirementId) ||
                    (!string.IsNullOrEmpty(req.TargetEndpoint) && t.TestName.IndexOf(req.TargetEndpoint.Trim('/').Replace('/', '_'), StringComparison.OrdinalIgnoreCase) >= 0));

                if (matchedTest != null)
                {
                    matrix.Add(new BusinessTraceabilityItem
                    {
                        RuleId = req.RequirementId,
                        RequirementText = req.Title,
                        TargetMethod = req.TargetMethod,
                        GeneratedTestName = matchedTest.TestName,
                        CoverageStatus = "Covered",
                        AiAnalysis = $"Requirement contract verified against test '{matchedTest.TestName}'."
                    });
                }
                else
                {
                    matrix.Add(new BusinessTraceabilityItem
                    {
                        RuleId = req.RequirementId,
                        RequirementText = req.Title,
                        TargetMethod = req.TargetMethod,
                        GeneratedTestName = "NOT COVERED",
                        CoverageStatus = "MissingCoverage",
                        AiAnalysis = $"No approved unit test covers requirement '{req.RequirementId}'. Candidate test recommended."
                    });
                }
            }

            return matrix;
        }
    }
}

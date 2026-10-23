using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.Runner.History
{
    public class TestRunHistoryService
    {
        private readonly string _historyDirectory;

        public TestRunHistoryService(string? customHistoryDir = null)
        {
            _historyDirectory = !string.IsNullOrWhiteSpace(customHistoryDir)
                ? customHistoryDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnterpriseTestGen", "RunHistory");

            try
            {
                if (!Directory.Exists(_historyDirectory))
                {
                    Directory.CreateDirectory(_historyDirectory);
                }
            }
            catch
            {
                // Fallback to temp directory
                _historyDirectory = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_RunHistory");
                if (!Directory.Exists(_historyDirectory)) Directory.CreateDirectory(_historyDirectory);
            }
        }

        public void SaveRun(TestRunSummary summary, string projectName = "DefaultProject")
        {
            try
            {
                var historyItem = new TestRunHistoryItem
                {
                    RunId = summary.RunId,
                    Timestamp = summary.Timestamp,
                    ProjectName = projectName,
                    TotalTests = summary.TotalTests,
                    Passed = summary.Passed,
                    Failed = summary.Failed,
                    LineCoveragePercentage = summary.LineCoveragePercentage,
                    BranchCoveragePercentage = summary.BranchCoveragePercentage,
                    DurationSeconds = summary.DurationSeconds
                };

                var filePath = Path.Combine(_historyDirectory, $"{summary.Timestamp:yyyyMMdd_HHmmss}_{summary.RunId}.json");
                var json = JsonSerializer.Serialize(historyItem, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch
            {
                // Non-fatal if history writing fails
            }
        }

        public List<TestRunHistoryItem> GetRecentRuns(int limit = 15)
        {
            var list = new List<TestRunHistoryItem>();
            try
            {
                if (!Directory.Exists(_historyDirectory)) return list;

                var files = Directory.GetFiles(_historyDirectory, "*.json")
                                     .OrderByDescending(f => f)
                                     .Take(limit)
                                     .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var item = JsonSerializer.Deserialize<TestRunHistoryItem>(json);
                        if (item != null) list.Add(item);
                    }
                    catch { }
                }
            }
            catch { }

            return list;
        }

        public RunComparisonResult CompareWithBaseline(TestRunSummary current, TestRunHistoryItem? baseline)
        {
            var result = new RunComparisonResult
            {
                CurrentRunId = current.RunId,
                BaselineRunId = baseline?.RunId ?? "N/A"
            };

            if (baseline == null)
            {
                result.TestsDelta = current.TotalTests;
                result.PassRateDelta = current.TotalTests > 0 ? (current.Passed * 100.0 / current.TotalTests) : 0.0;
                result.LineCoverageDelta = current.LineCoveragePercentage;
                result.BranchCoverageDelta = current.BranchCoveragePercentage;
                result.NewFailures = current.TestFailures;
                return result;
            }

            result.TestsDelta = current.TotalTests - baseline.TotalTests;
            double currentPassRate = current.TotalTests > 0 ? (current.Passed * 100.0 / current.TotalTests) : 0.0;
            double baselinePassRate = baseline.TotalTests > 0 ? (baseline.Passed * 100.0 / baseline.TotalTests) : 0.0;
            result.PassRateDelta = Math.Round(currentPassRate - baselinePassRate, 2);
            result.LineCoverageDelta = Math.Round(current.LineCoveragePercentage - baseline.LineCoveragePercentage, 2);
            result.BranchCoverageDelta = Math.Round(current.BranchCoveragePercentage - baseline.BranchCoveragePercentage, 2);
            result.NewFailures = current.TestFailures;

            return result;
        }

        public string ExportTestMatrixToCsv(List<TestCaseReviewItem> testCases)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TestId,ProjectRole,TargetClass,TargetMethod,Category,Priority,Status,TestName,ExpectedResult,Source");

            foreach (var tc in testCases)
            {
                string id = EscapeCsv(tc.Id);
                string role = EscapeCsv(tc.TargetProjectRole);
                string cls = EscapeCsv(tc.TargetClass);
                string method = EscapeCsv(tc.TargetMethod);
                string category = EscapeCsv(tc.Category);
                string priority = EscapeCsv(tc.Priority);
                string status = EscapeCsv(tc.Status);
                string name = EscapeCsv(tc.TestName);
                string expected = EscapeCsv(tc.ExpectedResult);
                string source = EscapeCsv(tc.Source);

                sb.AppendLine($"{id},{role},{cls},{method},{category},{priority},{status},{name},{expected},{source}");
            }

            return sb.ToString();
        }

        public string ExportTestReportToMarkdown(TestRunSummary summary, TestQualityReport? quality = null, string projectName = "Enterprise Unit Testing")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Automated Test Execution & Quality Report");
            sb.AppendLine();
            sb.AppendLine($"- **Project:** `{projectName}`");
            sb.AppendLine($"- **Run ID:** `{summary.RunId}`");
            sb.AppendLine($"- **Timestamp:** `{summary.Timestamp:yyyy-MM-dd HH:mm:ss UTC}`");
            sb.AppendLine($"- **Duration:** `{summary.DurationSeconds}s`");
            sb.AppendLine();
            sb.AppendLine("## Execution Summary");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| Total Tests | **{summary.TotalTests}** |");
            sb.AppendLine($"| Passed | **{summary.Passed}** |");
            sb.AppendLine($"| Failed | **{summary.Failed}** |");
            sb.AppendLine($"| Skipped | **{summary.Skipped}** |");
            sb.AppendLine($"| Real Line Coverage | **{summary.LineCoveragePercentage}%** |");
            sb.AppendLine($"| Real Branch Coverage | **{summary.BranchCoveragePercentage}%** |");
            sb.AppendLine($"| Coverage Verified | **{(summary.CoverageCollected ? "Coverlet Verified" : "Uncollected")}** |");
            sb.AppendLine();

            if (quality != null)
            {
                sb.AppendLine("## Test Quality & Mutation Verification");
                sb.AppendLine();
                sb.AppendLine($"- **Quality Score:** `{quality.QualityScore}/100`");
                sb.AppendLine($"- **Weak Assertions:** `{quality.WeakAssertionCount}`");
                sb.AppendLine($"- **Missing Assertions:** `{quality.MissingAssertionCount}`");
                sb.AppendLine($"- **Duplicate Tests:** `{quality.DuplicateTestCount}`");
                sb.AppendLine();

                if (quality.Findings.Any())
                {
                    sb.AppendLine("### Quality Findings");
                    sb.AppendLine();
                    sb.AppendLine("| Rule ID | Severity | Test Name | Issue & Recommendation |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");
                    foreach (var f in quality.Findings)
                    {
                        sb.AppendLine($"| `{f.RuleId}` | **{f.Severity}** | `{f.TestName}` | {f.Message} *Tip: {f.Recommendation}* |");
                    }
                    sb.AppendLine();
                }
            }

            if (summary.TestFailures.Any())
            {
                sb.AppendLine("## Failed Tests");
                sb.AppendLine();
                foreach (var fail in summary.TestFailures)
                {
                    sb.AppendLine($"- ❌ `{fail}`");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "\"\"";
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }
    }
}

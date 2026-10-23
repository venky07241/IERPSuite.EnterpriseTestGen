using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.Runner
{
    public class DotnetTestRunner
    {
        public TestRunSummary RunTests(string testProjectPath, string? filterClass = null, int timeoutSeconds = 120, bool collectCoverage = true)
        {
            var summary = new TestRunSummary
            {
                RunId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow
            };

            if (string.IsNullOrWhiteSpace(testProjectPath) || (!File.Exists(testProjectPath) && !Directory.Exists(testProjectPath)))
            {
                summary.Logs.Add($"Error: Target test project path not found or empty: {testProjectPath}");
                summary.Failed = 1;
                return summary;
            }

            var projectDir = File.Exists(testProjectPath) ? Path.GetDirectoryName(testProjectPath)! : testProjectPath;
            var sw = Stopwatch.StartNew();

            try
            {
                // Step 1: Pre-execution Compilation Check
                var buildResult = ValidateCompilation(testProjectPath);
                if (!buildResult.Success)
                {
                    summary.CompilationErrors = buildResult.Errors;
                    summary.Failed = 1;
                    summary.Logs.Add("=== COMPILATION PRE-CHECK FAILED ===");
                    foreach (var err in buildResult.Errors)
                    {
                        summary.Logs.Add(err);
                    }
                    summary.Logs.Add("====================================");
                    summary.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
                    return summary;
                }

                // Step 2: Execute Tests with Coverlet Coverage Collection
                var runStartTimeUtc = DateTime.UtcNow;
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = projectDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("test");
                startInfo.ArgumentList.Add(testProjectPath);
                if (!string.IsNullOrWhiteSpace(filterClass) && filterClass != "ALL")
                {
                    startInfo.ArgumentList.Add("--filter");
                    startInfo.ArgumentList.Add($"FullyQualifiedName~{filterClass}");
                }
                startInfo.ArgumentList.Add("-v");
                startInfo.ArgumentList.Add("q");
                if (collectCoverage)
                {
                    startInfo.ArgumentList.Add("--collect:XPlat Code Coverage");
                }
                startInfo.ArgumentList.Add("--logger");
                startInfo.ArgumentList.Add("console;verbosity=normal");
                startInfo.ArgumentList.Add("--logger");
                startInfo.ArgumentList.Add("trx");

                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(timeoutSeconds * 1000);
                if (!exited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    summary.Logs.Add($"[TIMEOUT] Test execution exceeded {timeoutSeconds}s threshold and was safely terminated.");
                    summary.Failed = 1;
                    summary.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
                    return summary;
                }

                process.WaitForExit();
                sw.Stop();
                summary.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);

                string output = SanitizeLogOutput(outputBuilder.ToString());
                string error = SanitizeLogOutput(errorBuilder.ToString());

                if (!string.IsNullOrWhiteSpace(output)) summary.Logs.Add(output.Trim());
                if (!string.IsNullOrWhiteSpace(error)) summary.Logs.Add($"Errors: {error.Trim()}");

                // Parse Test Counts Deterministically from Output
                var matchPassed = Regex.Match(output, @"Passed:\s*(\d+)");
                if (matchPassed.Success && int.TryParse(matchPassed.Groups[1].Value, out int p)) summary.Passed = p;

                var matchFailed = Regex.Match(output, @"Failed:\s*(\d+)");
                if (matchFailed.Success && int.TryParse(matchFailed.Groups[1].Value, out int f)) summary.Failed = f;

                var matchSkipped = Regex.Match(output, @"Skipped:\s*(\d+)");
                if (matchSkipped.Success && int.TryParse(matchSkipped.Groups[1].Value, out int sk)) summary.Skipped = sk;

                var matchTotal = Regex.Match(output, @"Total:\s*(\d+)");
                if (matchTotal.Success && int.TryParse(matchTotal.Groups[1].Value, out int t))
                    summary.TotalTests = t;
                else
                    summary.TotalTests = summary.Passed + summary.Failed + summary.Skipped;

                // Capture failed test names
                var failedLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Where(l => l.Contains("Failed ") || l.Contains("[FAIL]"))
                                        .Select(l => l.Trim())
                                        .ToList();
                summary.TestFailures = failedLines;

                // Step 3: Parse Real Coverlet Cobertura Code Coverage (NO FABRICATION, NO STALE CACHE)
                if (collectCoverage)
                {
                    ParseRealCoverage(projectDir, summary, runStartTimeUtc);
                }
            }
            catch (Exception ex)
            {
                summary.Logs.Add($"Process execution exception: {ex.Message}");
                summary.Failed = 1;
            }

            return summary;
        }

        public (bool Success, List<string> Errors) ValidateCompilation(string testProjectPath)
        {
            var errors = new List<string>();
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(testProjectPath);
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("q");

            try
            {
                using var process = new Process { StartInfo = startInfo };
                var sbOut = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                bool exited = process.WaitForExit(60000);

                if (!exited)
                {
                    try { process.Kill(true); } catch { }
                    errors.Add("Compilation check timed out after 60s.");
                    return (false, errors);
                }

                var text = sbOut.ToString();
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains(": error CS", StringComparison.OrdinalIgnoreCase) || 
                        line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(line.Trim());
                    }
                }

                return (process.ExitCode == 0 && errors.Count == 0, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Build check exception: {ex.Message}");
                return (false, errors);
            }
        }

        public void ParseRealCoverage(string searchDirectory, TestRunSummary summary, DateTime? runStartTimeUtc = null)
        {
            summary.CoverageCollected = false;
            summary.CoverageStatus = "Not Available";
            summary.LineCoveragePercentage = 0.0;
            summary.BranchCoveragePercentage = 0.0;

            try
            {
                var testResultsDir = Path.Combine(searchDirectory, "TestResults");
                if (!Directory.Exists(testResultsDir))
                {
                    var parentResults = Path.Combine(Directory.GetParent(searchDirectory)?.FullName ?? "", "TestResults");
                    if (Directory.Exists(parentResults)) testResultsDir = parentResults;
                }

                if (!Directory.Exists(testResultsDir))
                {
                    summary.Logs.Add("[Coverage] No TestResults directory found. Ensure coverlet.collector is referenced.");
                    return;
                }

                // Locate coverage.cobertura.xml, ensuring it was written during or after this run
                var minTimeUtc = runStartTimeUtc.HasValue ? runStartTimeUtc.Value.AddSeconds(-5) : DateTime.MinValue;
                var coverageFiles = Directory.GetFiles(testResultsDir, "*coverage*.xml", SearchOption.AllDirectories)
                                             .Where(f => File.GetLastWriteTimeUtc(f) >= minTimeUtc)
                                             .OrderByDescending(File.GetLastWriteTimeUtc)
                                             .ToList();

                if (!coverageFiles.Any())
                {
                    summary.Logs.Add("[Coverage] No fresh coverage report generated for this run. Older stale coverage files were excluded.");
                    return;
                }

                var bestCoverageFile = coverageFiles.First();
                ParseCoberturaXml(bestCoverageFile, summary);
            }
            catch (Exception ex)
            {
                summary.Logs.Add($"[Coverage] Failed to parse coverage report: {ex.Message}");
            }
        }

        public void ParseCoberturaXml(string xmlFilePath, TestRunSummary summary)
        {
            if (!File.Exists(xmlFilePath))
                return;

            try
            {
                var doc = XDocument.Load(xmlFilePath);
                var root = doc.Root;
                if (root == null) return;

                // Cobertura format: <coverage line-rate="0.825" branch-rate="0.75" lines-covered="150" lines-valid="200" ...>
                var lineRateAttr = root.Attribute("line-rate")?.Value;
                var branchRateAttr = root.Attribute("branch-rate")?.Value;

                if (double.TryParse(lineRateAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lineRate))
                {
                    summary.LineCoveragePercentage = Math.Round(lineRate * 100.0, 2);
                    summary.CoverageCollected = true;
                    summary.CoverageStatus = "Available";
                }

                if (double.TryParse(branchRateAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double branchRate))
                {
                    summary.BranchCoveragePercentage = Math.Round(branchRate * 100.0, 2);
                }

                if (int.TryParse(root.Attribute("lines-covered")?.Value, out int lc)) summary.LinesCovered = lc;
                if (int.TryParse(root.Attribute("lines-valid")?.Value, out int lt)) summary.LinesTotal = lt;
                if (int.TryParse(root.Attribute("branches-covered")?.Value, out int bc)) summary.BranchesCovered = bc;
                if (int.TryParse(root.Attribute("branches-valid")?.Value, out int bt)) summary.BranchesTotal = bt;

                summary.CoverageReportPath = xmlFilePath;
                summary.Logs.Add($"[Coverage Verified] Line Coverage: {summary.LineCoveragePercentage}% ({summary.LinesCovered}/{summary.LinesTotal} lines) | Branch Coverage: {summary.BranchCoveragePercentage}% ({summary.BranchesCovered}/{summary.BranchesTotal} branches)");
            }
            catch (Exception ex)
            {
                summary.Logs.Add($"[Coverage] Error reading XML: {ex.Message}");
            }
        }

        private static string SanitizeLogOutput(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // Redact common secret patterns like passwords, bearer tokens, api keys
            var sanitized = Regex.Replace(input, @"(password|pwd|secret|apiKey|bearer)\s*[:=]\s*[^\s,;]+", "$1=***", RegexOptions.IgnoreCase);
            return sanitized;
        }
    }
}

using System.Collections.Generic;

namespace IdeaInfinityIT.EnterpriseTestGen.Core.Models
{
    public class ParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public string? DefaultValue { get; set; }
    }

    public class DependencyModel
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
    }

    public class BranchModel
    {
        public string ConditionText { get; set; } = string.Empty;
        public string BranchType { get; set; } = "General"; // NullCheck, Boundary, Boolean, Enum
        public int LineNumber { get; set; }
    }

    public class MethodAnalysis
    {
        public string ClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = "void";
        public bool IsAsync { get; set; }
        public string HttpMethod { get; set; } = string.Empty;
        public string RouteTemplate { get; set; } = string.Empty;
        public List<string> Attributes { get; set; } = new();
        public List<ParameterModel> Parameters { get; set; } = new();
        public List<BranchModel> Branches { get; set; } = new();
        public List<DependencyModel> Dependencies { get; set; } = new();
        public List<InvokedDependencyMethod> InvokedDependencies { get; set; } = new();
    }

    public class InvokedDependencyMethod
    {
        public string DependencyFieldName { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public bool IsAsync { get; set; }
        public int ArgumentCount { get; set; }
        public List<string> ArgumentTypes { get; set; } = new();
    }

    public class MockSetupDefinition
    {
        public string MockFieldName { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public bool IsAsync { get; set; }
        public string ReturnExpression { get; set; } = string.Empty;
        public string ThrowsException { get; set; } = string.Empty;
    }

    public class TestSpecification
    {
        public string ScenarioId { get; set; } = string.Empty;
        public string TargetClass { get; set; } = string.Empty;
        public string TargetMethod { get; set; } = string.Empty;
        public string TestName { get; set; } = string.Empty;
        public string Category { get; set; } = "HappyPath"; // HappyPath, NullValidation, Boundary, Exception, BRD, UserCustom
        public string Priority { get; set; } = "High";
        public string Description { get; set; } = string.Empty;
        public List<string> Preconditions { get; set; } = new();
        public Dictionary<string, string> Inputs { get; set; } = new();
        public List<MockSetupDefinition> MockSetups { get; set; } = new();
        public string ExpectedOutcome { get; set; } = string.Empty;
        public string? ExpectedException { get; set; }
        public double ConfidenceScore { get; set; } = 1.0;
        public List<string> UnresolvedAssumptions { get; set; } = new();
        public string Status { get; set; } = "Planned"; // Planned, Generated, Executed, Verified
    }

    public class ClassAnalysis
    {
        public string ClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string BaseClass { get; set; } = string.Empty;
        public List<string> ImplementedInterfaces { get; set; } = new();
        public string RoutePrefix { get; set; } = string.Empty;
        public bool IsWebController { get; set; }
        public bool IsApiController { get; set; }
        public bool IsService { get; set; }
        public bool IsValidator { get; set; }
        public bool IsRepository { get; set; }
        public string ProjectRole => IsWebController ? "Web MVC Controller" : (IsApiController ? "Server Web API" : (IsValidator ? "Validator" : (IsRepository ? "Data Repository" : (IsService ? "Transaction Service" : "Class Library"))));
        public List<DependencyModel> ConstructorDependencies { get; set; } = new();
        public List<MethodAnalysis> Methods { get; set; } = new();
        public List<TestDiagnosticModel> SourceDiagnostics { get; set; } = new();
    }

    public class DiscoveredProjectInfo
    {
        public string ProjectFilePath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string Sdk { get; set; } = "Microsoft.NET.Sdk";
        public string TargetFramework { get; set; } = "net8.0";
        public string ProjectType { get; set; } = "ClassLibrary"; // WebMvc, WebApi, ClassLibrary, TestProject
        public string TestFrameworkDetected { get; set; } = "None"; // xUnit, NUnit, MSTest, None
        public string MockFrameworkDetected { get; set; } = "None"; // Moq, NSubstitute, FakeItEasy, None
        public List<string> PackageReferences { get; set; } = new();
        public List<string> ProjectReferences { get; set; } = new();
        public int SourceFilesCount { get; set; }
    }

    public class DiscoveredSolutionInfo
    {
        public string SolutionFilePath { get; set; } = string.Empty;
        public string SolutionName { get; set; } = string.Empty;
        public List<DiscoveredProjectInfo> Projects { get; set; } = new();
    }

    public class StructuredRequirement
    {
        public string RequirementId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SourceType { get; set; } = "BRDDocument"; // OpenApi, Markdown, UserStory, Gherkin, BRDDocument, InlineText
        public string SourceDocument { get; set; } = string.Empty;
        public string Category { get; set; } = "Functional"; // Functional, Validation, Security, Boundary, ErrorHandling
        public string TargetEndpoint { get; set; } = string.Empty;
        public string TargetMethod { get; set; } = string.Empty;
        public string TargetClass { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = string.Empty;
        public List<string> AcceptanceCriteria { get; set; } = new();
        public string Status { get; set; } = "Inferred"; // Verified, Inferred, Ambiguous
    }

    public class OpenApiEndpointInfo
    {
        public string Path { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = "GET";
        public string OperationId { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ParameterModel> Parameters { get; set; } = new();
        public List<int> ExpectedStatusCodes { get; set; } = new();
        public string RequestBodyType { get; set; } = string.Empty;
    }

    public class OpenApiSpecificationModel
    {
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<OpenApiEndpointInfo> Endpoints { get; set; } = new();
    }

    public class CustomTestCase
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object?> Given { get; set; } = new();
        public Dictionary<string, object?> Expected { get; set; } = new();
    }

    public class CustomScenarioFile
    {
        public string TargetClass { get; set; } = string.Empty;
        public string TargetMethod { get; set; } = string.Empty;
        public List<CustomTestCase> CustomTestCases { get; set; } = new();
    }

    public class BusinessRuleModel
    {
        public string RuleId { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public string ExpectedOutcome { get; set; } = string.Empty;
        public string SourceDocument { get; set; } = string.Empty;
    }

    public class TestScenario
    {
        public string TestName { get; set; } = string.Empty;
        public string Source { get; set; } = "CodeAnalysis";
        public string TargetMethod { get; set; } = string.Empty;
        public Dictionary<string, string> MockSetups { get; set; } = new();
        public Dictionary<string, string> Inputs { get; set; } = new();
        public string ExpectedAssertion { get; set; } = string.Empty;
    }

    public class TargetConfig
    {
        public string Solution { get; set; } = string.Empty;
        public List<string> TargetProjects { get; set; } = new();
        public List<string> TestProjects { get; set; } = new();
        public List<string> BusinessDocs { get; set; } = new();
        public List<string> CustomScenarios { get; set; } = new();
        public string TestFramework { get; set; } = "xunit";
        public string MockFramework { get; set; } = "moq";
    }

    public class TestRunSummary
    {
        public string RunId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int TotalTests { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public double DurationSeconds { get; set; }
        public bool CoverageCollected { get; set; }
        public string CoverageStatus { get; set; } = "Not Available";
        public double LineCoveragePercentage { get; set; }
        public double BranchCoveragePercentage { get; set; }
        public int LinesCovered { get; set; }
        public int LinesTotal { get; set; }
        public int BranchesCovered { get; set; }
        public int BranchesTotal { get; set; }
        public string CoverageReportPath { get; set; } = string.Empty;
        public List<string> CompilationErrors { get; set; } = new();
        public List<string> TestFailures { get; set; } = new();
        public List<string> GeneratedFiles { get; set; } = new();
        public List<string> Logs { get; set; } = new();
    }

    public class TestQualityFinding
    {
        public string RuleId { get; set; } = string.Empty; // e.g. TQ-001 (WeakAssertion), TQ-002 (MissingAssertion)
        public string Severity { get; set; } = "Warning"; // Error, Warning, Info
        public string TestName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
    }

    public class TestQualityReport
    {
        public int TotalTestsAnalyzed { get; set; }
        public int WeakAssertionCount { get; set; }
        public int MissingAssertionCount { get; set; }
        public int DuplicateTestCount { get; set; }
        public double QualityScore { get; set; } = 100.0; // 0-100 scale
        public List<TestQualityFinding> Findings { get; set; } = new();
    }

    public class TestRunHistoryItem
    {
        public string RunId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int TotalTests { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public double LineCoveragePercentage { get; set; }
        public double BranchCoveragePercentage { get; set; }
        public double DurationSeconds { get; set; }
    }

    public class RunComparisonResult
    {
        public string CurrentRunId { get; set; } = string.Empty;
        public string BaselineRunId { get; set; } = string.Empty;
        public int TestsDelta { get; set; }
        public double PassRateDelta { get; set; }
        public double LineCoverageDelta { get; set; }
        public double BranchCoverageDelta { get; set; }
        public List<string> NewFailures { get; set; } = new();
        public List<string> ResolvedFailures { get; set; } = new();
        public List<string> NewTests { get; set; } = new();
    }

    public class ProjectAnalysisResult
    {
        public bool Success { get; set; }
        public string SourceType { get; set; } = string.Empty; // "ZipArchive", "Folder", "MultipleFiles", "Path"
        public string ProjectName { get; set; } = string.Empty;
        public int TotalSourceFiles { get; set; }
        public int TotalClasses { get; set; }
        public int TotalMethods { get; set; }
        public List<ClassAnalysis> Classes { get; set; } = new();
        public string ExtractedTempPath { get; set; } = string.Empty;
    }

    public class AiSettingsModel
    {
        public string Provider { get; set; } = "LocalHeuristic"; // "LocalHeuristic", "OpenAI", "AzureOpenAI", "Ollama", "Gemini"
        public string EndpointUrl { get; set; } = "http://localhost:11434";
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "llama3";
        public double Temperature { get; set; } = 0.2;
    }

    public class BusinessTraceabilityItem
    {
        public string RuleId { get; set; } = string.Empty;
        public string RequirementText { get; set; } = string.Empty;
        public string TargetMethod { get; set; } = string.Empty;
        public string GeneratedTestName { get; set; } = string.Empty;
        public string TraceabilityState { get; set; } = "RequirementParsed"; // NotAnalyzed, RequirementParsed, TestSuggested, TestGenerated, TestCompiled, TestPassed, CoverageVerified, RequiresReview
        public string CoverageStatus { get; set; } = "Requires Review"; // Requires Review, Unverified, Generated, Passed, Verified
        public bool IsVerified { get; set; }
        public string AiAnalysis { get; set; } = string.Empty;
    }

    public class AiEnhancementResult
    {
        public bool Success { get; set; }
        public string ProviderUsed { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> InferredEdgeCases { get; set; } = new();
        public List<string> SuggestedAssertions { get; set; } = new();
        public string EnhancedTestCode { get; set; } = string.Empty;
        public List<BusinessTraceabilityItem> TraceabilityItems { get; set; } = new();
    }

    public class TestDiagnosticModel
    {
        public string FileName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string ErrorId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int Column { get; set; }
        public string Severity { get; set; } = "Error"; // Error, Warning
        public string CodeSnippet { get; set; } = string.Empty;
    }

    public class GeneratedTestCaseSummary
    {
        public string TestName { get; set; } = string.Empty;
        public string TargetMethod { get; set; } = string.Empty;
        public string Category { get; set; } = "HappyPath"; // HappyPath, NullValidation, Boundary, BRD, UserCustom, ErrorFlow
        public string Description { get; set; } = string.Empty;
        public string ExpectedAssertion { get; set; } = string.Empty;
    }

    public class ExistingTestCaseAnalysis
    {
        public string TestClassName { get; set; } = string.Empty;
        public string TestMethodName { get; set; } = string.Empty;
        public string TargetClassName { get; set; } = string.Empty;
        public string TargetMethodName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public bool IsAsync { get; set; }
        public string Category { get; set; } = "ExistingTest";
        public string TestFramework { get; set; } = "xUnit";
        public bool IsInferredFromCallGraph { get; set; }
        public List<string> InferredAssertions { get; set; } = new();
        public bool HasAssertions => InferredAssertions.Count > 0;
        public List<string> MockedDependencies { get; set; } = new();
    }

    public class ExistingTestProjectAnalysis
    {
        public string ProjectName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public int TotalTestFiles { get; set; }
        public int TotalTestClasses { get; set; }
        public int TotalTestMethods { get; set; }
        public int CoveredMethodsCount { get; set; }
        public int TestsWithAssertionsCount => ExistingTests.Count(t => t.HasAssertions);
        public int TestsWithoutAssertionsCount => ExistingTests.Count(t => !t.HasAssertions);
        public List<ExistingTestCaseAnalysis> ExistingTests { get; set; } = new();
        public List<string> TestedTargetClasses => ExistingTests.Select(t => t.TargetClassName).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
    }

    public class WebToApiMapping
    {
        public string WebControllerName { get; set; } = string.Empty;
        public string WebActionMethod { get; set; } = string.Empty;
        public string ApiEndpointUrl { get; set; } = string.Empty;
        public string TargetApiController { get; set; } = string.Empty;
        public string TargetApiMethod { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = "POST";
        public string CommunicationPattern { get; set; } = "HttpClient / API Gateway";
        public string Description { get; set; } = string.Empty;
    }

    public class TestCaseReviewItem
    {
        public string Id { get; set; } = string.Empty;
        public string TestName { get; set; } = string.Empty;
        public string TargetProjectRole { get; set; } = "Core API"; // "Web MVC", "Core API", "Domain Service"
        public string ProjectName { get; set; } = string.Empty;
        public string TargetClass { get; set; } = string.Empty;
        public string TargetMethod { get; set; } = string.Empty;
        public string Category { get; set; } = "HappyPath"; // HappyPath, NullValidation, Boundary, Exception, BRD, UserCustom, ExistingCovered, NeedsUpdate
        public string TestInput { get; set; } = string.Empty;
        public string ExpectedResult { get; set; } = string.Empty;
        public string Source { get; set; } = "CodeAnalysis"; // CodeAnalysis, BRD, Custom, AiSuggestion, ExistingTest
        public string Priority { get; set; } = "Medium"; // High, Medium, Low
        public string Status { get; set; } = "Suggested"; // Suggested, Approved, Rejected, Updated, Existing
        public bool IsApproved { get; set; } = true;
        public bool IsExisting { get; set; } = false;
        public string ExistingTestFile { get; set; } = string.Empty;
        public string ExistingTestMethod { get; set; } = string.Empty;
    }

    public class MultiProjectAnalysisResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public string SolutionName { get; set; } = "IERP Multi-Project Solution";
        public ProjectAnalysisResult? WebProject { get; set; }
        public ProjectAnalysisResult? ApiProject { get; set; }
        public ExistingTestProjectAnalysis? ExistingTestProject { get; set; }
        public List<WebToApiMapping> WebToApiMappings { get; set; } = new();
        public List<TestCaseReviewItem> ReviewTestCases { get; set; } = new();
        public List<ClassAnalysis> AllDiscoveredClasses { get; set; } = new();
        public List<TestDiagnosticModel> SourceDiagnostics { get; set; } = new();
        public int TotalSourceFiles => (WebProject?.TotalSourceFiles ?? 0) + (ApiProject?.TotalSourceFiles ?? 0);
        public int TotalClasses => AllDiscoveredClasses.Count;
        public int TotalMethods => AllDiscoveredClasses.Sum(c => c.Methods.Count);
        public int TotalSuggestedCases => ReviewTestCases.Count;
        public int TotalExistingCases => ExistingTestProject?.TotalTestMethods ?? 0;
    }
}

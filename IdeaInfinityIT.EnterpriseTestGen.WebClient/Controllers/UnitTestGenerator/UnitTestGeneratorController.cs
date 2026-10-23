using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn.Quality;
using IdeaInfinityIT.EnterpriseTestGen.Business;
using IdeaInfinityIT.EnterpriseTestGen.Business.Ai;
using IdeaInfinityIT.EnterpriseTestGen.CustomCases;
using IdeaInfinityIT.EnterpriseTestGen.Generator;
using IdeaInfinityIT.EnterpriseTestGen.Runner;
using IdeaInfinityIT.EnterpriseTestGen.Runner.History;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using IdeaInfinityIT.EnterpriseTestGen.Core.Security;
using IdeaInfinityIT.EnterpriseTestGen.WebClient.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.WebClient.Controllers.UnitTestGenerator
{
    public class UnitTestGeneratorController : Controller
    {
        private readonly RoslynCodeAnalyzer _analyzer;
        private readonly TestQualityVerifier _qualityVerifier;
        private readonly BusinessDocParser _docParser;
        private readonly CustomCaseParser _caseParser;
        private readonly XunitMoqGenerator _generator;
        private readonly DotnetTestRunner _runner;
        private readonly TestRunHistoryService _historyService;
        private readonly IAiTestEnhancerService _aiService;

        public UnitTestGeneratorController(RoslynCodeAnalyzer analyzer,TestQualityVerifier qualityVerifier,BusinessDocParser docParser,CustomCaseParser caseParser,XunitMoqGenerator generator,DotnetTestRunner runner,TestRunHistoryService historyService,IAiTestEnhancerService aiService)
        {
            _analyzer = analyzer;
            _qualityVerifier = qualityVerifier;
            _docParser = docParser;
            _caseParser = caseParser;
            _generator = generator;
            _runner = runner;
            _historyService = historyService;
            _aiService = aiService;
        }

        [HttpGet]
        public IActionResult UnitTestGeneratorView()
        {
            var viewModel = new UnitTestGeneratorViewModel
            {
                PageTitle = "Enterprise Automated Unit Testing Engine",
                DefaultOutputTestDirectory = string.Empty,
                AiSettings = new AiSettingsModel
                {
                    Provider = "LocalHeuristic",
                    EndpointUrl = "http://localhost:11434",
                    ModelName = "llama3"
                }
            };

            return View("~/Views/UnitTestGenerator/UnitTestGeneratorView.cshtml", viewModel);
        }

        [HttpGet]
        public IActionResult BrowseDirectories(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Json(new
                    {
                        success = true,
                        currentPath = string.Empty,
                        parentPath = (string?)null,
                        directories = new List<object>(),
                        files = new List<object>()
                    });
                }

                path = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(path, mustExist: true);

                if (!Directory.Exists(path))
                {
                    return Json(new { success = false, message = $"Directory not found: '{path}'" });
                }

                var dirInfo = new DirectoryInfo(path);
                var parentPath = dirInfo.Parent?.FullName;

                var subDirs = dirInfo.GetDirectories()
                    .Where(d => !d.Name.StartsWith(".") && d.Name != "bin" && d.Name != "obj" && d.Name != "node_modules")
                    .Select(d => new
                    {
                        name = d.Name,
                        fullPath = d.FullName,
                        isDir = true,
                        isProject = d.GetFiles("*.csproj").Any(),
                        isSolution = d.GetFiles("*.sln").Any()
                    })
                    .OrderByDescending(d => d.isSolution || d.isProject)
                    .ThenBy(d => d.name)
                    .ToList();

                var files = dirInfo.GetFiles("*.cs")
                    .Concat(dirInfo.GetFiles("*.csproj"))
                    .Concat(dirInfo.GetFiles("*.sln"))
                    .Select(f => new
                    {
                        name = f.Name,
                        fullPath = f.FullName,
                        isDir = false,
                        isProject = f.Extension == ".csproj",
                        isSolution = f.Extension == ".sln"
                    })
                    .OrderBy(f => f.name)
                    .ToList();

                return Json(new
                {
                    success = true,
                    currentPath = dirInfo.FullName,
                    parentPath,
                    directories = subDirs,
                    files = files
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(ValueCountLimit = int.MaxValue, MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
        public async Task<IActionResult> AnalyzeProject(string? targetPath,IFormFile? projectZip,List<IFormFile>? csharpFiles,List<IFormFile>? folderFiles,[FromForm] List<string>? folderRelativePaths,string? existingTestPath = null)
        {
            try
            {
                var result = new ProjectAnalysisResult { Success = true };
                string workingDir = string.Empty;

                // Option 0: Complete Folder Uploaded (webkitdirectory)
                if (folderFiles != null && folderFiles.Count > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_Folder_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);

                    for (int i = 0; i < folderFiles.Count; i++)
                    {
                        var file = folderFiles[i];
                        var rel = (folderRelativePaths != null && i < folderRelativePaths.Count && !string.IsNullOrWhiteSpace(folderRelativePaths[i]))
                            ? folderRelativePaths[i]
                            : file.FileName;

                        var targetFilePath = WorkspaceSecurityValidator.ValidateRelativePath(tempFolder, rel);
                        var targetDir = Path.GetDirectoryName(targetFilePath);
                        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                        using var stream = new FileStream(targetFilePath, FileMode.Create);
                        await file.CopyToAsync(stream);
                    }

                    var classes = _analyzer.AnalyzeProjectFiles(tempFolder);
                    var csFileCount = Directory.GetFiles(tempFolder, "*.cs", SearchOption.AllDirectories).Length;
                    var rootFolder = folderRelativePaths?.FirstOrDefault()?.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "UploadedProject";

                    result.SourceType = "UploadedFolder";
                    result.ProjectName = rootFolder;
                    result.TotalSourceFiles = csFileCount;
                    result.TotalClasses = classes.Count;
                    result.TotalMethods = classes.Sum(c => c.Methods.Count);
                    result.Classes = classes;
                    result.ExtractedTempPath = tempFolder;
                    workingDir = tempFolder;
                }
                // Option 1: Complete Project ZIP Archive Uploaded
                else if (projectZip != null && projectZip.Length > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_Extract_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);

                    var tempZipPath = Path.Combine(tempFolder, "project_upload.zip");
                    using (var stream = new FileStream(tempZipPath, FileMode.Create))
                    {
                        await projectZip.CopyToAsync(stream);
                    }

                    // Extract ZIP safely with Zip-Slip & bomb protection
                    var extractDir = Path.Combine(tempFolder, "Extracted");
                    WorkspaceSecurityValidator.SafeExtractZip(tempZipPath, extractDir);

                    var classes = _analyzer.AnalyzeProjectFiles(extractDir);
                    var csFileCount = Directory.GetFiles(extractDir, "*.cs", SearchOption.AllDirectories).Length;

                    result.SourceType = "ZipArchive";
                    result.ProjectName = Path.GetFileNameWithoutExtension(projectZip.FileName);
                    result.TotalSourceFiles = csFileCount;
                    result.TotalClasses = classes.Count;
                    result.TotalMethods = classes.Sum(c => c.Methods.Count);
                    result.Classes = classes;
                    result.ExtractedTempPath = extractDir;
                    workingDir = extractDir;
                }
                // Option 2: Multiple or Single .cs Files Uploaded
                else if (csharpFiles != null && csharpFiles.Count > 0)
                {
                    var classes = new List<ClassAnalysis>();
                    foreach (var file in csharpFiles)
                    {
                        using var reader = new StreamReader(file.OpenReadStream());
                        var code = await reader.ReadToEndAsync();
                        var analyzed = _analyzer.AnalyzeSource(code, file.FileName);
                        classes.AddRange(analyzed);
                    }

                    result.SourceType = "UploadedFiles";
                    result.ProjectName = csharpFiles.Count == 1 ? csharpFiles[0].FileName : $"{csharpFiles.Count} Source Files";
                    result.TotalSourceFiles = csharpFiles.Count;
                    result.TotalClasses = classes.Count;
                    result.TotalMethods = classes.Sum(c => c.Methods.Count);
                    result.Classes = classes;
                }
                // Option 3: Local Directory or File Path Provided
                else if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    targetPath = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(targetPath);
                    if (System.IO.File.Exists(targetPath))
                    {
                        var classes = _analyzer.AnalyzeFile(targetPath);
                        result.SourceType = "SingleFilePath";
                        result.ProjectName = Path.GetFileName(targetPath);
                        result.TotalSourceFiles = 1;
                        result.TotalClasses = classes.Count;
                        result.TotalMethods = classes.Sum(c => c.Methods.Count);
                        result.Classes = classes;
                        workingDir = Path.GetDirectoryName(targetPath) ?? string.Empty;
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        var classes = _analyzer.AnalyzeProjectFiles(targetPath);
                        var csFileCount = Directory.GetFiles(targetPath, "*.cs", SearchOption.AllDirectories).Length;
                        result.SourceType = "ProjectDirectory";
                        result.ProjectName = new DirectoryInfo(targetPath).Name;
                        result.TotalSourceFiles = csFileCount;
                        result.TotalClasses = classes.Count;
                        result.TotalMethods = classes.Sum(c => c.Methods.Count);
                        result.Classes = classes;
                        workingDir = targetPath;
                    }
                    else
                    {
                        return Json(new { success = false, message = $"Path not found on disk: '{targetPath}'. Please verify the directory or file path." });
                    }
                }
                else
                {
                    return Json(new { success = false, message = "Please upload a project .ZIP archive, select a folder, upload .cs files, or specify a valid directory/file path." });
                }

                // Check for existing tests in working directory or supplied existingTestPath
                ExistingTestProjectAnalysis? existingTestAnalysis = null;
                string? discoveredTestDir = existingTestPath;

                if (string.IsNullOrWhiteSpace(discoveredTestDir) && !string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
                {
                    // Auto-discover any test directories inside the project
                    var testDirs = Directory.GetDirectories(workingDir, "*Test*", SearchOption.AllDirectories)
                                           .Where(d => !d.Contains(@"\bin\") && !d.Contains(@"\obj\"))
                                           .ToList();
                    if (testDirs.Any())
                    {
                        discoveredTestDir = testDirs.First();
                    }
                }

                if (!string.IsNullOrWhiteSpace(discoveredTestDir) && (Directory.Exists(discoveredTestDir) || System.IO.File.Exists(discoveredTestDir)))
                {
                    existingTestAnalysis = _analyzer.AnalyzeExistingTestProject(discoveredTestDir);
                }

                // Synthesize Step 8 Review Test Cases
                var reviewCases = _analyzer.SynthesizeReviewTestCases(result.Classes, existingTestAnalysis);

                return Json(new 
                { 
                    success = true, 
                    result, 
                    existingTests = existingTestAnalysis,
                    reviewTestCases = reviewCases,
                    totalReviewCases = reviewCases.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error analyzing project: " + ex.Message });
            }
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(ValueCountLimit = int.MaxValue, MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
        public async Task<IActionResult> AnalyzeDualProjects(string? webPath,string? apiPath,string? existingTestPath,string? solutionPath,IFormFile? webZip,IFormFile? apiZip,IFormFile? existingTestZip,List<IFormFile>? webFolderFiles,[FromForm] List<string>? webFolderRelativePaths,List<IFormFile>? apiFolderFiles,[FromForm] List<string>? apiFolderRelativePaths,IFormFile? brdFile,string? brdContent,string? customYaml)
        {
            try
            {
                var multiResult = new MultiProjectAnalysisResult { Success = true };

                // Auto-resolve paths if Solution Path is provided
                if (!string.IsNullOrWhiteSpace(solutionPath))
                {
                    solutionPath = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(solutionPath);
                    if (Directory.Exists(solutionPath))
                    {
                        multiResult.SolutionName = new DirectoryInfo(solutionPath).Name;
                        var subDirs = Directory.GetDirectories(solutionPath);

                        if (string.IsNullOrWhiteSpace(webPath))
                        {
                            var foundWeb = subDirs.FirstOrDefault(d => d.EndsWith("_web", StringComparison.OrdinalIgnoreCase) || d.Contains("web", StringComparison.OrdinalIgnoreCase) || d.Contains("WebClient", StringComparison.OrdinalIgnoreCase));
                            if (foundWeb != null) webPath = foundWeb;
                        }

                        if (string.IsNullOrWhiteSpace(apiPath))
                        {
                            var foundApi = subDirs.FirstOrDefault(d => d.EndsWith(".server", StringComparison.OrdinalIgnoreCase) || d.Contains("server", StringComparison.OrdinalIgnoreCase) || d.Contains(".API", StringComparison.OrdinalIgnoreCase));
                            if (foundApi != null) apiPath = foundApi;
                        }

                        if (string.IsNullOrWhiteSpace(existingTestPath))
                        {
                            // Look for .Tests in solution
                            var foundTests = Directory.GetDirectories(solutionPath, "*Tests*", SearchOption.AllDirectories)
                                                     .Where(d => !d.Contains(@"\bin\") && !d.Contains(@"\obj\"))
                                                     .FirstOrDefault();
                            if (foundTests != null) existingTestPath = foundTests;
                        }
                    }
                }

                // 1. Analyze Web Project
                List<ClassAnalysis> webClasses = new();
                if (webFolderFiles != null && webFolderFiles.Count > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_Web_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);
                    for (int i = 0; i < webFolderFiles.Count; i++)
                    {
                        var file = webFolderFiles[i];
                        var rel = (webFolderRelativePaths != null && i < webFolderRelativePaths.Count) ? webFolderRelativePaths[i] : file.FileName;
                        var targetFilePath = WorkspaceSecurityValidator.ValidateRelativePath(tempFolder, rel);
                        var targetDir = Path.GetDirectoryName(targetFilePath);
                        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                        using var stream = new FileStream(targetFilePath, FileMode.Create);
                        await file.CopyToAsync(stream);
                    }
                    webClasses = _analyzer.AnalyzeProjectFiles(tempFolder);
                    multiResult.WebProject = new ProjectAnalysisResult
                    {
                        Success = true,
                        SourceType = "UploadedFolder",
                        ProjectName = "Web Project",
                        TotalSourceFiles = Directory.GetFiles(tempFolder, "*.cs", SearchOption.AllDirectories).Length,
                        TotalClasses = webClasses.Count,
                        TotalMethods = webClasses.Sum(c => c.Methods.Count),
                        Classes = webClasses,
                        ExtractedTempPath = tempFolder
                    };
                }
                else if (webZip != null && webZip.Length > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_WebZip_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);
                    var tempZipPath = Path.Combine(tempFolder, "web_upload.zip");
                    using (var stream = new FileStream(tempZipPath, FileMode.Create)) await webZip.CopyToAsync(stream);
                    var extractDir = Path.Combine(tempFolder, "Extracted");
                    WorkspaceSecurityValidator.SafeExtractZip(tempZipPath, extractDir);
                    webClasses = _analyzer.AnalyzeProjectFiles(extractDir);
                    multiResult.WebProject = new ProjectAnalysisResult
                    {
                        Success = true,
                        SourceType = "ZipArchive",
                        ProjectName = Path.GetFileNameWithoutExtension(webZip.FileName),
                        TotalSourceFiles = Directory.GetFiles(extractDir, "*.cs", SearchOption.AllDirectories).Length,
                        TotalClasses = webClasses.Count,
                        TotalMethods = webClasses.Sum(c => c.Methods.Count),
                        Classes = webClasses,
                        ExtractedTempPath = extractDir
                    };
                }
                else if (!string.IsNullOrWhiteSpace(webPath))
                {
                    webPath = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(webPath);
                    if (Directory.Exists(webPath))
                    {
                        webClasses = _analyzer.AnalyzeProjectFiles(webPath);
                        multiResult.WebProject = new ProjectAnalysisResult
                        {
                            Success = true,
                            SourceType = "ProjectDirectory",
                            ProjectName = new DirectoryInfo(webPath).Name,
                            TotalSourceFiles = Directory.GetFiles(webPath, "*.cs", SearchOption.AllDirectories).Length,
                            TotalClasses = webClasses.Count,
                            TotalMethods = webClasses.Sum(c => c.Methods.Count),
                            Classes = webClasses,
                            ExtractedTempPath = webPath
                        };
                    }
                }

                // 2. Analyze API Project
                List<ClassAnalysis> apiClasses = new();
                if (apiFolderFiles != null && apiFolderFiles.Count > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_Api_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);
                    for (int i = 0; i < apiFolderFiles.Count; i++)
                    {
                        var file = apiFolderFiles[i];
                        var rel = (apiFolderRelativePaths != null && i < apiFolderRelativePaths.Count) ? apiFolderRelativePaths[i] : file.FileName;
                        var targetFilePath = WorkspaceSecurityValidator.ValidateRelativePath(tempFolder, rel);
                        var targetDir = Path.GetDirectoryName(targetFilePath);
                        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                        using var stream = new FileStream(targetFilePath, FileMode.Create);
                        await file.CopyToAsync(stream);
                    }
                    apiClasses = _analyzer.AnalyzeProjectFiles(tempFolder);
                    multiResult.ApiProject = new ProjectAnalysisResult
                    {
                        Success = true,
                        SourceType = "UploadedFolder",
                        ProjectName = "Core API Project",
                        TotalSourceFiles = Directory.GetFiles(tempFolder, "*.cs", SearchOption.AllDirectories).Length,
                        TotalClasses = apiClasses.Count,
                        TotalMethods = apiClasses.Sum(c => c.Methods.Count),
                        Classes = apiClasses,
                        ExtractedTempPath = tempFolder
                    };
                }
                else if (apiZip != null && apiZip.Length > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_ApiZip_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);
                    var tempZipPath = Path.Combine(tempFolder, "api_upload.zip");
                    using (var stream = new FileStream(tempZipPath, FileMode.Create)) await apiZip.CopyToAsync(stream);
                    var extractDir = Path.Combine(tempFolder, "Extracted");
                    WorkspaceSecurityValidator.SafeExtractZip(tempZipPath, extractDir);
                    apiClasses = _analyzer.AnalyzeProjectFiles(extractDir);
                    multiResult.ApiProject = new ProjectAnalysisResult
                    {
                        Success = true,
                        SourceType = "ZipArchive",
                        ProjectName = Path.GetFileNameWithoutExtension(apiZip.FileName),
                        TotalSourceFiles = Directory.GetFiles(extractDir, "*.cs", SearchOption.AllDirectories).Length,
                        TotalClasses = apiClasses.Count,
                        TotalMethods = apiClasses.Sum(c => c.Methods.Count),
                        Classes = apiClasses,
                        ExtractedTempPath = extractDir
                    };
                }
                else if (!string.IsNullOrWhiteSpace(apiPath))
                {
                    apiPath = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(apiPath);
                    if (Directory.Exists(apiPath))
                    {
                        apiClasses = _analyzer.AnalyzeProjectFiles(apiPath);
                        multiResult.ApiProject = new ProjectAnalysisResult
                        {
                            Success = true,
                            SourceType = "ProjectDirectory",
                            ProjectName = new DirectoryInfo(apiPath).Name,
                            TotalSourceFiles = Directory.GetFiles(apiPath, "*.cs", SearchOption.AllDirectories).Length,
                            TotalClasses = apiClasses.Count,
                            TotalMethods = apiClasses.Sum(c => c.Methods.Count),
                            Classes = apiClasses,
                            ExtractedTempPath = apiPath
                        };
                    }
                }

                // 3. Analyze Existing Test Project
                ExistingTestProjectAnalysis? existingTestAnalysis = null;
                if (!string.IsNullOrWhiteSpace(existingTestPath) && (Directory.Exists(existingTestPath) || System.IO.File.Exists(existingTestPath)))
                {
                    existingTestAnalysis = _analyzer.AnalyzeExistingTestProject(existingTestPath);
                    multiResult.ExistingTestProject = existingTestAnalysis;
                }
                else if (!string.IsNullOrWhiteSpace(apiPath) && Directory.Exists(apiPath))
                {
                    // Check inside API folder for .Tests
                    var testDirs = Directory.GetDirectories(apiPath, "*Test*", SearchOption.AllDirectories)
                                           .Where(d => !d.Contains(@"\bin\") && !d.Contains(@"\obj\"))
                                           .ToList();
                    if (testDirs.Any())
                    {
                        existingTestAnalysis = _analyzer.AnalyzeExistingTestProject(testDirs.First());
                        multiResult.ExistingTestProject = existingTestAnalysis;
                    }
                }

                // Combine all classes
                multiResult.AllDiscoveredClasses = webClasses.Concat(apiClasses).ToList();

                // 4. Analyze Web to API Communication
                if (webClasses.Any() && apiClasses.Any())
                {
                    multiResult.WebToApiMappings = _analyzer.AnalyzeWebToApiCommunication(webClasses, apiClasses);
                }

                // 5. Parse BRD Rules if uploaded
                List<BusinessRuleModel>? brdRules = null;
                if (brdFile != null && brdFile.Length > 0)
                {
                    using var stream = brdFile.OpenReadStream();
                    brdRules = _docParser.ParseStream(stream, brdFile.FileName);
                }
                else if (!string.IsNullOrWhiteSpace(brdContent))
                {
                    brdRules = _docParser.ParseDocumentText(brdContent, "Inline_BRD.txt");
                }

                // 6. Parse Custom Scenarios if provided
                CustomScenarioFile? customCases = null;
                if (!string.IsNullOrWhiteSpace(customYaml))
                {
                    customCases = _caseParser.ParseYaml(customYaml);
                }

                // 7. Synthesize Step 8 Review Test Cases across all discovered classes
                multiResult.ReviewTestCases = _analyzer.SynthesizeReviewTestCases(
                    multiResult.AllDiscoveredClasses, 
                    existingTestAnalysis, 
                    brdRules, 
                    customCases);

                return Json(new { success = true, result = multiResult });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error analyzing dual projects: " + ex.Message });
            }
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(ValueCountLimit = int.MaxValue, MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
        public async Task<IActionResult> GenerateTests(string? targetPath,string? selectedClassName,[FromForm] List<string>? selectedMethods,string? customYaml,string? brdContent,IFormFile? brdFile,IFormFile? projectZip,List<IFormFile>? csharpFiles,List<IFormFile>? folderFiles,[FromForm] List<string>? folderRelativePaths,bool enableAi = false,string? aiProvider = null,string? aiEndpoint = null,string? aiApiKey = null,string? aiModel = null)
        {
            try
            {
                // Handle BRD document upload if supplied
                var businessRules = new List<BusinessRuleModel>();
                if (brdFile != null && brdFile.Length > 0)
                {
                    using var stream = brdFile.OpenReadStream();
                    businessRules = _docParser.ParseStream(stream, brdFile.FileName);
                }
                else if (!string.IsNullOrWhiteSpace(brdContent))
                {
                    businessRules = _docParser.ParseDocumentText(brdContent, "Inline_BRD.txt");
                }

                // Handle YAML custom scenarios
                CustomScenarioFile? customCases = null;
                if (!string.IsNullOrWhiteSpace(customYaml))
                {
                    customCases = _caseParser.ParseYaml(customYaml);
                }

                // Retrieve Target Class Analysis
                List<ClassAnalysis> classes = new();

                if (folderFiles != null && folderFiles.Count > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_FolderGen_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempFolder);

                    for (int i = 0; i < folderFiles.Count; i++)
                    {
                        var file = folderFiles[i];
                        var rel = (folderRelativePaths != null && i < folderRelativePaths.Count && !string.IsNullOrWhiteSpace(folderRelativePaths[i]))
                            ? folderRelativePaths[i]
                            : file.FileName;

                        var cleanRel = rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                        var targetFilePath = Path.Combine(tempFolder, cleanRel);
                        var targetDir = Path.GetDirectoryName(targetFilePath);
                        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                        using var stream = new FileStream(targetFilePath, FileMode.Create);
                        await file.CopyToAsync(stream);
                    }
                    classes = _analyzer.AnalyzeProjectFiles(tempFolder);
                }
                else if (projectZip != null && projectZip.Length > 0)
                {
                    var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_Gen_" + Guid.NewGuid().ToString("N"));
                    var tempZipPath = Path.Combine(tempFolder, projectZip.FileName);
                    Directory.CreateDirectory(tempFolder);
                    using (var stream = new FileStream(tempZipPath, FileMode.Create))
                    {
                        await projectZip.CopyToAsync(stream);
                    }
                    var extractDir = Path.Combine(tempFolder, "Extracted");
                    ZipFile.ExtractToDirectory(tempZipPath, extractDir, true);
                    classes = _analyzer.AnalyzeProjectFiles(extractDir);
                }
                else if (csharpFiles != null && csharpFiles.Count > 0)
                {
                    foreach (var file in csharpFiles)
                    {
                        using var reader = new StreamReader(file.OpenReadStream());
                        var code = await reader.ReadToEndAsync();
                        classes.AddRange(_analyzer.AnalyzeSource(code, file.FileName));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    if (System.IO.File.Exists(targetPath))
                    {
                        classes = _analyzer.AnalyzeFile(targetPath);
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        classes = _analyzer.AnalyzeProjectFiles(targetPath);
                    }
                }

                if (classes.Count == 0)
                {
                    return Json(new { success = false, message = "No valid C# classes could be identified from the input source." });
                }

                // Filter class if a specific class is selected
                var targetClass = !string.IsNullOrWhiteSpace(selectedClassName) && selectedClassName != "ALL"
                    ? classes.FirstOrDefault(c => c.ClassName == selectedClassName) ?? classes.First()
                    : classes.First();

                // 1. Generate base xUnit + Moq tests via Roslyn AST with metadata
                var (testCode, testCases) = _generator.GenerateTestClassWithMetadata(targetClass, businessRules, customCases, selectedMethods);

                // 2. If AI enhancement is requested, run AI service
                AiEnhancementResult? aiResult = null;
                if (enableAi)
                {
                    var aiSettings = new AiSettingsModel
                    {
                        Provider = aiProvider ?? "LocalHeuristic",
                        EndpointUrl = aiEndpoint ?? "http://localhost:11434",
                        ApiKey = aiApiKey ?? string.Empty,
                        ModelName = aiModel ?? "llama3"
                    };

                    aiResult = await _aiService.EnhanceTestsAsync(targetClass, testCode, businessRules, aiSettings);
                    if (aiResult.Success && !string.IsNullOrWhiteSpace(aiResult.EnhancedTestCode))
                    {
                        testCode = aiResult.EnhancedTestCode;
                        if (aiResult.InferredEdgeCases != null)
                        {
                            foreach (var ec in aiResult.InferredEdgeCases)
                            {
                                testCases.Add(new GeneratedTestCaseSummary
                                {
                                    TestName = "AI_Synthesized_EdgeCase",
                                    TargetMethod = targetClass.Methods.FirstOrDefault()?.MethodName ?? "General",
                                    Category = "Boundary",
                                    Description = ec,
                                    ExpectedAssertion = "Handles enterprise boundary condition defensively"
                                });
                            }
                        }
                    }
                }

                // 3. Automated Syntax & Compiler Error Validation Check (Roslyn diagnostics)
                var testFileName = $"{targetClass.ClassName}Tests.cs";
                var diagnostics = _analyzer.ValidateGeneratedTest(testCode, testFileName);

                // 4. Test Quality & Weak Assertion Verification
                var qualityReport = _qualityVerifier.VerifyTestQuality(testCode, testFileName);

                // 5. Structured Test Specifications
                var plannedSpecs = _generator.PlanTestSpecifications(targetClass, businessRules, customCases, selectedMethods);

                // 6. Generate Traceability Matrix
                var traceability = await _aiService.AnalyzeTraceabilityAsync(targetClass, businessRules);

                return Json(new
                {
                    success = true,
                    className = targetClass.ClassName,
                    projectRole = targetClass.ProjectRole,
                    totalClassesDiscovered = classes.Count,
                    generatedTestCode = testCode,
                    testCases = testCases,
                    plannedSpecs = plannedSpecs,
                    qualityReport = qualityReport,
                    diagnostics = diagnostics,
                    hasErrors = diagnostics.Count > 0,
                    businessRulesCount = businessRules.Count,
                    customCasesCount = customCases?.CustomTestCases.Count ?? 0,
                    aiEnhanced = enableAi,
                    aiDetails = aiResult,
                    traceability
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Test generation failed: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AiEnhanceTests([FromBody] JsonElement payload)
        {
            try
            {
                var className = payload.GetProperty("className").GetString() ?? "TargetClass";
                var testCode = payload.GetProperty("testCode").GetString() ?? "";
                var provider = payload.TryGetProperty("provider", out var p) ? p.GetString() : "LocalHeuristic";
                var endpoint = payload.TryGetProperty("endpoint", out var ep) ? ep.GetString() : "http://localhost:11434";
                var apiKey = payload.TryGetProperty("apiKey", out var key) ? key.GetString() : "";
                var model = payload.TryGetProperty("model", out var mdl) ? mdl.GetString() : "llama3";

                var dummyClass = new ClassAnalysis
                {
                    ClassName = className,
                    Namespace = "IdeaInfinityIT.IERP",
                    Methods = new List<MethodAnalysis>
                    {
                        new MethodAnalysis { MethodName = "ProcessTransaction", ReturnType = "Task<bool>", IsAsync = true,
                            Parameters = new List<ParameterModel> { new ParameterModel { Name = "transactionId", Type = "long" }, new ParameterModel { Name = "payload", Type = "object" } } },
                        new MethodAnalysis { MethodName = "ValidateEntity", ReturnType = "bool", IsAsync = false,
                            Parameters = new List<ParameterModel> { new ParameterModel { Name = "entityCode", Type = "string" } } }
                    }
                };

                var settings = new AiSettingsModel
                {
                    Provider = provider ?? "LocalHeuristic",
                    EndpointUrl = endpoint ?? "http://localhost:11434",
                    ApiKey = apiKey ?? "",
                    ModelName = model ?? "llama3"
                };

                var result = await _aiService.EnhanceTestsAsync(dummyClass, testCode, null, settings);

                return Json(new { success = true, result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveTests(string targetDirectory, string className, string testCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    return Json(new { success = false, message = "Please specify a destination folder path where the test file should be saved." });
                }

                var canonicalDir = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(targetDirectory, mustExist: false);
                if (!Directory.Exists(canonicalDir))
                {
                    Directory.CreateDirectory(canonicalDir);
                }

                var fileName = $"{className}Tests.cs";
                var fullPath = Path.Combine(canonicalDir, fileName);
                WorkspaceSecurityValidator.ValidateTestSavePath(fullPath);
                System.IO.File.WriteAllText(fullPath, testCode, Encoding.UTF8);

                return Json(new { success = true, savedPath = fullPath, fileName });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Could not save test file: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult RunTests(string testProjectPath, string? filterClass = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(testProjectPath))
                {
                    return Json(new { success = false, message = "Target test project path is required." });
                }

                testProjectPath = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(testProjectPath, mustExist: true);

                var summary = _runner.RunTests(testProjectPath, filterClass: filterClass);
                var projName = Path.GetFileNameWithoutExtension(testProjectPath);
                _historyService.SaveRun(summary, projName);

                var recentRuns = _historyService.GetRecentRuns(10);
                var baseline = recentRuns.Skip(1).FirstOrDefault();
                var comparison = _historyService.CompareWithBaseline(summary, baseline);

                return Json(new
                {
                    success = true,
                    runId = summary.RunId,
                    passed = summary.Passed,
                    failed = summary.Failed,
                    skipped = summary.Skipped,
                    total = summary.TotalTests,
                    lineCoverage = summary.LineCoveragePercentage,
                    branchCoverage = summary.BranchCoveragePercentage,
                    coverageCollected = summary.CoverageCollected,
                    durationSeconds = summary.DurationSeconds,
                    compilationErrors = summary.CompilationErrors,
                    testFailures = summary.TestFailures,
                    comparison = comparison,
                    logs = summary.Logs
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Test execution failed: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetRunHistory()
        {
            try
            {
                var runs = _historyService.GetRecentRuns(20);
                return Json(new { success = true, runs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ExportMatrixCsv([FromBody] List<TestCaseReviewItem> testCases)
        {
            try
            {
                var csv = _historyService.ExportTestMatrixToCsv(testCases ?? new List<TestCaseReviewItem>());
                var bytes = Encoding.UTF8.GetBytes(csv);
                return File(bytes, "text/csv", "TestCaseReviewMatrix.csv");
            }
            catch (Exception ex)
            {
                return BadRequest("CSV export failed: " + ex.Message);
            }
        }

        [HttpPost]
        public IActionResult ExportMarkdownReport([FromBody] TestRunSummary summary)
        {
            try
            {
                var md = _historyService.ExportTestReportToMarkdown(summary ?? new TestRunSummary());
                var bytes = Encoding.UTF8.GetBytes(md);
                return File(bytes, "text/markdown", "TestExecutionReport.md");
            }
            catch (Exception ex)
            {
                return BadRequest("Markdown export failed: " + ex.Message);
            }
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        public IActionResult GenerateTestsFromReviewCases(string? targetClassName,string? targetPath,string? existingTestFilePath,bool isAugmentExisting,string? reviewCasesJson)
        {
            try
            {
                var approvedCases = new List<TestCaseReviewItem>();
                if (!string.IsNullOrWhiteSpace(reviewCasesJson))
                {
                    try
                    {
                        approvedCases = JsonSerializer.Deserialize<List<TestCaseReviewItem>>(reviewCasesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    }
                    catch { }
                }

                // Locate ClassAnalysis
                ClassAnalysis? targetClass = null;
                if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    if (System.IO.File.Exists(targetPath))
                    {
                        targetClass = _analyzer.AnalyzeFile(targetPath).FirstOrDefault(c => string.Equals(c.ClassName, targetClassName, StringComparison.OrdinalIgnoreCase))
                                     ?? _analyzer.AnalyzeFile(targetPath).FirstOrDefault();
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        var allClasses = _analyzer.AnalyzeProjectFiles(targetPath);
                        targetClass = allClasses.FirstOrDefault(c => string.Equals(c.ClassName, targetClassName, StringComparison.OrdinalIgnoreCase))
                                     ?? allClasses.FirstOrDefault();
                    }
                }

                // If not found in targetPath, search sibling/parent workspace projects (e.g. cesc_mms_web, core.mms.server)
                if (targetClass == null && !string.IsNullOrWhiteSpace(targetClassName))
                {
                    var candidatePaths = new List<string>();
                    if (!string.IsNullOrWhiteSpace(targetPath))
                    {
                        var dir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            var parent = Directory.GetParent(dir)?.FullName;
                            if (!string.IsNullOrEmpty(parent)) candidatePaths.Add(parent);
                        }
                    }

                    // Sibling projects in workspace
                    var baseAppDir = AppDomain.CurrentDomain.BaseDirectory;
                    var slnRoot = Directory.GetParent(baseAppDir)?.Parent?.Parent?.Parent?.Parent?.FullName;
                    if (!string.IsNullOrEmpty(slnRoot) && Directory.Exists(slnRoot)) candidatePaths.Add(slnRoot);

                    foreach (var root in candidatePaths.Distinct())
                    {
                        try
                        {
                            var matchFile = Directory.GetFiles(root, $"{targetClassName}.cs", SearchOption.AllDirectories)
                                .FirstOrDefault(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"));
                            if (!string.IsNullOrEmpty(matchFile) && System.IO.File.Exists(matchFile))
                            {
                                var analyzed = _analyzer.AnalyzeFile(matchFile);
                                targetClass = analyzed.FirstOrDefault(c => string.Equals(c.ClassName, targetClassName, StringComparison.OrdinalIgnoreCase))
                                             ?? analyzed.FirstOrDefault();
                                if (targetClass != null) break;
                            }
                        }
                        catch { }
                    }
                }

                if (targetClass == null)
                {
                    // Fallback synthetic class if path not available
                    targetClass = new ClassAnalysis
                    {
                        ClassName = targetClassName ?? "TargetController",
                        Namespace = "IdeaInfinityIT.IERPSuite",
                        IsWebController = targetClassName?.Contains("Web") == true || targetClassName?.EndsWith("Controller") == true,
                        IsApiController = targetClassName?.Contains("API") == true || targetClassName?.EndsWith("BaseController") == true
                    };
                }

                // Ensure methods are populated from approvedCases if class had 0 methods or was synthesized
                if (targetClass.Methods.Count == 0 && approvedCases.Any())
                {
                    var approvedMethodsList = approvedCases.Select(c => c.TargetMethod).Where(m => !string.IsNullOrWhiteSpace(m) && m != "General").Distinct().ToList();
                    foreach (var m in approvedMethodsList)
                    {
                        targetClass.Methods.Add(new MethodAnalysis
                        {
                            MethodName = m,
                            ReturnType = targetClass.IsWebController || targetClass.IsApiController ? "Task<IActionResult>" : "Task<bool>",
                            IsAsync = true,
                            Parameters = new List<ParameterModel>()
                        });
                    }
                }

                string testCode;
                var testCaseSummaries = new List<GeneratedTestCaseSummary>();

                if (isAugmentExisting && !string.IsNullOrWhiteSpace(existingTestFilePath) && System.IO.File.Exists(existingTestFilePath))
                {
                    testCode = _generator.AugmentExistingTestClass(existingTestFilePath, targetClass, approvedCases);
                    testCaseSummaries = approvedCases.Where(c => c.IsApproved).Select(c => new GeneratedTestCaseSummary
                    {
                        TestName = c.TestName,
                        TargetMethod = c.TargetMethod,
                        Category = c.Category,
                        Description = c.TestInput,
                        ExpectedAssertion = c.ExpectedResult
                    }).ToList();
                }
                else
                {
                    var approvedMethods = approvedCases.Where(c => c.IsApproved).Select(c => c.TargetMethod).Distinct().ToList();
                    var (code, summaries) = _generator.GenerateTestClassWithMetadata(targetClass, selectedMethods: approvedMethods);
                    testCode = code;
                    testCaseSummaries = summaries;
                }

                var diagnostics = _analyzer.ValidateGeneratedTest(testCode, $"{targetClass.ClassName}Tests.cs");

                return Json(new
                {
                    success = true,
                    className = targetClass.ClassName,
                    testCode,
                    testCases = testCaseSummaries,
                    diagnostics,
                    isAugmented = isAugmentExisting
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error generating tests from review cases: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveAugmentedCodeToFile(string filePath, string updatedCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return Json(new { success = false, message = "Target file path is required." });

                WorkspaceSecurityValidator.ValidateTestSavePath(filePath);
                var canonicalPath = WorkspaceSecurityValidator.ValidateAndCanonicalizePath(filePath, mustExist: false);

                var dir = Path.GetDirectoryName(canonicalPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await System.IO.File.WriteAllTextAsync(canonicalPath, updatedCode, Encoding.UTF8);
                return Json(new { success = true, message = $"Successfully saved updated test class to: {canonicalPath}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving file: " + ex.Message });
            }
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(ValueCountLimit = int.MaxValue, MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
        public async Task<IActionResult> DownloadAllTestsZip(string? targetPath,IFormFile? projectZip,List<IFormFile>? csharpFiles,List<IFormFile>? folderFiles,[FromForm] List<string>? folderRelativePaths,string? currentTestCode,string? currentClassName,string? selectedClassName = null)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    // 1. Add standard .NET 8 Test Project (.csproj)
                    var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <IsPackable>false</IsPackable>
                    <IsTestProject>true</IsTestProject>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include=""coverlet.collector"" Version=""6.0.0"">
                      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
                      <PrivateAssets>all</PrivateAssets>
                    </PackageReference>
                    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
                    <PackageReference Include=""xunit"" Version=""2.5.3"" />
                    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.3"">
                      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
                      <PrivateAssets>all</PrivateAssets>
                    </PackageReference>
                    <PackageReference Include=""Moq"" Version=""4.20.70"" />
                    <PackageReference Include=""FluentAssertions"" Version=""6.12.0"" />
                  </ItemGroup>
                </Project>";
                    var csprojEntry = archive.CreateEntry("GeneratedTests.csproj");
                    using (var writer = new StreamWriter(csprojEntry.Open(), Encoding.UTF8))
                    {
                        await writer.WriteAsync(csprojContent);
                    }

                    // 2. Add README.md with instructions
                    var readmeContent = @"# Generated Enterprise xUnit + Moq Test Suite
                    Generated by CESC i~ERP Automated Unit Test Engineering Platform.

                    ## Requirements
                    - .NET 8.0 SDK (`dotnet --version`)

                    ## Running Unit Tests
                    1. Reference your target application or API project:
                       ```bash
                       dotnet add reference <path-to-target-project.csproj>
                       ```
                    2. Run tests in terminal:
                       ```bash
                       dotnet test --logger ""console;verbosity=normal""
                       ```
                    3. Run tests with coverage:
                       ```bash
                       dotnet test --collect:""XPlat Code Coverage""
                       ```
                    ";
                    var readmeEntry = archive.CreateEntry("README.md");
                    using (var writer = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
                    {
                        await writer.WriteAsync(readmeContent);
                    }

                    // 3. If the user passed the current active test code, save that immediately
                    if (!string.IsNullOrWhiteSpace(currentTestCode))
                    {
                        var singleFileName = (string.IsNullOrWhiteSpace(currentClassName) ? "Generated" : currentClassName) + "Tests.cs";
                        var activeEntry = archive.CreateEntry(singleFileName);
                        using var writer = new StreamWriter(activeEntry.Open(), Encoding.UTF8);
                        await writer.WriteAsync(currentTestCode);
                    }

                    // 4. Also scan any project files ONLY if batch archive for ALL classes is explicitly requested
                    bool isBatchAll = (string.IsNullOrWhiteSpace(selectedClassName) || string.Equals(selectedClassName, "ALL", StringComparison.OrdinalIgnoreCase)) &&
                                      (string.IsNullOrWhiteSpace(currentClassName) || string.Equals(currentClassName, "ALL", StringComparison.OrdinalIgnoreCase) || string.Equals(currentClassName, "Generated", StringComparison.OrdinalIgnoreCase));

                    if (isBatchAll)
                    {
                        List<ClassAnalysis> classes = new();
                    if (folderFiles != null && folderFiles.Count > 0)
                    {
                        var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_ZipFolder_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempFolder);
                        for (int i = 0; i < folderFiles.Count; i++)
                        {
                            var file = folderFiles[i];
                            var rel = (folderRelativePaths != null && i < folderRelativePaths.Count && !string.IsNullOrWhiteSpace(folderRelativePaths[i]))? folderRelativePaths[i] : file.FileName;
                            var cleanRel = rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                            var targetFilePath = Path.Combine(tempFolder, cleanRel);
                            var targetDir = Path.GetDirectoryName(targetFilePath);
                            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                            using var stream = new FileStream(targetFilePath, FileMode.Create);
                            await file.CopyToAsync(stream);
                        }
                        classes = _analyzer.AnalyzeProjectFiles(tempFolder);
                    }
                    else if (projectZip != null && projectZip.Length > 0)
                    {
                        var tempFolder = Path.Combine(Path.GetTempPath(), "EnterpriseTestGen_ZipDownload_" + Guid.NewGuid().ToString("N"));
                        var tempZipPath = Path.Combine(tempFolder, projectZip.FileName);
                        Directory.CreateDirectory(tempFolder);
                        using (var stream = new FileStream(tempZipPath, FileMode.Create))
                        {
                            await projectZip.CopyToAsync(stream);
                        }
                        var extractDir = Path.Combine(tempFolder, "Extracted");
                        ZipFile.ExtractToDirectory(tempZipPath, extractDir, true);
                        classes = _analyzer.AnalyzeProjectFiles(extractDir);
                    }
                    else if (csharpFiles != null && csharpFiles.Count > 0)
                    {
                        foreach (var file in csharpFiles)
                        {
                            using var reader = new StreamReader(file.OpenReadStream());
                            var code = await reader.ReadToEndAsync();
                            classes.AddRange(_analyzer.AnalyzeSource(code, file.FileName));
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(targetPath))
                    {
                        if (Directory.Exists(targetPath)) classes = _analyzer.AnalyzeProjectFiles(targetPath);
                        else if (System.IO.File.Exists(targetPath)) classes = _analyzer.AnalyzeFile(targetPath);
                    }

                    // Write each analyzed class tests (if not already written as activeEntry)
                    foreach (var cls in classes)
                    {
                        var fileName = $"{cls.ClassName}Tests.cs";
                        if (!string.IsNullOrWhiteSpace(currentTestCode) && cls.ClassName == currentClassName)
                        {
                            // Already included active version
                            continue;
                        }

                        var code = _generator.GenerateTestClass(cls);
                        var entry = archive.CreateEntry(fileName);
                        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                        await writer.WriteAsync(code);
                    }
                }
                }

                memoryStream.Position = 0;
                var downloadName = !string.IsNullOrWhiteSpace(currentClassName) 
                    ? $"{currentClassName}_TestProject.zip" 
                    : "Generated_xUnit_TestSuite.zip";
                return File(memoryStream.ToArray(), "application/zip", downloadName);
            }
            catch (Exception ex)
            {
                return BadRequest("Failed creating ZIP: " + ex.Message);
            }
        }
    }
}

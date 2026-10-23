using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IdeaInfinityIT.EnterpriseTestGen.Roslyn
{
    public class RoslynCodeAnalyzer
    {
        private readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, long FileSize, List<ClassAnalysis> Result)> _fileCache = new(StringComparer.OrdinalIgnoreCase);

        public void ClearCache() => _fileCache.Clear();

        public DiscoveredProjectInfo DiscoverProject(string csprojPath)
        {
            var projectInfo = new DiscoveredProjectInfo
            {
                ProjectFilePath = csprojPath,
                ProjectName = Path.GetFileNameWithoutExtension(csprojPath)
            };

            if (!File.Exists(csprojPath))
                return projectInfo;

            try
            {
                var doc = XDocument.Load(csprojPath);
                var root = doc.Root;
                if (root != null)
                {
                    projectInfo.Sdk = root.Attribute("Sdk")?.Value ?? "Microsoft.NET.Sdk";

                    var tfElem = root.Descendants("TargetFramework").FirstOrDefault() 
                                 ?? root.Descendants("TargetFrameworks").FirstOrDefault();
                    if (tfElem != null)
                    {
                        projectInfo.TargetFramework = tfElem.Value.Trim();
                    }

                    // Package References
                    foreach (var pkg in root.Descendants("PackageReference"))
                    {
                        var name = pkg.Attribute("Include")?.Value ?? pkg.Attribute("Update")?.Value ?? "";
                        var version = pkg.Attribute("Version")?.Value ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            projectInfo.PackageReferences.Add(string.IsNullOrEmpty(version) ? name : $"{name} ({version})");

                            var lower = name.ToLowerInvariant();
                            if (lower.Contains("xunit")) projectInfo.TestFrameworkDetected = "xUnit";
                            else if (lower.Contains("nunit")) projectInfo.TestFrameworkDetected = "NUnit";
                            else if (lower.Contains("mstest") || lower.Contains("testframework")) projectInfo.TestFrameworkDetected = "MSTest";

                            if (lower.Contains("moq")) projectInfo.MockFrameworkDetected = "Moq";
                            else if (lower.Contains("nsubstitute")) projectInfo.MockFrameworkDetected = "NSubstitute";
                            else if (lower.Contains("fakeiteasy")) projectInfo.MockFrameworkDetected = "FakeItEasy";
                        }
                    }

                    // Project References
                    foreach (var pref in root.Descendants("ProjectReference"))
                    {
                        var inc = pref.Attribute("Include")?.Value ?? "";
                        if (!string.IsNullOrEmpty(inc))
                        {
                            projectInfo.ProjectReferences.Add(inc);
                        }
                    }

                    // Project Type Determination
                    bool isTest = projectInfo.TestFrameworkDetected != "None" ||
                                  root.Descendants("IsTestProject").Any(x => x.Value.Equals("true", StringComparison.OrdinalIgnoreCase)) ||
                                  projectInfo.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
                                  projectInfo.ProjectName.EndsWith("Test", StringComparison.OrdinalIgnoreCase);

                    if (isTest)
                    {
                        projectInfo.ProjectType = "TestProject";
                    }
                    else if (projectInfo.Sdk.Contains("Microsoft.NET.Sdk.Web") || 
                             projectInfo.PackageReferences.Any(p => p.Contains("Microsoft.AspNetCore.OpenApi") || p.Contains("Swashbuckle")))
                    {
                        projectInfo.ProjectType = projectInfo.ProjectName.Contains("WebClient", StringComparison.OrdinalIgnoreCase) || 
                                                  projectInfo.ProjectName.Contains(".Web", StringComparison.OrdinalIgnoreCase)
                            ? "WebMvc" 
                            : "WebApi";
                    }
                    else
                    {
                        projectInfo.ProjectType = "ClassLibrary";
                    }
                }

                var projectDir = Path.GetDirectoryName(csprojPath) ?? "";
                if (Directory.Exists(projectDir))
                {
                    projectInfo.SourceFilesCount = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                        .Count(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"));
                }
            }
            catch
            {
                // Graceful fallback for non-standard XML
            }

            return projectInfo;
        }

        public DiscoveredSolutionInfo DiscoverSolution(string slnPath)
        {
            var solutionInfo = new DiscoveredSolutionInfo
            {
                SolutionFilePath = slnPath,
                SolutionName = Path.GetFileNameWithoutExtension(slnPath)
            };

            if (!File.Exists(slnPath))
                return solutionInfo;

            var slnDir = Path.GetDirectoryName(slnPath) ?? "";
            var lines = File.ReadAllLines(slnPath);

            foreach (var line in lines)
            {
                // Format: Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Name", "RelativePath\Proj.csproj", "{GUID}"
                if (line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase) && line.Contains(".csproj"))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        var relativePath = parts[1].Trim(' ', '"', '\'').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                        var fullProjPath = Path.IsPathRooted(relativePath) ? relativePath : Path.GetFullPath(Path.Combine(slnDir, relativePath));
                        if (File.Exists(fullProjPath))
                        {
                            solutionInfo.Projects.Add(DiscoverProject(fullProjPath));
                        }
                    }
                }
            }

            return solutionInfo;
        }

        public List<ClassAnalysis> AnalyzeProjectFiles(string projectDirectory)
        {
            var results = new List<ClassAnalysis>();
            var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                                   .Where(f => !f.Contains(@"\bin\") && 
                                               !f.Contains(@"\obj\") && 
                                               !f.Contains(".Designer.cs") &&
                                               !f.EndsWith("Tests.cs"));

            foreach (var file in csFiles)
            {
                var classes = AnalyzeFile(file);
                results.AddRange(classes);
            }

            return results;
        }

        public List<ClassAnalysis> AnalyzeFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<ClassAnalysis>();

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (_fileCache.TryGetValue(filePath, out var cached))
                {
                    if (cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc && cached.FileSize == fileInfo.Length)
                    {
                        return cached.Result;
                    }
                }

                var code = File.ReadAllText(filePath);
                var result = AnalyzeSource(code, filePath);
                _fileCache[filePath] = (fileInfo.LastWriteTimeUtc, fileInfo.Length, result);
                return result;
            }
            catch
            {
                var code = File.ReadAllText(filePath);
                return AnalyzeSource(code, filePath);
            }
        }

        public List<ClassAnalysis> AnalyzeSource(string sourceCode, string filePath = "")
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            var results = new List<ClassAnalysis>();

            foreach (var classDecl in classDeclarations)
            {
                if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword)))
                    continue;

                var namespaceDecl = classDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                var nsName = namespaceDecl?.Name.ToString() ?? "Global";

                var baseTypes = classDecl.BaseList?.Types.Select(t => t.Type.ToString()).ToList() ?? new List<string>();
                var baseType = baseTypes.FirstOrDefault() ?? "";
                var className = classDecl.Identifier.Text;

                var implementedInterfaces = baseTypes.Where(t => t.StartsWith("I") && t.Length > 1 && char.IsUpper(t[1])).ToList();

                // Extract Class Attributes (e.g. [ApiController], [Route("api/[controller]")])
                var classAttributes = classDecl.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Select(a => a.Name.ToString())
                    .ToList();

                var routeAttr = classDecl.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .FirstOrDefault(a => a.Name.ToString().Contains("Route"));
                var routePrefix = routeAttr?.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('\"', '@') ?? "";

                // Deterministic Architecture Classification without hardcoded company project names
                bool hasApiControllerAttr = classAttributes.Any(a => a.Contains("ApiController"));
                bool inheritsController = baseType == "Controller" || baseTypes.Any(t => t == "Controller");
                bool inheritsControllerBase = baseType.Contains("ControllerBase") || baseType.Contains("BaseController") || baseTypes.Any(t => t.Contains("ControllerBase"));

                var isWeb = inheritsController || 
                            (!hasApiControllerAttr && className.EndsWith("Controller") && (nsName.Contains("Web") || filePath.Contains("WebClient") || filePath.Contains("Web")));

                var isApi = hasApiControllerAttr || 
                            (inheritsControllerBase && !inheritsController) ||
                            className.EndsWith("BaseController") || 
                            (className.EndsWith("Controller") && (nsName.Contains("Api") || nsName.Contains("Server") || filePath.Contains("API") || filePath.Contains("Server")));

                var isValidator = baseType.Contains("AbstractValidator") || baseTypes.Any(t => t.Contains("AbstractValidator") || t.Contains("IValidator"));
                var isRepo = className.EndsWith("Repository") || baseTypes.Any(t => t.Contains("Repository"));
                var isSvc = (className.EndsWith("Service") || baseTypes.Any(t => t.Contains("Service")) || implementedInterfaces.Any(i => i.EndsWith("Service"))) && !isRepo;

                var analysis = new ClassAnalysis
                {
                    ClassName = className,
                    Namespace = nsName,
                    FilePath = filePath,
                    BaseClass = baseType,
                    ImplementedInterfaces = implementedInterfaces,
                    RoutePrefix = routePrefix,
                    IsWebController = isWeb && !hasApiControllerAttr,
                    IsApiController = isApi && (!isWeb || hasApiControllerAttr),
                    IsService = isSvc && !isWeb && !isApi,
                    IsValidator = isValidator,
                    IsRepository = isRepo
                };

                // Injected Dependencies from Constructor
                var constructors = classDecl.Members.OfType<ConstructorDeclarationSyntax>();
                foreach (var ctor in constructors)
                {
                    foreach (var param in ctor.ParameterList.Parameters)
                    {
                        var paramType = param.Type?.ToString() ?? "";
                        if (paramType.StartsWith("I") && paramType.Length > 1 && char.IsUpper(paramType[1]))
                        {
                            if (!analysis.ConstructorDependencies.Any(d => d.InterfaceName == paramType))
                            {
                                analysis.ConstructorDependencies.Add(new DependencyModel
                                {
                                    InterfaceName = paramType,
                                    ParameterName = param.Identifier.Text,
                                    FieldName = $"_{param.Identifier.Text}"
                                });
                            }
                        }
                    }
                }

                // Public Methods
                var methods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                                              .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)));

                foreach (var method in methods)
                {
                    var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)) || method.ReturnType.ToString().StartsWith("Task");
                    var returnType = method.ReturnType.ToString();

                    // Detect HTTP Attributes & Action Route Templates
                    var methodAttributes = method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Select(a => a.Name.ToString())
                        .ToList();

                    string httpMethod = "GET";
                    string routeTemplate = "";

                    var httpAttr = method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .FirstOrDefault(a => a.Name.ToString().StartsWith("Http", StringComparison.OrdinalIgnoreCase));

                    if (httpAttr != null)
                    {
                        var name = httpAttr.Name.ToString();
                        if (name.Contains("Post", StringComparison.OrdinalIgnoreCase)) httpMethod = "POST";
                        else if (name.Contains("Put", StringComparison.OrdinalIgnoreCase)) httpMethod = "PUT";
                        else if (name.Contains("Delete", StringComparison.OrdinalIgnoreCase)) httpMethod = "DELETE";
                        else if (name.Contains("Patch", StringComparison.OrdinalIgnoreCase)) httpMethod = "PATCH";
                        else if (name.Contains("Get", StringComparison.OrdinalIgnoreCase)) httpMethod = "GET";

                        routeTemplate = httpAttr.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('\"', '@') ?? "";
                    }
                    else if (method.Identifier.Text.StartsWith("Post", StringComparison.OrdinalIgnoreCase) ||
                             method.Identifier.Text.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
                             method.Identifier.Text.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ||
                             method.Identifier.Text.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ||
                             method.Identifier.Text.StartsWith("Update", StringComparison.OrdinalIgnoreCase))
                    {
                        httpMethod = "POST";
                    }

                    var methodModel = new MethodAnalysis
                    {
                        ClassName = analysis.ClassName,
                        Namespace = analysis.Namespace,
                        MethodName = method.Identifier.Text,
                        ReturnType = returnType,
                        IsAsync = isAsync,
                        HttpMethod = httpMethod,
                        RouteTemplate = routeTemplate,
                        Attributes = methodAttributes,
                        Dependencies = new List<DependencyModel>(analysis.ConstructorDependencies)
                    };

                    foreach (var param in method.ParameterList.Parameters)
                    {
                        methodModel.Parameters.Add(new ParameterModel
                        {
                            Name = param.Identifier.Text,
                            Type = param.Type?.ToString() ?? "object",
                            IsNullable = param.Type is NullableTypeSyntax,
                            DefaultValue = param.Default?.Value.ToString()
                        });
                    }

                    var ifStatements = method.DescendantNodes().OfType<IfStatementSyntax>();
                    foreach (var ifStmt in ifStatements)
                    {
                        var condText = ifStmt.Condition.ToString();
                        var branchType = "General";
                        if (condText.Contains("== null") || condText.Contains("!= null") || condText.Contains("!"))
                            branchType = "NullCheck";
                        else if (condText.Contains(">") || condText.Contains("<") || condText.Contains("=="))
                            branchType = "Boundary";

                        methodModel.Branches.Add(new BranchModel
                        {
                            ConditionText = condText,
                            BranchType = branchType,
                            LineNumber = ifStmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                        });
                    }

                    // Detect invoked methods on constructor dependencies (for intelligent mock setup)
                    var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
                    foreach (var inv in invocations)
                    {
                        if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            var exprText = memberAccess.Expression.ToString();
                            var matchedDep = analysis.ConstructorDependencies.FirstOrDefault(d => 
                                d.FieldName == exprText || 
                                d.ParameterName == exprText || 
                                exprText == $"this.{d.FieldName}");

                            if (matchedDep != null)
                            {
                                var invokedMethodName = memberAccess.Name.Identifier.Text;
                                if (!methodModel.InvokedDependencies.Any(id => id.InterfaceName == matchedDep.InterfaceName && id.MethodName == invokedMethodName))
                                {
                                    bool isAsyncInv = inv.Parent is AwaitExpressionSyntax || invokedMethodName.EndsWith("Async");
                                    var argTypes = new List<string>();
                                    foreach (var arg in inv.ArgumentList.Arguments)
                                    {
                                        var argText = arg.Expression.ToString();
                                        var matchedParam = method.ParameterList.Parameters.FirstOrDefault(p => p.Identifier.Text == argText);
                                        if (matchedParam != null && matchedParam.Type != null)
                                        {
                                            argTypes.Add(matchedParam.Type.ToString());
                                        }
                                        else if (arg.Expression is LiteralExpressionSyntax lit)
                                        {
                                            if (lit.IsKind(SyntaxKind.NumericLiteralExpression)) argTypes.Add("int");
                                            else if (lit.IsKind(SyntaxKind.StringLiteralExpression)) argTypes.Add("string");
                                            else if (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.IsKind(SyntaxKind.FalseLiteralExpression)) argTypes.Add("bool");
                                            else argTypes.Add("object");
                                        }
                                        else
                                        {
                                            argTypes.Add("object");
                                        }
                                    }

                                    methodModel.InvokedDependencies.Add(new InvokedDependencyMethod
                                    {
                                        DependencyFieldName = matchedDep.FieldName,
                                        InterfaceName = matchedDep.InterfaceName,
                                        MethodName = invokedMethodName,
                                        IsAsync = isAsyncInv,
                                        ArgumentCount = inv.ArgumentList.Arguments.Count,
                                        ArgumentTypes = argTypes
                                    });
                                }
                            }
                        }
                    }

                    analysis.Methods.Add(methodModel);
                }

                results.Add(analysis);
            }

            return results;
        }

        public List<TestDiagnosticModel> ValidateGeneratedTest(string testCode, string fileName = "GeneratedTests.cs")
        {
            var errors = new List<TestDiagnosticModel>();
            if (string.IsNullOrWhiteSpace(testCode))
                return errors;

            var tree = CSharpSyntaxTree.ParseText(testCode);
            var root = tree.GetRoot();
            var sourceText = tree.GetText();
            var diagnostics = tree.GetDiagnostics();

            foreach (var diag in diagnostics)
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                {
                    var lineSpan = diag.Location.GetLineSpan();
                    int lineIndex = lineSpan.StartLinePosition.Line;
                    int colIndex = lineSpan.StartLinePosition.Character;

                    // Extract enclosing method, constructor, or class from AST
                    var token = root.FindToken(diag.Location.SourceSpan.Start);
                    var methodNode = token.Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    var ctorNode = token.Parent?.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
                    var classNode = token.Parent?.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();

                    string targetMethod = "Class / File Scope";
                    if (methodNode != null)
                    {
                        targetMethod = methodNode.Identifier.Text;
                    }
                    else if (ctorNode != null)
                    {
                        targetMethod = $"{ctorNode.Identifier.Text} (Constructor)";
                    }
                    else if (classNode != null)
                    {
                        targetMethod = $"{classNode.Identifier.Text} (Class Declaration)";
                    }

                    // Extract offending code snippet from source text
                    string snippet = string.Empty;
                    if (lineIndex >= 0 && lineIndex < sourceText.Lines.Count)
                    {
                        snippet = sourceText.Lines[lineIndex].ToString().Trim();
                    }

                    errors.Add(new TestDiagnosticModel
                    {
                        FileName = fileName,
                        MethodName = targetMethod,
                        ErrorId = diag.Id,
                        Message = diag.GetMessage(),
                        LineNumber = lineIndex + 1,
                        Column = colIndex + 1,
                        Severity = "Error",
                        CodeSnippet = snippet
                    });
                }
            }

            return errors;
        }

        public (string RepairedCode, int AttemptsUsed, List<TestDiagnosticModel> RemainingDiagnostics) RepairGeneratedTest(string testCode, string fileName, int maxAttempts = 3)
        {
            var currentCode = testCode;
            int attempt = 0;
            var diags = ValidateGeneratedTest(currentCode, fileName);

            while (diags.Count > 0 && attempt < maxAttempts)
            {
                attempt++;
                bool modified = false;

                // 1. Missing using namespace diagnostics (CS0246)
                if (currentCode.Contains("IActionResult") && !currentCode.Contains("using Microsoft.AspNetCore.Mvc;"))
                {
                    currentCode = "using Microsoft.AspNetCore.Mvc;\n" + currentCode;
                    modified = true;
                }
                if (currentCode.Contains("CancellationToken") && !currentCode.Contains("using System.Threading;"))
                {
                    currentCode = "using System.Threading;\n" + currentCode;
                    modified = true;
                }
                if (currentCode.Contains("JsonSerializer") && !currentCode.Contains("using System.Text.Json;"))
                {
                    currentCode = "using System.Text.Json;\n" + currentCode;
                    modified = true;
                }

                // 2. Missing semicolon (CS1002)
                var semicolonDiags = diags.Where(d => d.ErrorId == "CS1002").ToList();
                if (semicolonDiags.Any())
                {
                    var lines = currentCode.Split('\n').ToList();
                    foreach (var sd in semicolonDiags)
                    {
                        int targetLine = sd.LineNumber - 1;
                        if (targetLine >= 0 && targetLine < lines.Count)
                        {
                            var trimmed = lines[targetLine].TrimEnd('\r');
                            if (!trimmed.EndsWith(";") && !trimmed.EndsWith("{") && !trimmed.EndsWith("}") && !string.IsNullOrWhiteSpace(trimmed))
                            {
                                lines[targetLine] = trimmed + ";\r";
                                modified = true;
                            }
                        }
                    }
                    if (modified)
                    {
                        currentCode = string.Join("\n", lines);
                    }
                }

                if (!modified)
                {
                    break;
                }

                diags = ValidateGeneratedTest(currentCode, fileName);
            }

            return (currentCode, attempt, diags);
        }

        public ExistingTestProjectAnalysis AnalyzeExistingTestProject(string testDirectoryOrFile)
        {
            var result = new ExistingTestProjectAnalysis
            {
                DirectoryPath = testDirectoryOrFile,
                ProjectName = Path.GetFileName(testDirectoryOrFile)
            };

            if (string.IsNullOrWhiteSpace(testDirectoryOrFile))
                return result;

            var testFiles = new List<string>();
            if (File.Exists(testDirectoryOrFile))
            {
                testFiles.Add(testDirectoryOrFile);
            }
            else if (Directory.Exists(testDirectoryOrFile))
            {
                testFiles = Directory.GetFiles(testDirectoryOrFile, "*.cs", SearchOption.AllDirectories)
                                     .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\") && 
                                                 (f.EndsWith("Tests.cs") || f.EndsWith("Test.cs") || f.Contains(@"\Tests\")))
                                     .ToList();
            }

            result.TotalTestFiles = testFiles.Count;
            var testClassesSet = new HashSet<string>();

            foreach (var file in testFiles)
            {
                var testsInFile = AnalyzeExistingTestFile(file);
                result.ExistingTests.AddRange(testsInFile);
                foreach (var t in testsInFile)
                {
                    testClassesSet.Add(t.TestClassName);
                }
            }

            result.TotalTestClasses = testClassesSet.Count;
            result.TotalTestMethods = result.ExistingTests.Count;
            result.CoveredMethodsCount = result.ExistingTests
                .Select(t => $"{t.TargetClassName}.{t.TargetMethodName}")
                .Where(s => !s.StartsWith("."))
                .Distinct()
                .Count();

            return result;
        }

        public List<ExistingTestCaseAnalysis> AnalyzeExistingTestFile(string filePath)
        {
            var tests = new List<ExistingTestCaseAnalysis>();
            if (!File.Exists(filePath)) return tests;

            var code = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDecl in classDeclarations)
            {
                var testClassName = classDecl.Identifier.Text;
                // Inferred target class from Test class name: e.g. PurchaseOrderBaseControllerTests -> PurchaseOrderBaseController
                string inferredTargetClass = testClassName;
                if (testClassName.EndsWith("Tests"))
                    inferredTargetClass = testClassName.Substring(0, testClassName.Length - 5);
                else if (testClassName.EndsWith("Test"))
                    inferredTargetClass = testClassName.Substring(0, testClassName.Length - 4);

                // Detect constructor mocks: e.g. Mock<IPurchaseOrderService>
                var mocks = new List<string>();
                var mockFields = classDecl.DescendantNodes().OfType<FieldDeclarationSyntax>();
                foreach (var f in mockFields)
                {
                    var typeStr = f.Declaration.Type.ToString();
                    if (typeStr.StartsWith("Mock<") && typeStr.EndsWith(">"))
                    {
                        var iface = typeStr.Substring(5, typeStr.Length - 6);
                        mocks.Add(iface);
                    }
                }

                var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
                foreach (var method in methods)
                {
                    string testFramework = "xUnit";
                    bool isTestMethod = false;
                    foreach (var al in method.AttributeLists)
                    {
                        foreach (var a in al.Attributes)
                        {
                            var aName = a.Name.ToString();
                            if (aName.Contains("Fact") || aName.Contains("Theory"))
                            {
                                isTestMethod = true;
                                testFramework = "xUnit";
                                break;
                            }
                            if (aName.Contains("Test") || aName.Contains("TestCase"))
                            {
                                isTestMethod = true;
                                testFramework = "NUnit";
                                break;
                            }
                            if (aName.Contains("TestMethod"))
                            {
                                isTestMethod = true;
                                testFramework = "MSTest";
                                break;
                            }
                        }
                        if (isTestMethod) break;
                    }

                    if (!isTestMethod) continue;

                    var methodName = method.Identifier.Text;
                    var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
                    var lineNum = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                    // 1. AST Call-graph method resolution (preferred over string splitting)
                    string inferredTargetMethod = "";
                    bool fromCallGraph = false;

                    var methodCalls = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
                    foreach (var call in methodCalls)
                    {
                        if (call.Expression is MemberAccessExpressionSyntax ma)
                        {
                            var targetObj = ma.Expression.ToString();
                            if (targetObj.Contains("controller", StringComparison.OrdinalIgnoreCase) || 
                                targetObj.Contains("service", StringComparison.OrdinalIgnoreCase) || 
                                targetObj.Contains("repo", StringComparison.OrdinalIgnoreCase) ||
                                targetObj.Equals("sut", StringComparison.OrdinalIgnoreCase) ||
                                targetObj.Equals("_sut", StringComparison.OrdinalIgnoreCase))
                            {
                                inferredTargetMethod = ma.Name.Identifier.Text;
                                fromCallGraph = true;
                                break;
                            }
                        }
                    }

                    // 2. Fallback to naming pattern: e.g. GetAllPurchaseOrderAsync_ReturnsOk_WithData -> GetAllPurchaseOrderAsync
                    if (string.IsNullOrEmpty(inferredTargetMethod))
                    {
                        inferredTargetMethod = methodName.Contains("_") ? methodName.Split('_')[0] : methodName;
                    }

                    // Look for assertions: Should(), Assert.Equal, Verify, etc.
                    var assertions = new List<string>();
                    foreach (var call in methodCalls)
                    {
                        var callStr = call.ToString();
                        if (callStr.Contains("Should()") || callStr.Contains("Assert.") || callStr.Contains(".Verify(") ||
                            callStr.Contains("CollectionAssert.") || callStr.Contains("StringAssert."))
                        {
                            var truncated = callStr.Length > 80 ? callStr.Substring(0, 77) + "..." : callStr;
                            assertions.Add(truncated);
                        }
                    }

                    tests.Add(new ExistingTestCaseAnalysis
                    {
                        TestClassName = testClassName,
                        TestMethodName = methodName,
                        TargetClassName = inferredTargetClass,
                        TargetMethodName = inferredTargetMethod,
                        FilePath = filePath,
                        LineNumber = lineNum,
                        IsAsync = isAsync,
                        Category = "ExistingTest",
                        TestFramework = testFramework,
                        IsInferredFromCallGraph = fromCallGraph,
                        InferredAssertions = assertions,
                        MockedDependencies = mocks
                    });
                }
            }

            return tests;
        }

        public List<WebToApiMapping> AnalyzeWebToApiCommunication(List<ClassAnalysis> webClasses, List<ClassAnalysis> apiClasses)
        {
            var mappings = new List<WebToApiMapping>();
            var apiControllers = apiClasses.Where(c => c.IsApiController || c.ClassName.EndsWith("Controller")).ToList();

            foreach (var webClass in webClasses.Where(c => c.IsWebController || c.IsService || c.ClassName.EndsWith("Controller")))
            {
                foreach (var webMethod in webClass.Methods)
                {
                    // Scan dependencies for IHttpClientFactory, HttpClient, or API proxies
                    var hasHttpDep = webMethod.Dependencies.Any(d => d.InterfaceName.Contains("Http") || d.InterfaceName.Contains("Service") || d.InterfaceName.Contains("Client"));

                    // Look for matching API controller / method by name similarity or domain keywords
                    string webPrefix = webClass.ClassName.Replace("Controller", "").Replace("Service", "");
                    var matchingApi = apiControllers.FirstOrDefault(a => 
                        a.ClassName.StartsWith(webPrefix, StringComparison.OrdinalIgnoreCase) ||
                        a.ClassName.Contains(webPrefix, StringComparison.OrdinalIgnoreCase));

                    if (matchingApi != null)
                    {
                        var matchingMethod = matchingApi.Methods.FirstOrDefault(m => 
                            string.Equals(m.MethodName, webMethod.MethodName, StringComparison.OrdinalIgnoreCase) ||
                            m.MethodName.StartsWith(webMethod.MethodName, StringComparison.OrdinalIgnoreCase) ||
                            webMethod.MethodName.StartsWith(m.MethodName, StringComparison.OrdinalIgnoreCase));

                        string targetMethodName = matchingMethod?.MethodName ?? webMethod.MethodName;
                        string inferredEndpoint = $"/api/v1/{matchingApi.ClassName.Replace("Controller", "").Replace("BaseController", "")}/{targetMethodName.Replace("Async", "")}";

                        mappings.Add(new WebToApiMapping
                        {
                            WebControllerName = webClass.ClassName,
                            WebActionMethod = webMethod.MethodName,
                            ApiEndpointUrl = inferredEndpoint,
                            TargetApiController = matchingApi.ClassName,
                            TargetApiMethod = targetMethodName,
                            HttpMethod = webMethod.MethodName.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ? "GET" : "POST",
                            CommunicationPattern = "HTTP REST API (HttpClient)",
                            Description = $"Web '{webClass.ClassName}.{webMethod.MethodName}' routes to Core Server '{matchingApi.ClassName}.{targetMethodName}' via {inferredEndpoint}"
                        });
                    }
                }
            }

            return mappings;
        }

        public List<TestCaseReviewItem> SynthesizeReviewTestCases(
            List<ClassAnalysis> classes, 
            ExistingTestProjectAnalysis? existingTests = null,
            List<BusinessRuleModel>? brdRules = null,
            CustomScenarioFile? customCases = null)
        {
            var reviewCases = new List<TestCaseReviewItem>();
            int tcIndex = 1;

            var existingTestMap = new Dictionary<string, List<ExistingTestCaseAnalysis>>(StringComparer.OrdinalIgnoreCase);
            if (existingTests != null)
            {
                foreach (var et in existingTests.ExistingTests)
                {
                    var key = $"{et.TargetClassName}.{et.TargetMethodName}";
                    if (!existingTestMap.ContainsKey(key))
                        existingTestMap[key] = new List<ExistingTestCaseAnalysis>();
                    existingTestMap[key].Add(et);
                }
            }

            foreach (var cls in classes)
            {
                string role = cls.IsWebController ? "Web MVC" : (cls.IsApiController ? "Core API" : "Domain Service");

                foreach (var method in cls.Methods)
                {
                    if (method.MethodName.StartsWith("get_") || method.MethodName.StartsWith("set_"))
                        continue;

                    var methodKey = $"{cls.ClassName}.{method.MethodName}";
                    bool hasExistingTest = existingTestMap.ContainsKey(methodKey);

                    // If existing tests exist for this method, add them as ExistingCovered
                    if (hasExistingTest)
                    {
                        var foundTests = existingTestMap[methodKey];
                        foreach (var et in foundTests)
                        {
                            reviewCases.Add(new TestCaseReviewItem
                            {
                                Id = $"TC-EXT-{tcIndex++:D3}",
                                TestName = et.TestMethodName,
                                TargetProjectRole = role,
                                ProjectName = cls.Namespace,
                                TargetClass = cls.ClassName,
                                TargetMethod = method.MethodName,
                                Category = "ExistingCovered",
                                TestInput = "Pre-configured in test suite",
                                ExpectedResult = et.InferredAssertions.Any() ? string.Join("; ", et.InferredAssertions) : "Asserts successful response",
                                Source = "Existing Test Suite",
                                Priority = "High",
                                Status = "Existing",
                                IsApproved = true,
                                IsExisting = true,
                                ExistingTestFile = et.FilePath,
                                ExistingTestMethod = et.TestMethodName
                            });
                        }
                    }

                    // 1. Happy Path Case (Always suggested if not already fully tested)
                    var happyName = cls.IsWebController || cls.IsApiController
                        ? $"{method.MethodName}_WhenCalled_ReturnsValidActionResult"
                        : $"{method.MethodName}_WithValidInputs_ExecutesSuccessfully";

                    if (!hasExistingTest || !existingTestMap[methodKey].Any(t => t.TestMethodName.Contains("Valid") || t.TestMethodName.Contains("Ok")))
                    {
                        reviewCases.Add(new TestCaseReviewItem
                        {
                            Id = $"TC-SUG-{tcIndex++:D3}",
                            TestName = happyName,
                            TargetProjectRole = role,
                            ProjectName = cls.Namespace,
                            TargetClass = cls.ClassName,
                            TargetMethod = method.MethodName,
                            Category = "HappyPath",
                            TestInput = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: valid")),
                            ExpectedResult = (cls.IsWebController || cls.IsApiController) ? "Returns 200 OK / Valid ActionResult" : "Executes without throwing exceptions",
                            Source = "Roslyn Code Analysis",
                            Priority = "High",
                            Status = hasExistingTest ? "Updated" : "Suggested",
                            IsApproved = true
                        });
                    }

                    // 2. Defensive Null Validation (If method has reference parameters)
                    var refParam = method.Parameters.FirstOrDefault(p => p.Type == "string" || p.Type.EndsWith("Entity") || p.Type.EndsWith("Model") || p.Type.EndsWith("Dto") || !p.Type.All(char.IsLower));
                    if (refParam != null)
                    {
                        var nullName = $"{method.MethodName}_When_{refParam.Name}IsNull_HandlesDefensively";
                        if (!hasExistingTest || !existingTestMap[methodKey].Any(t => t.TestMethodName.Contains("Null")))
                        {
                            reviewCases.Add(new TestCaseReviewItem
                            {
                                Id = $"TC-NUL-{tcIndex++:D3}",
                                TestName = nullName,
                                TargetProjectRole = role,
                                ProjectName = cls.Namespace,
                                TargetClass = cls.ClassName,
                                TargetMethod = method.MethodName,
                                Category = "NullValidation",
                                TestInput = $"{refParam.Name} = null",
                                ExpectedResult = "Handles null gracefully without NullReferenceException (BadRequest or error code)",
                                Source = "Roslyn Code Analysis",
                                Priority = "Medium",
                                Status = "Suggested",
                                IsApproved = true
                            });
                        }
                    }

                    // 3. Boundary / Conditional Branch Cases
                    foreach (var branch in method.Branches.Take(2))
                    {
                        var branchName = $"{method.MethodName}_WhenCondition_{branch.BranchType}_EvaluatesExpectedFlow";
                        reviewCases.Add(new TestCaseReviewItem
                        {
                            Id = $"TC-BND-{tcIndex++:D3}",
                            TestName = branchName,
                            TargetProjectRole = role,
                            ProjectName = cls.Namespace,
                            TargetClass = cls.ClassName,
                            TargetMethod = method.MethodName,
                            Category = branch.BranchType == "NullCheck" ? "NullValidation" : "Boundary",
                            TestInput = $"Condition: {branch.ConditionText}",
                            ExpectedResult = "Branch executed and verified at line " + branch.LineNumber,
                            Source = "Roslyn Code Analysis",
                            Priority = "Medium",
                            Status = "Suggested",
                            IsApproved = true
                        });
                    }

                    // 4. BRD Rules (If matching requirements exist)
                    if (brdRules != null)
                    {
                        foreach (var rule in brdRules.Where(r => r.Condition.Contains(cls.ClassName) || r.ExpectedOutcome.Contains(method.MethodName)))
                        {
                            reviewCases.Add(new TestCaseReviewItem
                            {
                                Id = $"TC-BRD-{tcIndex++:D3}",
                                TestName = $"{method.MethodName}_Rule_{rule.RuleId}_EnforcesBusinessLogic",
                                TargetProjectRole = role,
                                ProjectName = cls.Namespace,
                                TargetClass = cls.ClassName,
                                TargetMethod = method.MethodName,
                                Category = "BRD",
                                TestInput = rule.Condition,
                                ExpectedResult = rule.ExpectedOutcome,
                                Source = $"BRD Document ({rule.SourceDocument})",
                                Priority = "High",
                                Status = "Suggested",
                                IsApproved = true
                            });
                        }
                    }

                    // 5. Custom Scenarios (If user specified custom cases)
                    if (customCases != null && (string.Equals(customCases.TargetClass, cls.ClassName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(customCases.TargetClass)))
                    {
                        foreach (var cc in customCases.CustomTestCases)
                        {
                            reviewCases.Add(new TestCaseReviewItem
                            {
                                Id = $"TC-CUS-{tcIndex++:D3}",
                                TestName = cc.Name,
                                TargetProjectRole = role,
                                ProjectName = cls.Namespace,
                                TargetClass = cls.ClassName,
                                TargetMethod = method.MethodName,
                                Category = "CustomUser",
                                TestInput = string.Join(", ", cc.Given.Select(kv => $"{kv.Key}: {kv.Value}")),
                                ExpectedResult = string.Join(", ", cc.Expected.Select(kv => $"{kv.Key}: {kv.Value}")),
                                Source = "User Custom Scenario",
                                Priority = "High",
                                Status = "Approved",
                                IsApproved = true
                            });
                        }
                    }
                }
            }

            return reviewCases;
        }
    }
}

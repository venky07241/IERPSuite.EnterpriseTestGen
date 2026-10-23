using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.Business
{
    public class BusinessDocParser
    {
        public List<BusinessRuleModel> ParseDocumentText(string docContent, string documentName = "BRD")
        {
            var rules = new List<BusinessRuleModel>();
            if (string.IsNullOrWhiteSpace(docContent))
                return rules;

            // Check if input is OpenAPI JSON
            if (docContent.TrimStart().StartsWith("{") && (docContent.Contains("\"openapi\"") || docContent.Contains("\"swagger\"")))
            {
                var (_, _, oasRules) = ParseOpenApiSpec(docContent, documentName);
                if (oasRules.Count > 0)
                    return oasRules;
            }

            // Check if input is Markdown with Gherkin / Acceptance Criteria
            if (docContent.Contains("Feature:") || docContent.Contains("Scenario:") || docContent.Contains("As a ") || docContent.Contains("Given "))
            {
                var (_, gherkinRules) = ParseMarkdownRequirements(docContent, documentName);
                if (gherkinRules.Count > 0)
                    return gherkinRules;
            }

            var lines = docContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int ruleCounter = 1;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (Regex.IsMatch(trimmed, @"\b(if|when|must|should|verify|validation|require|approval|cannot|forbidden|boundary)\b", RegexOptions.IgnoreCase))
                {
                    rules.Add(new BusinessRuleModel
                    {
                        RuleId = $"BRD-RULE-{ruleCounter++:D3}",
                        Condition = trimmed,
                        ExpectedOutcome = "Enforce verified business expectation",
                        SourceDocument = documentName
                    });
                }
            }

            return rules;
        }

        public (OpenApiSpecificationModel Spec, List<StructuredRequirement> Requirements, List<BusinessRuleModel> Rules) ParseOpenApiSpec(string jsonContent, string documentName = "openapi.json")
        {
            var spec = new OpenApiSpecificationModel { Title = "OpenAPI Specification", Version = "3.0" };
            var requirements = new List<StructuredRequirement>();
            var rules = new List<BusinessRuleModel>();

            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("info", out var info))
                {
                    if (info.TryGetProperty("title", out var title)) spec.Title = title.GetString() ?? spec.Title;
                    if (info.TryGetProperty("version", out var ver)) spec.Version = ver.GetString() ?? spec.Version;
                }

                if (root.TryGetProperty("paths", out var paths))
                {
                    int reqIndex = 1;
                    foreach (var pathProp in paths.EnumerateObject())
                    {
                        string pathUrl = pathProp.Name;
                        foreach (var methodProp in pathProp.Value.EnumerateObject())
                        {
                            string httpMethod = methodProp.Name.ToUpperInvariant();
                            if (httpMethod != "GET" && httpMethod != "POST" && httpMethod != "PUT" && 
                                httpMethod != "DELETE" && httpMethod != "PATCH")
                                continue;

                            var operation = methodProp.Value;
                            string operationId = operation.TryGetProperty("operationId", out var opId) ? (opId.GetString() ?? "") : "";
                            string summary = operation.TryGetProperty("summary", out var sum) ? (sum.GetString() ?? "") : "";
                            string description = operation.TryGetProperty("description", out var desc) ? (desc.GetString() ?? "") : "";

                            var endpointInfo = new OpenApiEndpointInfo
                            {
                                Path = pathUrl,
                                HttpMethod = httpMethod,
                                OperationId = operationId,
                                Summary = summary,
                                Description = description
                            };

                            var statusCodes = new List<int>();
                            if (operation.TryGetProperty("responses", out var responses))
                            {
                                foreach (var respProp in responses.EnumerateObject())
                                {
                                    if (int.TryParse(respProp.Name, out int code))
                                        statusCodes.Add(code);
                                }
                            }
                            endpointInfo.ExpectedStatusCodes = statusCodes;
                            spec.Endpoints.Add(endpointInfo);

                            string ruleId = $"OAS-{reqIndex++:D3}";
                            string targetMethodName = !string.IsNullOrEmpty(operationId) ? operationId : $"{httpMethod}_{pathUrl.Trim('/').Replace('/', '_')}";

                            var req = new StructuredRequirement
                            {
                                RequirementId = ruleId,
                                Title = $"[{httpMethod}] {pathUrl}" + (!string.IsNullOrEmpty(summary) ? $" - {summary}" : ""),
                                Description = !string.IsNullOrEmpty(description) ? description : summary,
                                SourceType = "OpenApi",
                                SourceDocument = documentName,
                                Category = httpMethod == "GET" ? "Functional" : "Validation",
                                TargetEndpoint = pathUrl,
                                TargetMethod = targetMethodName,
                                HttpMethod = httpMethod,
                                Status = "Verified",
                                AcceptanceCriteria = statusCodes.Select(c => $"Must respond with HTTP status {c}").ToList()
                            };
                            requirements.Add(req);

                            rules.Add(new BusinessRuleModel
                            {
                                RuleId = ruleId,
                                Condition = $"HTTP {httpMethod} request to {pathUrl} with valid contract",
                                ExpectedOutcome = statusCodes.Any() ? $"Returns expected HTTP status [{string.Join(", ", statusCodes)}]" : "Returns successful response",
                                SourceDocument = documentName
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                rules.Add(new BusinessRuleModel
                {
                    RuleId = "OAS-ERR-001",
                    Condition = "Invalid OpenAPI JSON format",
                    ExpectedOutcome = ex.Message,
                    SourceDocument = documentName
                });
            }

            return (spec, requirements, rules);
        }

        public (List<StructuredRequirement> Requirements, List<BusinessRuleModel> Rules) ParseMarkdownRequirements(string markdownContent, string documentName = "requirements.md")
        {
            var requirements = new List<StructuredRequirement>();
            var rules = new List<BusinessRuleModel>();
            if (string.IsNullOrWhiteSpace(markdownContent))
                return (requirements, rules);

            var lines = markdownContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int reqCounter = 1;

            string currentScenario = "";
            var currentCriteria = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Scenario / User Story header
                if (line.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("### Scenario:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("## Scenario:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentScenario))
                    {
                        AddRequirementFromScenario(requirements, rules, ref reqCounter, currentScenario, currentCriteria, documentName);
                        currentCriteria.Clear();
                    }

                    currentScenario = Regex.Replace(line, @"^(#+\s*)?Scenario:\s*", "", RegexOptions.IgnoreCase).Trim();
                }
                else if (line.StartsWith("Given ", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("Then ", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("And ", StringComparison.OrdinalIgnoreCase))
                {
                    currentCriteria.Add(line);
                }
                else if (line.StartsWith("As a ", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("I want ", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("So that ", StringComparison.OrdinalIgnoreCase))
                {
                    currentCriteria.Add(line);
                    if (string.IsNullOrEmpty(currentScenario))
                    {
                        currentScenario = "User Story Acceptance Criteria";
                    }
                }
                else if (line.StartsWith("- [ ]") || line.StartsWith("- [x]") || line.StartsWith("* ") || line.StartsWith("- "))
                {
                    var item = line.TrimStart('-', '*', '[', ']', 'x', ' ').Trim();
                    if (Regex.IsMatch(item, @"\b(if|when|must|should|verify|validation|require|cannot)\b", RegexOptions.IgnoreCase))
                    {
                        currentCriteria.Add(item);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentScenario) || currentCriteria.Count > 0)
            {
                AddRequirementFromScenario(requirements, rules, ref reqCounter, string.IsNullOrEmpty(currentScenario) ? "General Acceptance Criteria" : currentScenario, currentCriteria, documentName);
            }

            return (requirements, rules);
        }

        private static void AddRequirementFromScenario(
            List<StructuredRequirement> requirements, 
            List<BusinessRuleModel> rules, 
            ref int counter, 
            string scenarioTitle, 
            List<string> criteria, 
            string documentName)
        {
            string id = $"REQ-{counter++:D3}";
            var req = new StructuredRequirement
            {
                RequirementId = id,
                Title = scenarioTitle,
                Description = string.Join(Environment.NewLine, criteria),
                SourceType = "MarkdownGherkin",
                SourceDocument = documentName,
                Category = criteria.Any(c => c.Contains("error", StringComparison.OrdinalIgnoreCase) || c.Contains("throw", StringComparison.OrdinalIgnoreCase) || c.Contains("invalid", StringComparison.OrdinalIgnoreCase)) ? "ErrorHandling" : "Functional",
                Status = "Verified",
                AcceptanceCriteria = new List<string>(criteria)
            };
            requirements.Add(req);

            rules.Add(new BusinessRuleModel
            {
                RuleId = id,
                Condition = scenarioTitle,
                ExpectedOutcome = criteria.FirstOrDefault(c => c.StartsWith("Then ", StringComparison.OrdinalIgnoreCase)) ?? "Verified according to acceptance criteria",
                SourceDocument = documentName
            });
        }

        public List<BusinessRuleModel> ParseStream(Stream stream, string documentName = "BRD")
        {
            var ext = Path.GetExtension(documentName).ToLowerInvariant();

            // Unsupported binary formats: Reject with explicit diagnostic
            if (ext == ".pdf" || ext == ".xlsx" || ext == ".xls" || ext == ".bin" || ext == ".zip" || ext == ".exe" || ext == ".dll")
            {
                return new List<BusinessRuleModel>
                {
                    new BusinessRuleModel
                    {
                        RuleId = "DOC-UNSUPPORTED-001",
                        Condition = $"Unsupported binary file format '{ext}' for '{documentName}'",
                        ExpectedOutcome = "Requirements must be provided in .docx, .json (OpenAPI/Swagger), .md (Gherkin), .yaml, or .txt format. Binary files without text extraction cannot be processed as plain text.",
                        SourceDocument = documentName
                    }
                };
            }

            // Safe DOCX Extraction: word/document.xml inside zip container
            if (ext == ".docx")
            {
                try
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                    var entry = archive.GetEntry("word/document.xml");
                    if (entry != null)
                    {
                        using var entryStream = entry.Open();
                        var xdoc = XDocument.Load(entryStream);
                        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        var textNodes = xdoc.Descendants(w + "t").Select(t => t.Value);
                        var extractedText = string.Join(" ", textNodes);
                        return ParseDocumentText(extractedText, documentName);
                    }
                }
                catch (Exception ex)
                {
                    return new List<BusinessRuleModel>
                    {
                        new BusinessRuleModel
                        {
                            RuleId = "DOC-ERR-002",
                            Condition = $"Failed extracting text from DOCX archive '{documentName}'",
                            ExpectedOutcome = ex.Message,
                            SourceDocument = documentName
                        }
                    };
                }
            }

            // Check if raw stream has binary header (e.g. PDF magic bytes '%PDF')
            stream.Position = 0;
            byte[] header = new byte[Math.Min(4, (int)stream.Length)];
            int bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead >= 4 && header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F')
            {
                return new List<BusinessRuleModel>
                {
                    new BusinessRuleModel
                    {
                        RuleId = "DOC-UNSUPPORTED-002",
                        Condition = $"PDF binary stream detected in '{documentName}'",
                        ExpectedOutcome = "Direct PDF parsing is not supported. Please convert requirements to Markdown, Word (.docx), or OpenAPI JSON.",
                        SourceDocument = documentName
                    }
                };
            }

            // Normal text reading
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = reader.ReadToEnd();
            return ParseDocumentText(text, documentName);
        }

        public List<BusinessRuleModel> ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<BusinessRuleModel>();

            using var stream = File.OpenRead(filePath);
            return ParseStream(stream, Path.GetFileName(filePath));
        }
    }
}

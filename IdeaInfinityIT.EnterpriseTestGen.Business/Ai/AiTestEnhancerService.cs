using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.Business.Ai
{
    public class AiTestEnhancerService : IAiTestEnhancerService
    {
        private readonly HttpClient _httpClient;

        public AiTestEnhancerService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<AiEnhancementResult> EnhanceTestsAsync(
            ClassAnalysis classInfo,
            string originalTestCode,
            List<BusinessRuleModel>? businessRules,
            AiSettingsModel? settings = null)
        {
            settings ??= new AiSettingsModel();

            // If user has chosen an external LLM and provided endpoint/key, try invoking it
            if (settings.Provider != "LocalHeuristic" && !string.IsNullOrWhiteSpace(settings.EndpointUrl))
            {
                try
                {
                    var externalResult = await CallExternalLlmAsync(classInfo, originalTestCode, businessRules, settings);
                    if (externalResult != null && externalResult.Success)
                    {
                        return externalResult;
                    }
                }
                catch
                {
                    // Fall back to local semantic heuristic AI engine seamlessly
                }
            }

            // Built-in Enterprise Heuristic AI Engine
            return GenerateHeuristicEnhancement(classInfo, originalTestCode, businessRules);
        }

        public Task<List<BusinessTraceabilityItem>> AnalyzeTraceabilityAsync(
            ClassAnalysis classInfo,
            List<BusinessRuleModel>? businessRules,
            AiSettingsModel? settings = null)
        {
            var list = new List<BusinessTraceabilityItem>();
            businessRules ??= new List<BusinessRuleModel>();

            int ruleCounter = 1;
            foreach (var rule in businessRules)
            {
                // Match rule with target method
                var matchedMethod = classInfo.Methods.FirstOrDefault(m =>
                    rule.Condition.IndexOf(m.MethodName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rule.ExpectedOutcome.IndexOf(m.MethodName, StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? classInfo.Methods.FirstOrDefault();

                var methodName = matchedMethod?.MethodName ?? "GeneralValidation";

                list.Add(new BusinessTraceabilityItem
                {
                    RuleId = !string.IsNullOrWhiteSpace(rule.RuleId) ? rule.RuleId : $"BRD-R{ruleCounter:D3}",
                    RequirementText = $"{rule.Condition} => {rule.ExpectedOutcome}",
                    TargetMethod = methodName,
                    GeneratedTestName = $"{methodName}_When_{SanitizeName(rule.Condition)}_Should_{SanitizeName(rule.ExpectedOutcome)}",
                    TraceabilityState = "TestSuggested",
                    CoverageStatus = "Requires Review",
                    IsVerified = false,
                    AiAnalysis = $"Requirement mapped to method '{methodName}'. Test suggested; requires compilation, execution, and coverage evidence before being marked covered."
                });

                ruleCounter++;
            }

            // Also check methods that might not have explicit BRD clauses and infer AI boundary checks
            foreach (var method in classInfo.Methods)
            {
                if (method.MethodName.StartsWith("get_") || method.MethodName.StartsWith("set_")) continue;

                if (!list.Any(t => t.TargetMethod == method.MethodName))
                {
                    list.Add(new BusinessTraceabilityItem
                    {
                        RuleId = $"AI-AUTO-{ruleCounter:D3}",
                        RequirementText = $"Method '{method.MethodName}' should validate non-null arguments and return valid {method.ReturnType}.",
                        TargetMethod = method.MethodName,
                        GeneratedTestName = $"{method.MethodName}_GuardClause_NullOrBoundaryInput_ShouldThrowOrReturnDefault",
                        TraceabilityState = "TestSuggested",
                        CoverageStatus = "Unverified",
                        IsVerified = false,
                        AiAnalysis = "Inferred defensive boundary check: requires test execution and assertion verification."
                    });
                    ruleCounter++;
                }
            }

            return Task.FromResult(list);
        }

        private AiEnhancementResult GenerateHeuristicEnhancement(
            ClassAnalysis classInfo,
            string originalTestCode,
            List<BusinessRuleModel>? businessRules)
        {
            var result = new AiEnhancementResult
            {
                Success = true,
                ProviderUsed = "Enterprise Semantic Heuristic AI Engine (Offline / Local)",
                Summary = $"AI analyzed {classInfo.Methods.Count} methods and {classInfo.ConstructorDependencies.Count} injected dependencies. Synthesized defensive boundary tests, null guard validations, and business rule edge cases.",
                InferredEdgeCases = new List<string>(),
                SuggestedAssertions = new List<string>()
            };

            var sbAiTests = new StringBuilder();
            sbAiTests.AppendLine();
            sbAiTests.AppendLine("        // =========================================================================");
            sbAiTests.AppendLine("        // [AI-SYNTHESIZED ENTERPRISE EDGE-CASES & DEFENSIVE TESTS]");
            sbAiTests.AppendLine("        // Inferred by EnterpriseTestGen AI Rule & Semantic Reasoning Engine");
            sbAiTests.AppendLine("        // =========================================================================");

            bool isController = classInfo.IsWebController || classInfo.IsApiController || classInfo.ClassName.EndsWith("Controller");
            string sutVar = isController ? "_controller" : "_service";

            foreach (var method in classInfo.Methods)
            {
                if (method.MethodName.StartsWith("get_") || method.MethodName.StartsWith("set_")) continue;

                // 1. Parameter Null Guard Edge Case
                var refParams = method.Parameters.Where(p => 
                    p.Type == "string" || 
                    p.Type.EndsWith("Entity") || 
                    p.Type.EndsWith("Model") || 
                    p.Type.EndsWith("Dto") || 
                    p.Type.Contains("List") ||
                    !p.Type.All(char.IsLower)).ToList();

                foreach (var p in refParams)
                {
                    var edgeCaseDesc = $"Edge Case: Verify '{method.MethodName}' resiliency when '{p.Name}' is passed as null.";
                    result.InferredEdgeCases.Add(edgeCaseDesc);
                    result.SuggestedAssertions.Add($"Assert throw or handle gracefully when {p.Name} == null");

                    sbAiTests.AppendLine();
                    sbAiTests.AppendLine("        [Fact]");
                    sbAiTests.AppendLine($"        [Trait(\"Category\", \"AI_Synthesized_EdgeCase\")]");
                    sbAiTests.AppendLine($"        public async Task {method.MethodName}_When_{p.Name}IsNull_ShouldHandleGracefullyOrThrow()");
                    sbAiTests.AppendLine("        {");
                    sbAiTests.AppendLine("            // Arrange - AI Boundary Injection");
                    foreach (var param in method.Parameters)
                    {
                        if (param.Name == p.Name)
                        {
                            sbAiTests.AppendLine($"            {param.Type} {param.Name} = null!;");
                        }
                        else
                        {
                            sbAiTests.AppendLine($"            var {param.Name} = {GetDefaultValueForType(param.Type)};");
                        }
                    }

                    sbAiTests.AppendLine();
                    sbAiTests.AppendLine("            // Act & Assert - AI Robustness Verification");
                    var callArgs = string.Join(", ", method.Parameters.Select(pr => pr.Name));
                    if (method.IsAsync)
                    {
                        sbAiTests.AppendLine($"            Func<Task> act = async () => await {sutVar}.{method.MethodName}({callArgs});");
                        sbAiTests.AppendLine("            // Act should either complete gracefully or throw known ArgumentNullException");
                        sbAiTests.AppendLine("            await act.Should().NotThrowAsync<NullReferenceException>();");
                    }
                    else
                    {
                        sbAiTests.AppendLine($"            Action act = () => {sutVar}.{method.MethodName}({callArgs});");
                        sbAiTests.AppendLine("            act.Should().NotThrow<NullReferenceException>();");
                    }
                    sbAiTests.AppendLine("        }");
                }

                // 2. Numerical / ID Boundary Edge Case
                var idParams = method.Parameters.Where(p => 
                    p.Type == "int" || p.Type == "long" || p.Name.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                foreach (var idParam in idParams)
                {
                    var edgeCaseDesc = $"Boundary Case: Verify '{method.MethodName}' when identifier '{idParam.Name}' is zero or negative.";
                    result.InferredEdgeCases.Add(edgeCaseDesc);
                    result.SuggestedAssertions.Add($"Verify graceful handling for non-existent / negative identifier: {idParam.Name} = -1");

                    sbAiTests.AppendLine();
                    sbAiTests.AppendLine("        [Fact]");
                    sbAiTests.AppendLine($"        [Trait(\"Category\", \"AI_Synthesized_Boundary\")]");
                    sbAiTests.AppendLine($"        public async Task {method.MethodName}_When_{idParam.Name}IsNegative_ShouldReturnDefaultOrEmpty()");
                    sbAiTests.AppendLine("        {");
                    sbAiTests.AppendLine("            // Arrange - Boundary Value: -1");
                    foreach (var param in method.Parameters)
                    {
                        if (param.Name == idParam.Name)
                        {
                            sbAiTests.AppendLine($"            {param.Type} {param.Name} = -1;");
                        }
                        else
                        {
                            sbAiTests.AppendLine($"            var {param.Name} = {GetDefaultValueForType(param.Type)};");
                        }
                    }

                    sbAiTests.AppendLine();
                    sbAiTests.AppendLine("            // Act");
                    var callArgs = string.Join(", ", method.Parameters.Select(pr => pr.Name));
                    if (method.IsAsync)
                    {
                        sbAiTests.AppendLine($"            var result = await {sutVar}.{method.MethodName}({callArgs});");
                    }
                    else
                    {
                        sbAiTests.AppendLine($"            var result = {sutVar}.{method.MethodName}({callArgs});");
                    }
                    sbAiTests.AppendLine();
                    sbAiTests.AppendLine("            // Assert - System should handle negative key gracefully without crashing");
                    sbAiTests.AppendLine("            result.Should().NotBeNull();");
                    sbAiTests.AppendLine("        }");
                }
            }

            // Inject the AI test methods into the existing test class right before the closing brace
            var lastBraceIndex = originalTestCode.LastIndexOf('}');
            if (lastBraceIndex > 0)
            {
                var secondLastBraceIndex = originalTestCode.LastIndexOf('}', lastBraceIndex - 1);
                if (secondLastBraceIndex > 0)
                {
                    result.EnhancedTestCode = originalTestCode.Insert(secondLastBraceIndex, sbAiTests.ToString());
                }
                else
                {
                    result.EnhancedTestCode = originalTestCode.Insert(lastBraceIndex, sbAiTests.ToString());
                }
            }
            else
            {
                result.EnhancedTestCode = originalTestCode + sbAiTests.ToString();
            }

            return result;
        }

        private async Task<AiEnhancementResult?> CallExternalLlmAsync(
            ClassAnalysis classInfo,
            string originalTestCode,
            List<BusinessRuleModel>? businessRules,
            AiSettingsModel settings)
        {
            // Build LLM prompt
            var systemPrompt = "You are an elite enterprise C# / .NET 8 Principal Test Architect. Enhance the provided xUnit + Moq unit test class with enterprise-grade edge cases, null arguments, boundary value assertions, and business rule verifications. Return ONLY valid C# code.";
            var userPrompt = $@"Target Class: {classInfo.ClassName} (Namespace: {classInfo.Namespace})
Methods: {string.Join(", ", classInfo.Methods.Select(m => m.MethodName))}
Injected Dependencies: {string.Join(", ", classInfo.ConstructorDependencies.Select(d => d.InterfaceName))}
Business Rules: {(businessRules != null ? string.Join("; ", businessRules.Select(r => $"{r.Condition} => {r.ExpectedOutcome}")) : "None")}

Existing Test Code:
```csharp
{originalTestCode}
```

Instructions: Add defensive test cases covering:
1. Null arguments and guard clauses
2. Negative IDs and zero values
3. Business rule outcome validations
Add test methods inside the existing class. Keep xUnit, Moq, and FluentAssertions syntax.";

            // Format request based on provider
            var requestUrl = settings.EndpointUrl;
            var jsonPayload = "";

            if (settings.Provider == "Ollama")
            {
                if (!requestUrl.EndsWith("/api/generate")) requestUrl = requestUrl.TrimEnd('/') + "/api/generate";
                var reqObj = new
                {
                    model = settings.ModelName,
                    prompt = $"{systemPrompt}\n\n{userPrompt}",
                    stream = false
                };
                jsonPayload = JsonSerializer.Serialize(reqObj);
            }
            else
            {
                // OpenAI / Azure OpenAI / Gemini compatible completions
                if (!requestUrl.EndsWith("/chat/completions")) requestUrl = requestUrl.TrimEnd('/') + "/chat/completions";
                var reqObj = new
                {
                    model = settings.ModelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = settings.Temperature
                };
                jsonPayload = JsonSerializer.Serialize(reqObj);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            }

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var respString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respString);
            string aiResponseText = "";

            if (settings.Provider == "Ollama")
            {
                if (doc.RootElement.TryGetProperty("response", out var respProp))
                {
                    aiResponseText = respProp.GetString() ?? "";
                }
            }
            else
            {
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    aiResponseText = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
            }

            if (string.IsNullOrWhiteSpace(aiResponseText))
            {
                return null;
            }

            // Extract code block if markdown fenced
            var cleanCode = aiResponseText;
            if (aiResponseText.Contains("```csharp"))
            {
                var start = aiResponseText.IndexOf("```csharp") + 9;
                var end = aiResponseText.IndexOf("```", start);
                if (end > start) cleanCode = aiResponseText.Substring(start, end - start).Trim();
            }
            else if (aiResponseText.Contains("```"))
            {
                var start = aiResponseText.IndexOf("```") + 3;
                var end = aiResponseText.IndexOf("```", start);
                if (end > start) cleanCode = aiResponseText.Substring(start, end - start).Trim();
            }

            return new AiEnhancementResult
            {
                Success = true,
                ProviderUsed = $"{settings.Provider} ({settings.ModelName})",
                Summary = $"AI enhanced test suite successfully with LLM {settings.ModelName}.",
                EnhancedTestCode = cleanCode,
                InferredEdgeCases = new List<string> { "LLM Synthesized Multi-Condition Edge Cases", "Dynamic Parameter Boundary Testing" },
                SuggestedAssertions = new List<string> { "Deep business invariants asserted via FluentAssertions" }
            };
        }

        private static string GetDefaultValueForType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "default!";
            type = type.TrimEnd('?').Trim();

            var lower = type.ToLowerInvariant();
            if (lower == "int" || lower == "int32") return "1";
            if (lower == "long" || lower == "int64") return "100L";
            if (lower == "short" || lower == "int16") return "1";
            if (lower == "byte") return "(byte)1";
            if (lower == "string") return "\"TEST_DATA\"";
            if (lower == "char") return "'A'";
            if (lower == "bool" || lower == "boolean") return "true";
            if (lower == "decimal") return "100.50m";
            if (lower == "double") return "99.99";
            if (lower == "datetime") return "DateTime.UtcNow";
            if (lower == "guid") return "Guid.NewGuid()";
            if (lower == "cancellationtoken") return "CancellationToken.None";
            if (lower.StartsWith("ienumerable<") || lower.StartsWith("ilist<") || lower.StartsWith("list<"))
            {
                var inner = type.Substring(type.IndexOf('<') + 1).TrimEnd('>');
                return $"new List<{inner}>()";
            }
            if (type.StartsWith("I") && type.Length > 1 && char.IsUpper(type[1]))
            {
                return $"Mock.Of<{type}>()";
            }

            return $"new {type}()";
        }

        private static string SanitizeName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Default";
            var chars = text.Where(char.IsLetterOrDigit).ToArray();
            var result = new string(chars);
            return result.Length > 25 ? result.Substring(0, 25) : (result.Length > 0 ? result : "ValidCondition");
        }
    }
}

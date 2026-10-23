using System.Collections.Generic;
using System.Threading.Tasks;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.Business.Ai
{
    public interface IAiTestEnhancerService
    {
        Task<AiEnhancementResult> EnhanceTestsAsync(
            ClassAnalysis classInfo, 
            string originalTestCode, 
            List<BusinessRuleModel>? businessRules, 
            AiSettingsModel? settings = null);

        Task<List<BusinessTraceabilityItem>> AnalyzeTraceabilityAsync(
            ClassAnalysis classInfo,
            List<BusinessRuleModel>? businessRules,
            AiSettingsModel? settings = null);
    }
}

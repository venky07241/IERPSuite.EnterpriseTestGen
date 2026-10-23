using System.Collections.Generic;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;

namespace IdeaInfinityIT.EnterpriseTestGen.WebClient.Models
{
    public class UnitTestGeneratorViewModel
    {
        public string PageTitle { get; set; } = "Enterprise Automated Unit Testing Platform";
        public string TargetPath { get; set; } = string.Empty;
        public string DefaultOutputTestDirectory { get; set; } = string.Empty;
        public AiSettingsModel AiSettings { get; set; } = new();
        public List<ClassAnalysis> AnalyzedClasses { get; set; } = new();
        public ProjectAnalysisResult? LastProjectAnalysis { get; set; }
    }
}

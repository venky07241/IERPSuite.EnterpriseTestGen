using System;
using System.Collections.Generic;
using System.IO;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace IdeaInfinityIT.EnterpriseTestGen.CustomCases
{
    public class CustomCaseParser
    {
        private readonly IDeserializer _deserializer;

        public CustomCaseParser()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public CustomScenarioFile ParseYaml(string yamlContent)
        {
            try
            {
                return _deserializer.Deserialize<CustomScenarioFile>(yamlContent) ?? new CustomScenarioFile();
            }
            catch (Exception ex)
            {
                return new CustomScenarioFile
                {
                    TargetClass = "Unknown",
                    CustomTestCases = new List<CustomTestCase>
                    {
                        new CustomTestCase
                        {
                            Name = "FailedToParseScenario",
                            Description = $"Error parsing custom YAML: {ex.Message}"
                        }
                    }
                };
            }
        }

        public CustomScenarioFile ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new CustomScenarioFile();

            var text = File.ReadAllText(filePath);
            return ParseYaml(text);
        }
    }
}

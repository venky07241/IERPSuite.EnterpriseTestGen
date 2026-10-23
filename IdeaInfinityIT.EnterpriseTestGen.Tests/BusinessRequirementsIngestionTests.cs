using System.Linq;
using FluentAssertions;
using IdeaInfinityIT.EnterpriseTestGen.Business;
using Xunit;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests
{
    public class BusinessRequirementsIngestionTests
    {
        private readonly BusinessDocParser _parser = new();

        [Fact]
        public void ParseOpenApiSpec_ExtractsEndpointsAndProducesStructuredRequirements()
        {
            // Arrange
            var openApiJson = @"{
  ""openapi"": ""3.0.1"",
  ""info"": {
    ""title"": ""Billing Service API"",
    ""version"": ""v1""
  },
  ""paths"": {
    ""/api/v1/bills"": {
      ""get"": {
        ""summary"": ""Get all customer bills"",
        ""operationId"": ""GetAllBills"",
        ""responses"": {
          ""200"": { ""description"": ""Success"" },
          ""401"": { ""description"": ""Unauthorized"" }
        }
      },
      ""post"": {
        ""summary"": ""Generate a new bill"",
        ""operationId"": ""GenerateBill"",
        ""responses"": {
          ""201"": { ""description"": ""Created"" },
          ""400"": { ""description"": ""Invalid input"" }
        }
      }
    }
  }
}";

            // Act
            var (spec, requirements, rules) = _parser.ParseOpenApiSpec(openApiJson, "billing_spec.json");

            // Assert
            spec.Title.Should().Be("Billing Service API");
            spec.Endpoints.Should().HaveCount(2);

            requirements.Should().HaveCount(2);
            var getReq = requirements.First(r => r.HttpMethod == "GET");
            getReq.RequirementId.Should().StartWith("OAS-");
            getReq.TargetEndpoint.Should().Be("/api/v1/bills");
            getReq.TargetMethod.Should().Be("GetAllBills");
            getReq.AcceptanceCriteria.Should().Contain("Must respond with HTTP status 200");
            getReq.AcceptanceCriteria.Should().Contain("Must respond with HTTP status 401");

            var postReq = requirements.First(r => r.HttpMethod == "POST");
            postReq.TargetMethod.Should().Be("GenerateBill");

            rules.Should().HaveCount(2);
            rules.Any(r => r.Condition.Contains("HTTP POST request")).Should().BeTrue();
        }

        [Fact]
        public void ParseMarkdownRequirements_ParsesGherkinScenariosWithStableIds()
        {
            // Arrange
            var markdown = @"
# Billing Requirements

Feature: Bill Payment Validation

Scenario: Successful Bill Payment with Valid Card
  Given customer has pending bill balance
  When payment is submitted with valid card details
  Then payment status should be approved
  And transaction receipt should be issued

Scenario: Rejected Payment when Balance is Zero
  Given customer has zero balance
  When payment is attempted
  Then system should reject payment with BadRequest error
";

            // Act
            var (requirements, rules) = _parser.ParseMarkdownRequirements(markdown, "billing_acceptance.md");

            // Assert
            requirements.Should().HaveCount(2);

            var req1 = requirements[0];
            req1.RequirementId.Should().Be("REQ-001");
            req1.Title.Should().Be("Successful Bill Payment with Valid Card");
            req1.AcceptanceCriteria.Should().HaveCount(4);
            req1.AcceptanceCriteria.Should().Contain("Then payment status should be approved");

            var req2 = requirements[1];
            req2.RequirementId.Should().Be("REQ-002");
            req2.Title.Should().Be("Rejected Payment when Balance is Zero");
            req2.Category.Should().Be("ErrorHandling");

            rules.Should().HaveCount(2);
        }

        [Fact]
        public void ParseDocumentText_ExtractsConditionalRulesFromText()
        {
            // Arrange
            var text = @"
1. If the consumer load is greater than 50kW, approval must be verified.
2. System should validate invoice date before calculation.
3. Random comment that has no requirement keywords.
";

            // Act
            var rules = _parser.ParseDocumentText(text, "sample_brd.txt");

            // Assert
            rules.Should().HaveCount(2);
            rules[0].RuleId.Should().Be("BRD-RULE-001");
            rules[0].Condition.Should().Contain("consumer load is greater than 50kW");
            rules[1].RuleId.Should().Be("BRD-RULE-002");
        }
    }
}

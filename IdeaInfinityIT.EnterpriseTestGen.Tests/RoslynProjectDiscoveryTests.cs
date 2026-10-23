using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn;
using Xunit;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests
{
    public class RoslynProjectDiscoveryTests
    {
        private readonly RoslynCodeAnalyzer _analyzer = new();

        [Fact]
        public void DiscoverProject_WithWebClientCsproj_DetectsSdkAndTargetFramework()
        {
            // Arrange
            var csprojPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", 
                "IdeaInfinityIT.EnterpriseTestGen.WebClient", "IdeaInfinityIT.EnterpriseTestGen.WebClient.csproj"));

            // Act
            var projectInfo = _analyzer.DiscoverProject(csprojPath);

            // Assert
            projectInfo.Should().NotBeNull();
            projectInfo.ProjectName.Should().Be("IdeaInfinityIT.EnterpriseTestGen.WebClient");
            projectInfo.Sdk.Should().Contain("Microsoft.NET.Sdk.Web");
            projectInfo.TargetFramework.Should().Be("net8.0");
            projectInfo.ProjectReferences.Should().NotBeEmpty();
        }

        [Fact]
        public void DiscoverSolution_WithEnterpriseGenSln_FindsProjects()
        {
            // Arrange
            var slnPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IERPSuite.EnterpriseTestGen.sln"));

            // Act
            var solutionInfo = _analyzer.DiscoverSolution(slnPath);

            // Assert
            solutionInfo.Should().NotBeNull();
            solutionInfo.SolutionName.Should().Be("IERPSuite.EnterpriseTestGen");
            solutionInfo.Projects.Should().NotBeEmpty();
            solutionInfo.Projects.Any(p => p.ProjectName.Contains("Core")).Should().BeTrue();
            solutionInfo.Projects.Any(p => p.ProjectName.Contains("Roslyn")).Should().BeTrue();
        }

        [Fact]
        public void AnalyzeSource_DetectsApiControllerAndHttpAttributesDeterministically()
        {
            // Arrange - pure ASP.NET Core API controller with no proprietary names
            var source = @"
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CustomApp.Controllers
{
    [ApiController]
    [Route(""api/v1/[controller]"")]
    public class InvoicesController : ControllerBase
    {
        private readonly IInvoiceService _invoiceService;

        public InvoicesController(IInvoiceService invoiceService)
        {
            _invoiceService = invoiceService;
        }

        [HttpGet(""{id:int}"")]
        public async Task<IActionResult> GetById(int id)
        {
            if (id <= 0) return BadRequest();
            var invoice = await _invoiceService.GetAsync(id);
            if (invoice == null) return NotFound();
            return Ok(invoice);
        }

        [HttpPost]
        public async Task<IActionResult> CreateInvoice(InvoiceDto dto)
        {
            if (dto == null) return BadRequest();
            return Ok();
        }
    }
}";

            // Act
            var classes = _analyzer.AnalyzeSource(source, "InvoicesController.cs");

            // Assert
            classes.Should().HaveCount(1);
            var cls = classes[0];
            cls.ClassName.Should().Be("InvoicesController");
            cls.IsApiController.Should().BeTrue();
            cls.IsWebController.Should().BeFalse();
            cls.RoutePrefix.Should().Be("api/v1/[controller]");
            cls.ConstructorDependencies.Should().ContainSingle(d => d.InterfaceName == "IInvoiceService");

            cls.Methods.Should().HaveCount(2);

            var getMethod = cls.Methods.First(m => m.MethodName == "GetById");
            getMethod.HttpMethod.Should().Be("GET");
            getMethod.RouteTemplate.Should().Be("{id:int}");
            getMethod.Branches.Should().Contain(b => b.BranchType == "Boundary" || b.BranchType == "NullCheck");

            var postMethod = cls.Methods.First(m => m.MethodName == "CreateInvoice");
            postMethod.HttpMethod.Should().Be("POST");
        }

        [Fact]
        public void AnalyzeSource_DetectsMvcWebControllerAndValidators()
        {
            // Arrange - Web MVC controller inheriting Controller
            var source = @"
using Microsoft.AspNetCore.Mvc;

namespace MyCompany.Web.Controllers
{
    public class OrderViewController : Controller
    {
        private readonly IOrderService _orderService;

        public OrderViewController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        public IActionResult Index()
        {
            return View();
        }
    }

    public class OrderDtoValidator : AbstractValidator<OrderDto>
    {
    }
}";

            // Act
            var classes = _analyzer.AnalyzeSource(source, "OrderViewController.cs");

            // Assert
            classes.Should().HaveCount(2);

            var controller = classes.First(c => c.ClassName == "OrderViewController");
            controller.IsWebController.Should().BeTrue();
            controller.IsApiController.Should().BeFalse();
            controller.ProjectRole.Should().Be("Web MVC Controller");

            var validator = classes.First(c => c.ClassName == "OrderDtoValidator");
            validator.IsValidator.Should().BeTrue();
            validator.ProjectRole.Should().Be("Validator");
        }

        [Fact]
        public void AnalyzeFile_UsesIncrementalCacheForUnchangedFiles()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), $"CacheTest_{Guid.NewGuid():N}.cs");
            try
            {
                File.WriteAllText(tempFile, @"
public class CacheTestService : ICacheService
{
    public bool Ping() => true;
}");

                _analyzer.ClearCache();

                // Act 1 - First parse
                var first = _analyzer.AnalyzeFile(tempFile);
                first.Should().HaveCount(1);

                // Act 2 - Second parse should hit cache
                var second = _analyzer.AnalyzeFile(tempFile);

                // Assert
                second.Should().BeSameAs(first);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void AnalyzeFile_WithWorkEvidenceController_ExtractsDependenciesAndMethods()
        {
            var controllerPath = @"g:\venkatesh\CESC\Report\cesc_mms_web\src\IdeaInfinityIT.IERP.CESC.MMS.WebClient\Controllers\Transaction\WorkEvidence\WorkEvidenceController.cs";
            if (!File.Exists(controllerPath)) return;

            var classes = _analyzer.AnalyzeFile(controllerPath);
            classes.Should().NotBeEmpty();
            var cls = classes.First(c => c.ClassName == "WorkEvidenceController");
            cls.ConstructorDependencies.Should().HaveCount(4);
            cls.Methods.Should().NotBeEmpty();

            var generator = new IdeaInfinityIT.EnterpriseTestGen.Generator.XunitMoqGenerator();
            var (code, testCases) = generator.GenerateTestClassWithMetadata(cls);
            code.Should().Contain("Mock<IWorkEvidenceService>");
            code.Should().Contain("Mock<IConfiguration>");
            code.Should().Contain("Mock<IWorkflow>");
            code.Should().Contain("Mock<IFileUploadService>");
            testCases.Should().NotBeEmpty();
        }
    }
}

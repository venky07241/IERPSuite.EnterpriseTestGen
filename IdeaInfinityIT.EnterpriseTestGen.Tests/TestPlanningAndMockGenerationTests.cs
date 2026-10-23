using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using IdeaInfinityIT.EnterpriseTestGen.Core.Models;
using IdeaInfinityIT.EnterpriseTestGen.Generator;
using Xunit;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests
{
    public class TestPlanningAndMockGenerationTests
    {
        private readonly XunitMoqGenerator _generator = new();

        [Fact]
        public void PlanTestSpecifications_CreatesStructuredMachineReadableSpecs()
        {
            // Arrange
            var classInfo = new ClassAnalysis
            {
                ClassName = "OrderService",
                Namespace = "MyCompany.Services",
                IsService = true,
                ConstructorDependencies = new List<DependencyModel>
                {
                    new DependencyModel { InterfaceName = "IOrderRepository", FieldName = "_orderRepo", ParameterName = "orderRepo" }
                },
                Methods = new List<MethodAnalysis>
                {
                    new MethodAnalysis
                    {
                        MethodName = "ProcessOrderAsync",
                        ReturnType = "Task<bool>",
                        IsAsync = true,
                        Parameters = new List<ParameterModel>
                        {
                            new ParameterModel { Name = "orderId", Type = "long" },
                            new ParameterModel { Name = "orderCode", Type = "string" }
                        },
                        InvokedDependencies = new List<InvokedDependencyMethod>
                        {
                            new InvokedDependencyMethod
                            {
                                DependencyFieldName = "_orderRepo",
                                InterfaceName = "IOrderRepository",
                                MethodName = "SaveAsync",
                                IsAsync = true,
                                ArgumentCount = 1
                            }
                        }
                    }
                }
            };

            // Act
            var specs = _generator.PlanTestSpecifications(classInfo);

            // Assert
            specs.Should().NotBeEmpty();
            specs.Count.Should().BeGreaterOrEqualTo(3); // HappyPath, NullValidation, Boundary

            var happy = specs.First(s => s.Category == "HappyPath");
            happy.ScenarioId.Should().StartWith("SPEC-");
            happy.TargetMethod.Should().Be("ProcessOrderAsync");
            happy.Inputs.Should().ContainKey("orderId");
            happy.Inputs.Should().ContainKey("orderCode");
            happy.MockSetups.Should().ContainSingle(m => m.MethodName == "SaveAsync");
            happy.ConfidenceScore.Should().Be(0.95);

            var nullSpec = specs.FirstOrDefault(s => s.Category == "NullValidation");
            nullSpec.Should().NotBeNull();
            nullSpec!.Inputs["orderCode"].Should().Be("null");

            var boundarySpec = specs.FirstOrDefault(s => s.Category == "Boundary");
            boundarySpec.Should().NotBeNull();
            boundarySpec!.Inputs["orderId"].Should().Be("-1");
        }

        [Fact]
        public void GenerateTestClass_GeneratesMockSetupsAndVerificationsForInvokedDependencies()
        {
            // Arrange
            var classInfo = new ClassAnalysis
            {
                ClassName = "PaymentController",
                Namespace = "MyCompany.Controllers",
                IsApiController = true,
                ConstructorDependencies = new List<DependencyModel>
                {
                    new DependencyModel { InterfaceName = "IPaymentGateway", FieldName = "_paymentGateway", ParameterName = "paymentGateway" }
                },
                Methods = new List<MethodAnalysis>
                {
                    new MethodAnalysis
                    {
                        MethodName = "SubmitPaymentAsync",
                        ReturnType = "Task<IActionResult>",
                        IsAsync = true,
                        Parameters = new List<ParameterModel>
                        {
                            new ParameterModel { Name = "amount", Type = "decimal" }
                        },
                        InvokedDependencies = new List<InvokedDependencyMethod>
                        {
                            new InvokedDependencyMethod
                            {
                                DependencyFieldName = "_paymentGateway",
                                InterfaceName = "IPaymentGateway",
                                MethodName = "ChargeAsync",
                                IsAsync = true,
                                ArgumentCount = 2
                            }
                        }
                    }
                }
            };

            // Act
            var code = _generator.GenerateTestClass(classInfo);

            // Assert
            code.Should().Contain("private readonly Mock<IPaymentGateway> _mockPaymentGateway;");
            code.Should().Contain("_mockPaymentGateway.Setup(x => x.ChargeAsync(It.IsAny<object>(), It.IsAny<object>()))");
            code.Should().Contain(".ReturnsAsync(true);");
            code.Should().Contain("_mockPaymentGateway.Verify(x => x.ChargeAsync(It.IsAny<object>(), It.IsAny<object>()), Times.AtLeastOnce);");
        }
    }
}

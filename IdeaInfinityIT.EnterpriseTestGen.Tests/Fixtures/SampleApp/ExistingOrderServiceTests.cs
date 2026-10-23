using System;
using Xunit;
using FluentAssertions;
using Moq;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests.Fixtures.SampleApp
{
    public class SampleOrderServiceTests
    {
        private readonly Mock<ISampleRepository> _mockSampleRepository;
        private readonly Mock<ISampleNotificationService> _mockSampleNotificationService;
        private readonly SampleOrderService _service;

        public SampleOrderServiceTests()
        {
            _mockSampleRepository = new Mock<ISampleRepository>();
            _mockSampleNotificationService = new Mock<ISampleNotificationService>();
            _service = new SampleOrderService(_mockSampleRepository.Object, _mockSampleNotificationService.Object);
        }

        [Fact]
        public void ValidateOrder_WithValidOrder_ReturnsTrue()
        {
            // Arrange
            var order = new SampleOrder { Id = 100, CustomerName = "Acme Corp", Amount = 500m };

            // Act
            var isValid = _service.ValidateOrder(order);

            // Assert
            isValid.Should().BeTrue();
        }

        [Fact]
        public void ValidateOrder_WithNullOrder_ReturnsFalse()
        {
            // Act
            var isValid = _service.ValidateOrder(null!);

            // Assert
            isValid.Should().BeFalse();
        }
    }
}

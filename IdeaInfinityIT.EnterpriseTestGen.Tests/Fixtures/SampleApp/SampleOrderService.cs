using System;
using System.Threading.Tasks;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests.Fixtures.SampleApp
{
    public class SampleOrderService
    {
        private readonly ISampleRepository _repository;
        private readonly ISampleNotificationService _notificationService;

        public SampleOrderService(ISampleRepository repository, ISampleNotificationService notificationService)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        public async Task<bool> ProcessOrderAsync(long orderId, decimal discountPercentage)
        {
            if (orderId <= 0)
            {
                throw new ArgumentException("Order ID must be positive and non-zero.", nameof(orderId));
            }

            if (discountPercentage < 0 || discountPercentage > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(discountPercentage), "Discount percentage must be between 0 and 100.");
            }

            var order = await _repository.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                throw new InvalidOperationException($"Order with identifier {orderId} was not found.");
            }

            // Conditional business branch
            if (discountPercentage > 50)
            {
                order.Amount -= order.Amount * (discountPercentage / 100m);
                order.Status = "ApprovedWithManagerOverride";
            }
            else if (discountPercentage > 0)
            {
                order.Amount -= order.Amount * (discountPercentage / 100m);
                order.Status = "Approved";
            }
            else
            {
                order.Status = "ApprovedStandard";
            }

            var saved = await _repository.SaveOrderAsync(order);
            await _notificationService.SendNotificationAsync(order.CustomerName, $"Order {orderId} processed successfully.");

            return saved;
        }

        public bool ValidateOrder(SampleOrder order)
        {
            if (order == null) return false;
            if (string.IsNullOrWhiteSpace(order.CustomerName)) return false;
            if (order.Amount <= 0) return false;

            return true;
        }

        public async Task<SampleOrder?> CancelOrderAsync(long orderId, string reason)
        {
            if (orderId <= 0)
            {
                throw new ArgumentException("Order ID must be positive.", nameof(orderId));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Cancellation reason must be provided.", nameof(reason));
            }

            var order = await _repository.GetOrderByIdAsync(orderId);
            if (order == null) return null;

            order.Status = $"Cancelled: {reason}";
            await _repository.SaveOrderAsync(order);
            await _notificationService.SendNotificationAsync(order.CustomerName, $"Your order {orderId} was cancelled.");

            return order;
        }
    }
}
